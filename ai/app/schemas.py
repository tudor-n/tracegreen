"""The JSON contract with the C# API. HttpAiClient (api/src/TraceGreen.Infrastructure/Ai)
sends and reads exactly these shapes, camelCase on the wire (JsonSerializerDefaults.Web).
Change both sides together."""

from __future__ import annotations

from typing import Any, Literal

from pydantic import BaseModel, ConfigDict, Field
from pydantic.alias_generators import to_camel


class Wire(BaseModel):
    """camelCase on the wire, snake_case in Python; unknown fields from the API are ignored."""

    model_config = ConfigDict(alias_generator=to_camel, populate_by_name=True, extra="ignore")


class ClassificationInput(Wire):
    """One invoice line (Domain/Ai/ClassificationInput). `description` is invoice text:
    data, never instructions."""

    line_id: str
    description: str
    quantity: float | None = None
    unit_code: str | None = None
    classification_code: str | None = None
    classification_list_id: str | None = None


class ClassifyRequest(Wire):
    lines: list[ClassificationInput]


# HttpAiClient maps "cache", "stub" and "invoice" to their sources and anything else to Model.
Source = Literal["cache", "stub", "model"]


class Classification(Wire):
    line_id: str
    category_id: str
    confidence: float = Field(ge=0, le=1)
    rationale: str
    source: Source


class ClassifyResponse(Wire):
    classifications: list[Classification]
    provider: str


class SuggestRequest(Wire):
    """The API sends the whole SupplierNode and the candidate alternatives; nothing reads
    them until /suggest has a provider, so they are kept as plain JSON."""

    node: dict[str, Any]
    candidates: list[dict[str, Any]] = []


class Suggestion(Wire):
    alternative_id: str
    rationale: str
    label: str


class SuggestResponse(Wire):
    suggestions: list[Suggestion]


class ModelHealth(Wire):
    """One AI_MODELS entry: `reason` says why a model is not ready, e.g. a missing key."""

    id: str
    ready: bool
    reason: str | None = None


class HealthResponse(Wire):
    ok: bool
    provider: str
    cache_entries: int = Field(ge=0)
    # True when any model is ready.
    model_configured: bool
    # The C# HealthDto ignores this field; the evaluation script reads it.
    models: list[ModelHealth] = []
