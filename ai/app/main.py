"""TraceGreen's AI service. It classifies invoice lines into the allow-listed categories and
picks, among the curated alternatives it is sent, the ones that fit; it never produces a
carbon number. The C# API calls it with a
10 s timeout and falls back on its own stub, so an import never waits on this service."""

from __future__ import annotations

import logging
from collections.abc import Callable, Sequence

from fastapi import FastAPI, HTTPException

from app.cache import load_cache
from app.chat import ChatModelRefused, build_chat
from app.config import (
    API_CHAT_TIMEOUT_S,
    API_EXTRACT_TIMEOUT_S,
    API_TIMEOUT_S,
    Settings,
    settings,
)
from app.extract import build_extractor
from app.local_models import OllamaTags
from app.models import ModelFactory, chat_model_factory
from app.pdf import PdfRefused, decode, text_of
from app.providers import build_chain
from app.schemas import (
    ChatModel,
    ChatModelsResponse,
    ChatRequest,
    ChatResponse,
    ClassifyRequest,
    ClassifyResponse,
    ExtractRequest,
    ExtractResponse,
    HealthResponse,
    ModelHealth,
    SuggestRequest,
    SuggestResponse,
)
from app.suggest import build_suggester

log = logging.getLogger(__name__)


def create_app(
    config: Settings,
    model_factory: ModelFactory | None = None,
    local_models: Callable[[], Sequence[str]] | None = None,
) -> FastAPI:
    """The app for one configuration. The seed files are read here, so a broken seed fails
    at startup. Tests pass a `model_factory` of fake models; production builds the LangChain
    chat models of AI_MODELS. `local_models` lists the Ollama server's models for the chat box;
    by default the server is asked (GET /api/tags) when OLLAMA_DISCOVER is on."""
    if config.ai_timeout_s >= API_TIMEOUT_S:
        log.warning(
            "AI_TIMEOUT_S is %g s, not under the API's %g s timeout (AI_TIMEOUT_MS): the API "
            "may give up before this service answers. Use this only for scoring.",
            config.ai_timeout_s,
            API_TIMEOUT_S,
        )
    for name, budget, api in (
        ("AI_CHAT_TIMEOUT_S", config.chat_timeout_s, API_CHAT_TIMEOUT_S),
        ("AI_EXTRACT_TIMEOUT_S", config.extract_timeout_s, API_EXTRACT_TIMEOUT_S),
    ):
        if budget >= api:
            log.warning(
                "%s is %g s, not under the API's %g s default: the API may give up first.",
                name,
                budget,
                api,
            )
    entries = load_cache(config.seed_dir)
    chain = build_chain(config, entries, model_factory)
    # Every task builds its models lazily from the same factory, so no client exists before a
    # model is first called.
    factory = model_factory or chat_model_factory(config)
    suggester = build_suggester(config, factory)
    if local_models is None and config.ollama_discover:
        local_models = OllamaTags(config.ollama_base_url).names
    chat_task = build_chat(config, factory, local_models)
    extractor = build_extractor(config, factory)
    app = FastAPI(title="TraceGreen AI", version="0.1.0")

    @app.get("/health", response_model=HealthResponse)
    def health() -> HealthResponse:
        return HealthResponse(
            ok=True,
            provider=chain.name,
            cache_entries=len(entries),
            model_configured=config.model_configured,
            models=[
                ModelHealth(id=spec.id, ready=spec.ready, reason=spec.reason)
                for spec in config.models
            ],
        )

    @app.post("/classify", response_model=ClassifyResponse)
    def classify(request: ClassifyRequest, model: str | None = None) -> ClassifyResponse:
        # `model` is the scoring dev route, on only with AI_DEV_ROUTES=1: that model alone,
        # no cache. Otherwise the parameter is ignored.
        if config.dev_routes and model is not None:
            chosen = chain.model(model)
            if chosen is None:
                raise HTTPException(status_code=400, detail=f"Unknown model: {model}")
            if not chosen.spec.ready:
                raise HTTPException(
                    status_code=400, detail=f"Model not ready: {model} ({chosen.spec.reason})"
                )
            return ClassifyResponse(
                classifications=chain.classify_with(chosen, request.lines), provider=chosen.id
            )
        return ClassifyResponse(classifications=chain.classify(request.lines), provider=chain.name)

    @app.post("/suggest", response_model=SuggestResponse)
    def suggest(request: SuggestRequest) -> SuggestResponse:
        # F11: a pick among the curated candidates the API sends, never a new supplier or a
        # figure; no model or no valid answer gives none, and the API shows the list as it is.
        return suggester.suggest(request)

    @app.get("/chat/models", response_model=ChatModelsResponse)
    def chat_models() -> ChatModelsResponse:
        # The models the chat box offers: the chat's own in the order Automatic tries them, a
        # model without its key listed, not ready, with the reason; then the Ollama server's
        # other models, which are asked only when picked.
        return ChatModelsResponse(
            models=[
                ChatModel(
                    id=spec.id,
                    provider=spec.provider,
                    model=spec.model,
                    ready=spec.ready,
                    reason=spec.reason,
                    automatic=automatic,
                )
                for spec, automatic in chat_task.listed()
            ]
        )

    @app.post("/chat", response_model=ChatResponse)
    def chat(request: ChatRequest) -> ChatResponse:
        # F13: a sentence that cites the API's facts by id; the API checks it again and fills
        # the values in. No model, or no valid answer, says the question was not answered.
        # A named model (`model`) answers alone; one the chat lacks or cannot use is 422.
        try:
            return chat_task.answer(request)
        except ChatModelRefused as refused:
            raise HTTPException(
                status_code=422, detail={"code": refused.code, "message": str(refused)}
            ) from None

    @app.post("/extract-pdf", response_model=ExtractResponse)
    def extract_pdf(request: ExtractRequest) -> ExtractResponse:
        # F9: the PDF's text layer, then a model copies its fields into the draft. A refused
        # PDF is 422 with a code; no model, or none that answered, is 503. Nothing is imported.
        try:
            data = decode(request.pdf_base64)
            text, pages = text_of(data)
        except PdfRefused as refused:
            raise HTTPException(
                status_code=422, detail={"code": refused.code, "message": str(refused)}
            ) from None
        draft = extractor.extract(text, pages=pages)
        if draft is None:
            raise HTTPException(
                status_code=503,
                detail={"code": "unavailable", "message": "No model could read the invoice."},
            )
        return draft

    return app


app = create_app(settings)
