"""TraceGreen's AI service. It classifies invoice lines into the allow-listed categories and,
later, suggests alternatives; it never produces a carbon number. The C# API calls it with a
10 s timeout and falls back on its own stub, so an import never waits on this service."""

from __future__ import annotations

from fastapi import FastAPI, HTTPException

from app.cache import load_allow_list, load_cache
from app.config import Settings, settings
from app.providers import build_chain
from app.schemas import (
    ClassifyRequest,
    ClassifyResponse,
    HealthResponse,
    SuggestRequest,
    SuggestResponse,
)


def create_app(config: Settings) -> FastAPI:
    """The app for one configuration. The seed files are read here, so a broken seed fails
    at startup."""
    entries = load_cache(config.seed_dir)
    chain = build_chain(entries, load_allow_list(config.seed_dir))
    app = FastAPI(title="TraceGreen AI", version="0.1.0")

    @app.get("/health", response_model=HealthResponse)
    def health() -> HealthResponse:
        return HealthResponse(
            ok=True,
            provider=chain.name,
            cache_entries=len(entries),
            model_configured=config.model_configured,
        )

    @app.post("/classify", response_model=ClassifyResponse)
    def classify(request: ClassifyRequest) -> ClassifyResponse:
        return ClassifyResponse(
            classifications=[chain.classify(line) for line in request.lines],
            provider=chain.name,
        )

    @app.post("/suggest", response_model=SuggestResponse)
    def suggest(request: SuggestRequest) -> SuggestResponse:
        # No provider suggests yet (roadmap F11); the API shows the curated seed alternatives.
        return SuggestResponse(suggestions=[])

    @app.post("/extract-pdf")
    def extract_pdf() -> None:
        raise HTTPException(status_code=501, detail="PDF extraction is not implemented (F9).")

    return app


app = create_app(settings)
