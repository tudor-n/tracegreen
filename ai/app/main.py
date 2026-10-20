from __future__ import annotations

from fastapi import FastAPI
from pydantic import BaseModel, Field

from app.config import settings

app = FastAPI(title="TraceGreen AI", version="0.1.0")


class HealthResponse(BaseModel):
    ok: bool
    provider: str
    cacheEntries: int = Field(ge=0)
    modelConfigured: bool


@app.get("/health", response_model=HealthResponse)
def health() -> HealthResponse:
    return HealthResponse(
        ok=True,
        provider="unconfigured",
        cacheEntries=0,
        modelConfigured=settings.model_configured,
    )
