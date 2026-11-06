"""The chain behind /classify, one batch per call, all within AI_TIMEOUT_S:

1. The seed cache answers whatever lines it can, then the runtime cache.
2. The remaining lines go to the first ready model of AI_MODELS in one structured-output call.
   The call runs in a worker thread with a hard deadline: the model's own cap or the time
   left, whichever is shorter.
3. The answer is checked. Only line ids that were sent count, and only the first answer for
   each; confidence is clamped to 0-1 and the rationale is capped; a category outside the
   allow-list becomes UNCLASSIFIED; below 0.6 confidence the line becomes UNCLASSIFIED for a
   person to check.
4. Lines still missing go to the next ready model within the time left, then to the stub.

A model answer is kept in memory under the description's cache key and reused. A model only
picks a category id; the AI never produces a carbon number."""

from __future__ import annotations

import functools
import logging
import math
import threading
import time
from collections import OrderedDict
from collections.abc import Callable, Mapping, Sequence
from concurrent.futures import Future
from typing import Any

from pydantic import BaseModel

from app.cache import CacheEntry, cache_key, load_allow_list
from app.config import Settings
from app.models import LazyModel, ModelFactory, chat_model_factory
from app.prompts import (
    MAX_RATIONALE_CHARS,
    UNCLASSIFIED,
    answer_schema,
    load_categories,
    messages,
    system_prompt,
)
from app.schemas import Classification, ClassificationInput

log = logging.getLogger(__name__)

LOW_CONFIDENCE = 0.6
LOW_CONFIDENCE_RATIONALE = "Low confidence, check this line"
RUNTIME_CACHE_MAX_ENTRIES = 5000
# Below this much time left, a model attempt is not started.
MIN_ATTEMPT_S = 0.2


class RuntimeCache:
    """Model answers for this process by cache key, the oldest dropped past the cap."""

    def __init__(self, max_entries: int = RUNTIME_CACHE_MAX_ENTRIES) -> None:
        self._entries: OrderedDict[str, CacheEntry] = OrderedDict()
        self._max_entries = max_entries
        self._lock = threading.Lock()

    def get(self, key: str) -> CacheEntry | None:
        with self._lock:
            return self._entries.get(key)

    def put(self, key: str, entry: CacheEntry) -> None:
        with self._lock:
            self._entries[key] = entry
            self._entries.move_to_end(key)
            while len(self._entries) > self._max_entries:
                self._entries.popitem(last=False)

    def __len__(self) -> int:
        with self._lock:
            return len(self._entries)


def call_with_deadline(fn: Callable[[], Any], timeout_s: float) -> Any:
    """fn's result, run in a daemon worker thread and waited on for at most timeout_s; raises
    TimeoutError past it. An overrunning call is left to end on its own client timeout."""
    future: Future[Any] = Future()

    def run() -> None:
        try:
            future.set_result(fn())
        except Exception as error:
            future.set_exception(error)

    threading.Thread(target=run, name="ai-model-call", daemon=True).start()
    return future.result(timeout=timeout_s)


class Chain:
    def __init__(
        self,
        entries: Mapping[str, CacheEntry],
        allow_list: frozenset[str],
        models: Sequence[LazyModel],
        system: str,
        budget_s: float,
    ) -> None:
        self.allow_list = allow_list
        self.models = tuple(models)
        self.runtime_cache = RuntimeCache()
        self._entries = entries
        self._ready = tuple(m for m in self.models if m.spec.ready)
        self._system = system
        self._budget_s = budget_s

    @property
    def name(self) -> str:
        return ",".join(["cache", *(m.id for m in self._ready), "stub"])

    def model(self, model_id: str) -> LazyModel | None:
        return next((m for m in self.models if m.id == model_id), None)

    def classify(self, lines: Sequence[ClassificationInput]) -> list[Classification]:
        """One answer per line, in request order."""
        deadline = time.monotonic() + self._budget_s
        results: dict[int, Classification] = {}
        for index, line in enumerate(lines):
            cached = self._cached(line)
            if cached is not None:
                results[index] = cached
        for model in self._ready:
            pending = [i for i in range(len(lines)) if i not in results]
            if not pending:
                break
            for index, answer in self._ask(model, lines, pending, deadline).items():
                results[index] = answer
                self.runtime_cache.put(
                    cache_key(lines[index].description),
                    CacheEntry(answer.category_id, answer.confidence, answer.rationale),
                )
        reason = "AI unavailable" if self._ready else "AI not configured"
        return [results.get(i) or _stub(line, reason) for i, line in enumerate(lines)]

    def classify_with(
        self, model: LazyModel, lines: Sequence[ClassificationInput]
    ) -> list[Classification]:
        """The dev route: that model alone, no cache read or written; the stub for the rest."""
        deadline = time.monotonic() + self._budget_s
        answers = self._ask(model, lines, list(range(len(lines))), deadline) if lines else {}
        return [answers.get(i) or _stub(line, "AI unavailable") for i, line in enumerate(lines)]

    def _cached(self, line: ClassificationInput) -> Classification | None:
        key = cache_key(line.description)
        entry = self._entries.get(key)
        if entry is not None:
            return self._allowed(_answer(line, entry, "cache"))
        entry = self.runtime_cache.get(key)
        if entry is not None:
            return _answer(line, entry, "model")
        return None

    def _ask(
        self,
        model: LazyModel,
        lines: Sequence[ClassificationInput],
        pending: Sequence[int],
        deadline: float,
    ) -> dict[int, Classification]:
        """The checked answers of one model call for the pending lines, by line index; empty
        when the model errs, overruns or no time is left."""
        cap = min(model.spec.timeout_s, deadline - time.monotonic())
        if cap < MIN_ATTEMPT_S:
            return {}
        # The model sees short ids of our own, never the invoice's line ids.
        prompt_ids = {str(n): index for n, index in enumerate(pending, start=1)}
        prompt = messages(self._system, [(pid, lines[i]) for pid, i in prompt_ids.items()])
        try:
            output = call_with_deadline(lambda: model.runnable().invoke(prompt), cap)
        except TimeoutError:
            log.warning("model %s gave no answer within %.1f s", model.id, cap)
            return {}
        except Exception as error:
            log.warning("model %s failed: %s: %s", model.id, type(error).__name__, error)
            return {}
        return self._checked(output, prompt_ids, lines)

    def _checked(
        self, output: Any, prompt_ids: Mapping[str, int], lines: Sequence[ClassificationInput]
    ) -> dict[int, Classification]:
        answers = _field(output, "answers")
        if not isinstance(answers, list):
            log.warning("model answer has no answers list")
            return {}
        results: dict[int, Classification] = {}
        for item in answers:
            index = prompt_ids.get(_id(_field(item, "lineId")))
            category = _field(item, "categoryId")
            confidence = _confidence(_field(item, "confidence"))
            if index is None or index in results:
                continue  # an id that was not sent, or a second answer for one
            if not isinstance(category, str) or confidence is None:
                continue  # malformed: the line stays pending for the next model
            rationale = _field(item, "rationale")
            rationale = " ".join(rationale.split()) if isinstance(rationale, str) else ""
            answer = Classification(
                line_id=lines[index].line_id,
                category_id=category,
                confidence=confidence,
                rationale=rationale[:MAX_RATIONALE_CHARS],
                source="model",
            )
            results[index] = self._confident(answer)
        return results

    def _confident(self, answer: Classification) -> Classification:
        """The allow-list first, then the confidence threshold."""
        if answer.category_id not in self.allow_list:
            return self._allowed(answer)
        if answer.confidence < LOW_CONFIDENCE:
            return answer.model_copy(
                update={
                    "category_id": UNCLASSIFIED,
                    "confidence": 0.0,
                    "rationale": LOW_CONFIDENCE_RATIONALE,
                }
            )
        return answer

    def _allowed(self, answer: Classification) -> Classification:
        """Domain/Ai/LineClassification.EnforceAllowList, mirrored: a category outside the
        list is UNCLASSIFIED."""
        if answer.category_id in self.allow_list:
            return answer
        return answer.model_copy(
            update={
                "category_id": UNCLASSIFIED,
                "confidence": 0.0,
                "rationale": f"Category outside the allow-list: {answer.category_id}",
            }
        )


def build_chain(
    config: Settings,
    entries: Mapping[str, CacheEntry],
    model_factory: ModelFactory | None = None,
) -> Chain:
    """The chain for one configuration. The prompt and the answer schema are built here, once;
    no model client is built until a model is first called."""
    categories = load_categories(config.seed_dir)
    schema = answer_schema(categories)
    factory = model_factory or chat_model_factory(config)
    models = [LazyModel(spec, functools.partial(factory, spec, schema)) for spec in config.models]
    return Chain(
        entries=entries,
        allow_list=load_allow_list(config.seed_dir),
        models=models,
        system=system_prompt(categories),
        budget_s=config.ai_timeout_s,
    )


def _answer(line: ClassificationInput, entry: CacheEntry, source: str) -> Classification:
    return Classification(
        line_id=line.line_id,
        category_id=entry.category_id,
        confidence=entry.confidence,
        rationale=entry.rationale,
        source=source,
    )


def _stub(line: ClassificationInput, reason: str) -> Classification:
    """The last resort, answering like the C# StubAiClient: UNCLASSIFIED with no confidence."""
    return Classification(
        line_id=line.line_id,
        category_id=UNCLASSIFIED,
        confidence=0.0,
        rationale=reason,
        source="stub",
    )


def _field(value: Any, name: str) -> Any:
    """A field of a structured answer, whether the parser gave a dict or a pydantic model."""
    if isinstance(value, Mapping):
        return value.get(name)
    if isinstance(value, BaseModel):
        return getattr(value, name, None)
    return None


def _id(value: Any) -> str | None:
    if isinstance(value, bool):
        return None
    if isinstance(value, str | int):
        return str(value).strip()
    return None


def _confidence(value: Any) -> float | None:
    """A number clamped to 0-1, or None when it is not a number."""
    if isinstance(value, bool):
        return None
    try:
        number = float(value)
    except (TypeError, ValueError):
        return None
    if math.isnan(number):
        return None
    return min(1.0, max(0.0, number))
