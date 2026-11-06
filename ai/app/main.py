"""TraceGreen's AI service. It classifies invoice lines into the allow-listed categories and,
later, suggests alternatives; it never produces a carbon number. The C# API calls it with a
10 s timeout and falls back on its own stub, so an import never waits on this service."""

from __future__ import annotations

import logging

from fastapi import FastAPI, HTTPException

from app.cache import load_cache
from app.config import API_TIMEOUT_S, Settings, settings
from app.models import ModelFactory
from app.providers import build_chain
from app.schemas import (
    ClassifyRequest,
    ClassifyResponse,
    HealthResponse,
    ModelHealth,
    SuggestRequest,
    SuggestResponse,
)

log = logging.getLogger(__name__)


def create_app(config: Settings, model_factory: ModelFactory | None = None) -> FastAPI:
    """The app for one configuration. The seed files are read here, so a broken seed fails
    at startup. Tests pass a `model_factory` of fake models; production builds the LangChain
    chat models of AI_MODELS."""
    if config.ai_timeout_s >= API_TIMEOUT_S:
        log.warning(
            "AI_TIMEOUT_S is %g s, not under the API's %g s timeout (AI_TIMEOUT_MS): the API "
            "may give up before this service answers. Use this only for scoring.",
            config.ai_timeout_s,
            API_TIMEOUT_S,
        )
    entries = load_cache(config.seed_dir)
    chain = build_chain(config, entries, model_factory)
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
        # No provider suggests yet (roadmap F11); the API shows the curated seed alternatives.
        return SuggestResponse(suggestions=[])

    @app.post("/extract-pdf")
    def extract_pdf() -> None:
        raise HTTPException(status_code=501, detail="PDF extraction is not implemented (F9).")

    return app


app = create_app(settings)
