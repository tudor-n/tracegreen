"""Structured model calls shared by every task of the service (classify, suggest, chat,
extract): one call per model with a hard deadline, the models tried in AI_MODELS order, and
the first answer that passes the task's own checks wins. A model that errs, overruns or
answers something the checks refuse is skipped, and the caller falls back on its stub."""

from __future__ import annotations

import dataclasses
import functools
import logging
import threading
import time
from collections.abc import Callable, Iterable, Sequence
from concurrent.futures import Future
from typing import Any

from langchain_core.messages import BaseMessage

from app.config import ModelSpec
from app.models import LazyModel, ModelFactory

log = logging.getLogger(__name__)

# Below this much time left, a model attempt is not started.
MIN_ATTEMPT_S = 0.2
MAX_LOGGED_ERROR_CHARS = 200


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


def ask_once(model: LazyModel, prompt: Sequence[BaseMessage], deadline: float) -> Any | None:
    """One model's raw structured output within its own cap or the time left, whichever is
    shorter; None when no time is left, or the model errs or overruns (logged in one line)."""
    cap = min(model.spec.timeout_s, deadline - time.monotonic())
    if cap < MIN_ATTEMPT_S:
        return None
    try:
        return call_with_deadline(lambda: model.runnable().invoke(list(prompt)), cap)
    except TimeoutError:
        log.warning("model %s gave no answer within %.1f s", model.id, cap)
    except Exception as error:
        # A parse error carries the whole model output, so the message is cut to one line.
        detail = " ".join(str(error).split())[:MAX_LOGGED_ERROR_CHARS]
        log.warning("model %s failed: %s: %s", model.id, type(error).__name__, detail)
    return None


def first_valid[T](
    models: Iterable[LazyModel],
    prompt: Sequence[BaseMessage],
    deadline: float,
    check: Callable[[Any], T | None],
) -> tuple[T, str] | None:
    """The first ready model's answer that `check` accepts, with that model's id; None when
    every model fails, overruns or answers something `check` refuses."""
    for model in models:
        if not model.spec.ready:
            continue
        output = ask_once(model, prompt, deadline)
        if output is None:
            continue
        checked = check(output)
        if checked is not None:
            return checked, model.id
        log.warning("model %s answer did not pass the checks", model.id)
    return None


def bind_models(
    specs: Iterable[ModelSpec],
    factory: ModelFactory,
    schema: dict[str, Any],
    cap_s: float | None = None,
) -> list[LazyModel]:
    """A lazy model per spec with this task's answer schema bound in. `cap_s` replaces each
    model's own timeout for a task with a longer budget; the client is built with it, so the
    request timeout follows."""
    bound = []
    for spec in specs:
        if cap_s is not None:
            spec = dataclasses.replace(spec, timeout_s=cap_s)
        bound.append(LazyModel(spec, functools.partial(factory, spec, schema)))
    return bound
