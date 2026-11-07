"""The models the local Ollama server has (GET /api/tags), so the chat box can offer them with no
AI_CHAT_MODELS set (OLLAMA_DISCOVER, on by default). A model found this way is asked only when a
person picks it by name; Automatic keeps to AI_CHAT_MODELS, else AI_MODELS. The list is read
with a short timeout and kept for a while, so the chat box never waits long on a server that is
not running."""

from __future__ import annotations

import json
import logging
import threading
import time
import urllib.request
from collections.abc import Callable
from typing import Any

log = logging.getLogger(__name__)

TAGS_TIMEOUT_S = 1.5
# How long a list is kept: a model pulled meanwhile shows within this; a server that did not
# answer is asked again sooner.
KEEP_FOUND_S = 30.0
KEEP_MISSING_S = 10.0
# Loading a model can take a while on first use; the warm-up runs in the background.
WARM_UP_TIMEOUT_S = 120.0

# GETs a URL within a timeout and returns the parsed JSON body; raises on any failure.
Fetch = Callable[[str, float], Any]


def fetch_json(url: str, timeout_s: float) -> Any:
    with urllib.request.urlopen(url, timeout=timeout_s) as response:
        return json.load(response)


def warm_up(base_url: str, name: str, num_ctx: int, keep_alive: str) -> bool:
    """Loads a model into Ollama with the context the tasks use, so the first question does not
    wait for the load: an empty prompt loads the model and answers nothing. False on failure."""
    url = base_url.rstrip("/") + "/api/generate"
    body = {"model": name, "prompt": "", "keep_alive": keep_alive, "options": {"num_ctx": num_ctx}}
    try:
        request = urllib.request.Request(
            url, json.dumps(body).encode(), {"content-type": "application/json"}
        )
        with urllib.request.urlopen(request, timeout=WARM_UP_TIMEOUT_S) as response:
            response.read()
        log.info("model ollama:%s loaded", name)
        return True
    except Exception as error:
        log.info("model ollama:%s not loaded ahead: %s", name, type(error).__name__)
        return False


def chat_capable(entry: Any) -> bool:
    """False for an embedding model, which cannot write an answer: its name or family says so."""
    if not isinstance(entry, dict) or not isinstance(entry.get("name"), str):
        return False
    details = entry.get("details") if isinstance(entry.get("details"), dict) else {}
    families = [details.get("family"), *(details.get("families") or [])]
    words = [entry["name"].lower(), *(str(f).lower() for f in families if f)]
    return not any("embed" in w or "bert" in w for w in words)


class OllamaTags:
    """The chat-capable models of the Ollama server at `base_url`, by name (e.g. `qwen2.5:14b`),
    in the server's order; empty while the server does not answer."""

    def __init__(
        self,
        base_url: str,
        fetch: Fetch = fetch_json,
        clock: Callable[[], float] = time.monotonic,
    ) -> None:
        self._url = base_url.rstrip("/") + "/api/tags"
        self._fetch = fetch
        self._clock = clock
        self._names: tuple[str, ...] = ()
        self._until = float("-inf")
        self._lock = threading.Lock()

    def names(self) -> tuple[str, ...]:
        with self._lock:
            if self._clock() < self._until:
                return self._names
            try:
                body = self._fetch(self._url, TAGS_TIMEOUT_S)
                entries = body.get("models", []) if isinstance(body, dict) else []
                found = tuple(dict.fromkeys(e["name"] for e in entries if chat_capable(e)))
                keep = KEEP_FOUND_S
            except Exception as error:
                log.info("no local models from %s: %s", self._url, type(error).__name__)
                found, keep = (), KEEP_MISSING_S
            self._names, self._until = found, self._clock() + keep
            return found
