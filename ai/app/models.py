"""The chat models behind /classify, one factory per provider, built lazily: a client exists
only once the chain first calls that model, and never when no model is configured.

Every model runs at temperature 0, with no retries and a request timeout equal to its cap,
so a call the chain has given up on ends soon after. The provider packages are imported
inside the factories, so a service with no models never loads them."""

from __future__ import annotations

import threading
from collections.abc import Callable
from typing import Any

from langchain_core.runnables import Runnable

from app.config import ModelSpec, Settings

# Builds the runnable for one model: chat messages in, the structured answer out.
ModelFactory = Callable[[ModelSpec, dict[str, Any]], Runnable]


def chat_model_factory(settings: Settings) -> ModelFactory:
    """The production factory: the provider's LangChain chat model with structured output."""

    def build(spec: ModelSpec, schema: dict[str, Any]) -> Runnable:
        if spec.provider == "anthropic":
            from langchain_anthropic import ChatAnthropic

            model = ChatAnthropic(
                model=spec.model,
                api_key=settings.api_key("anthropic"),
                temperature=0,
                max_retries=0,
                default_request_timeout=spec.timeout_s,
            )
            # Tool use, with the tool forced.
            return model.with_structured_output(schema)
        if spec.provider == "openai":
            from langchain_openai import ChatOpenAI

            model = ChatOpenAI(
                model=spec.model,
                api_key=settings.api_key("openai"),
                temperature=0,
                max_retries=0,
                request_timeout=spec.timeout_s,
            )
            return model.with_structured_output(schema, method="json_schema", strict=True)
        if spec.provider == "google_genai":
            from langchain_google_genai import ChatGoogleGenerativeAI

            model = ChatGoogleGenerativeAI(
                model=spec.model,
                api_key=settings.api_key("google_genai"),
                temperature=0,
                max_retries=0,
                timeout=spec.timeout_s,
            )
            return model.with_structured_output(schema, method="json_schema")
        if spec.provider == "ollama":
            from langchain_ollama import ChatOllama

            # ChatOllama has no retry setting: its HTTP client never retries. reasoning=False
            # turns off Qwen 3.5's thinking mode, which would use up the budget.
            model = ChatOllama(
                model=spec.model,
                base_url=settings.ollama_base_url,
                temperature=0,
                reasoning=False,
                client_kwargs={"timeout": spec.timeout_s},
            )
            return model.with_structured_output(schema, method="json_schema")
        raise ValueError(f"no factory for provider {spec.provider!r}")

    return build


class LazyModel:
    """One model of the chain. Its runnable is built on the first call, once, even when two
    requests reach it at the same time."""

    def __init__(self, spec: ModelSpec, build: Callable[[], Runnable]) -> None:
        self.spec = spec
        self._build = build
        self._runnable: Runnable | None = None
        self._lock = threading.Lock()

    @property
    def id(self) -> str:
        return self.spec.id

    @property
    def built(self) -> bool:
        return self._runnable is not None

    def runnable(self) -> Runnable:
        if self._runnable is None:
            with self._lock:
                if self._runnable is None:
                    self._runnable = self._build()
        return self._runnable
