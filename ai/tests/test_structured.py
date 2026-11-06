"""The shared structured-call helper: deadlines, model order and the task's own checks."""

from __future__ import annotations

import threading
import time
from typing import Any

from langchain_core.messages import HumanMessage
from langchain_core.runnables import RunnableLambda

from app.config import ModelSpec
from app.structured import ask_once, bind_models, first_valid

PROMPT = [HumanMessage(content="question")]


def spec(name: str, *, ready: bool = True, timeout_s: float = 1.0) -> ModelSpec:
    return ModelSpec(provider="openai", model=name, timeout_s=timeout_s, ready=ready, reason=None)


def models(*specs: ModelSpec, answers: dict[str, Any], calls: list[str] | None = None):
    """Lazy models whose runnable answers from `answers` by model name, an Exception raising."""

    def factory(s: ModelSpec, schema: dict[str, Any]):
        def invoke(_messages: Any) -> Any:
            if calls is not None:
                calls.append(s.model)
            value = answers[s.model]
            if isinstance(value, Exception):
                raise value
            if isinstance(value, threading.Event):
                value.wait(5)
                return {"late": True}
            return value

        return RunnableLambda(invoke)

    return bind_models(specs, factory, {"title": "test"})


def test_first_valid_returns_the_first_answer_the_check_accepts_with_its_model_id() -> None:
    calls: list[str] = []
    bound = models(
        spec("a"),
        spec("b"),
        spec("c"),
        answers={"a": {"ok": False}, "b": {"ok": True}, "c": {"ok": True}},
        calls=calls,
    )

    result = first_valid(
        bound, PROMPT, time.monotonic() + 5, lambda out: out if out["ok"] else None
    )

    assert result == ({"ok": True}, "openai:b")
    assert calls == ["a", "b"]


def test_first_valid_skips_a_model_that_errs_and_one_that_is_not_ready() -> None:
    calls: list[str] = []
    bound = models(
        spec("a"),
        spec("off", ready=False),
        spec("b"),
        answers={"a": RuntimeError("boom"), "off": {"ok": True}, "b": {"ok": True}},
        calls=calls,
    )

    assert first_valid(bound, PROMPT, time.monotonic() + 5, lambda out: out) == (
        {"ok": True},
        "openai:b",
    )
    assert calls == ["a", "b"]


def test_first_valid_is_none_when_every_model_fails_the_check() -> None:
    bound = models(spec("a"), spec("b"), answers={"a": {"ok": False}, "b": {"ok": False}})

    assert (
        first_valid(bound, PROMPT, time.monotonic() + 5, lambda out: out if out["ok"] else None)
        is None
    )


def test_ask_once_gives_up_at_the_model_cap() -> None:
    hang = threading.Event()
    (bound,) = models(spec("slow", timeout_s=0.3), answers={"slow": hang})

    started = time.monotonic()
    assert ask_once(bound, PROMPT, time.monotonic() + 5) is None
    assert time.monotonic() - started < 1.0
    hang.set()


def test_ask_once_starts_nothing_without_time_left() -> None:
    calls: list[str] = []
    (bound,) = models(spec("a"), answers={"a": {"ok": True}}, calls=calls)

    assert ask_once(bound, PROMPT, time.monotonic() + 0.05) is None
    assert calls == []


def test_bind_models_can_give_a_task_its_own_cap() -> None:
    bound = models(spec("a", timeout_s=4.0), answers={"a": {}})
    longer = bind_models(
        [spec("a", timeout_s=4.0)], lambda s, schema: RunnableLambda(lambda _: {}), {}, cap_s=12.0
    )

    assert bound[0].spec.timeout_s == 4.0
    assert longer[0].spec.timeout_s == 12.0
