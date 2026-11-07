"""The Ollama server's models for the chat box (OLLAMA_DISCOVER): read from GET /api/tags, chat
models only, kept for a while, and an empty list while the server does not answer. No test
reaches a real server."""

from __future__ import annotations

import dataclasses
from typing import Any

import pytest

from app.config import Settings
from app.local_models import KEEP_FOUND_S, KEEP_MISSING_S, OllamaTags, chat_capable
from app.models import chat_model_factory

TAGS = {
    "models": [
        {"name": "ministral-3:3b", "details": {"family": "mistral3", "families": ["mistral3"]}},
        {"name": "nomic-embed-text:latest", "details": {"family": "nomic-bert"}},
        {"name": "qwen2.5:14b", "details": {"family": "qwen2"}},
        {"name": "bge-m3:latest", "details": {"family": "bert"}},
    ]
}


class Server:
    """A fetch that answers as the Ollama server would, or fails, and counts its calls."""

    def __init__(self, body: Any = None, error: Exception | None = None) -> None:
        self.body, self.error = body, error
        self.urls: list[str] = []

    def __call__(self, url: str, timeout_s: float) -> Any:
        self.urls.append(url)
        assert timeout_s <= 2
        if self.error is not None:
            raise self.error
        return self.body


class Clock:
    def __init__(self) -> None:
        self.now = 100.0

    def __call__(self) -> float:
        return self.now


def test_the_chat_models_come_in_the_servers_order_without_embedding_models() -> None:
    server = Server(TAGS)

    names = OllamaTags("http://host.docker.internal:11434/", fetch=server).names()

    assert names == ("ministral-3:3b", "qwen2.5:14b")
    assert server.urls == ["http://host.docker.internal:11434/api/tags"]


def test_the_list_is_kept_for_a_while_then_read_again() -> None:
    server, clock = Server(TAGS), Clock()
    tags = OllamaTags("http://ollama:11434", fetch=server, clock=clock)

    tags.names()
    clock.now += KEEP_FOUND_S - 1
    tags.names()
    clock.now += 2
    tags.names()

    assert len(server.urls) == 2


def test_a_server_that_does_not_answer_lists_nothing_and_is_asked_again_sooner() -> None:
    server, clock = Server(error=ConnectionRefusedError()), Clock()
    tags = OllamaTags("http://ollama:11434", fetch=server, clock=clock)

    assert tags.names() == ()
    clock.now += KEEP_MISSING_S + 1
    server.error, server.body = None, TAGS
    assert tags.names() == ("ministral-3:3b", "qwen2.5:14b")


@pytest.mark.parametrize("body", [None, [], {"models": "x"}, {"models": [{"nom": "x"}]}])
def test_an_odd_answer_lists_nothing(body: Any) -> None:
    assert OllamaTags("http://ollama:11434", fetch=Server(body)).names() == ()


def test_only_named_entries_count() -> None:
    assert chat_capable({"name": "llama3.2:3b"}) is True
    assert chat_capable({"model": "llama3.2:3b"}) is False


def test_the_context_and_discovery_come_from_the_environment() -> None:
    assert (Settings.from_env({}).ollama_num_ctx, Settings.from_env({}).ollama_discover) == (
        12288,
        True,
    )
    custom = Settings.from_env({"OLLAMA_NUM_CTX": "16384", "OLLAMA_DISCOVER": "0"})
    assert (custom.ollama_num_ctx, custom.ollama_discover) == (16384, False)
    for bad in ("1024", "8k", "-1"):
        with pytest.raises(ValueError, match="OLLAMA_NUM_CTX"):
            Settings.from_env({"OLLAMA_NUM_CTX": bad})


def test_every_ollama_request_has_the_one_context_and_a_short_task_its_cap() -> None:
    config = Settings.from_env({"AI_MODELS": "ollama:local:3b", "OLLAMA_NUM_CTX": "8192"})
    (spec,) = config.models
    factory = chat_model_factory(config)

    for max_tokens, expected in ((None, None), (400, 400)):
        schema = {"title": "answer", "type": "object", "properties": {}}
        runnable = factory(dataclasses.replace(spec, max_tokens=max_tokens), schema)
        model = runnable.bound.first.bound
        options = model._chat_params([])["options"]
        assert options["num_ctx"] == 8192
        assert options.get("num_predict") == expected
