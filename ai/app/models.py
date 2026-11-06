"""The chat models behind /classify, one factory per provider, built lazily: a client exists
only once the chain first calls that model, and never when no model is configured.

Every model runs with no retries and a request timeout equal to its cap, so a call the chain
has given up on ends soon after, and at temperature 0 except Anthropic's (see its factory).
The provider packages are imported inside the factories, and at startup only for ready
models, so a service with no models never loads them."""

from __future__ import annotations

import threading
from collections.abc import Callable, Iterable
from importlib import import_module
from typing import Any

from langchain_core.runnables import Runnable

from app.config import ModelSpec, Settings

# Builds the runnable for one model: chat messages in, the structured answer out.
ModelFactory = Callable[[ModelSpec, dict[str, Any]], Runnable]

# The LangChain package each provider's factory imports.
PROVIDER_PACKAGES = {
    "anthropic": "langchain_anthropic",
    "openai": "langchain_openai",
    "google_genai": "langchain_google_genai",
    "ollama": "langchain_ollama",
}


def preload_providers(specs: Iterable[ModelSpec]) -> None:
    """Imports the package of every ready model's provider. It builds no client; it only
    moves the import, up to half a second, out of the first call's cap."""
    for provider in sorted({spec.provider for spec in specs if spec.ready}):
        import_module(PROVIDER_PACKAGES[provider])


def chat_model_factory(settings: Settings) -> ModelFactory:
    """The production factory: the provider's LangChain chat model with structured output."""

    def build(spec: ModelSpec, schema: dict[str, Any]) -> Runnable:
        if spec.provider == "anthropic":
            from langchain_anthropic import ChatAnthropic

            # No temperature: claude-sonnet-5-5 and claude-fable-5 reject any but the default.
            # Structured output (output_config.format) rather than tool use, because
            # claude-opus-5-5, claude-sonnet-5-5 and claude-fable-5-1 cannot be forced to call
            # a tool and may answer in text instead.
            model = ChatAnthropic(
                model=spec.model,
                api_key=settings.api_key("anthropic"),
                max_retries=0,
                default_request_timeout=spec.timeout_s,
            )
            # ANTHROPIC_EFFORT, for a model whose profile lists that level (claude-sonnet-5-5
            # and claude-opus-5-5 do; claude-haiku-4-5 takes none). It goes in output_config, not
            # reasoning_effort, which would also turn on thinking with a summary nobody reads;
            # the model keeps its own thinking default.
            effort = settings.anthropic_effort
            if effort in ((model.profile or {}).get("reasoning_effort_levels") or ()):
                model = model.model_copy(update={"output_config": {"effort": effort}})
            return model.with_structured_output(schema, method="json_schema")
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
            # turns off a thinking mode, such as Qwen 3.5's, which would use up the budget; a
            # model without one, such as ministral-3, accepts it. num_ctx is one value for every
            # task (OLLAMA_NUM_CTX), since Ollama reloads the model when it changes; num_predict
            # caps a short task's answer (the chat), so a confused model stops in seconds.
            model = ChatOllama(
                model=spec.model,
                base_url=settings.ollama_base_url,
                temperature=0,
                reasoning=False,
                num_ctx=settings.ollama_num_ctx,
                num_predict=spec.max_tokens,
                keep_alive=settings.ollama_keep_alive,
                client_kwargs={"timeout": spec.timeout_s},
            )
            # stream=False: ChatOllama streams by default, so its timeout would bound each chunk,
            # not the call, and a call the chain gave up on would keep Ollama generating. Not
            # streamed, the timeout ends the wait and closes the connection, which stops it.
            structured = model.with_structured_output(schema, method="json_schema")
            return structured.bind(stream=False)
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
