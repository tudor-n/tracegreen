"""The JSON contract with the C# API. HttpAiClient (api/src/TraceGreen.Infrastructure/Ai)
sends and reads exactly these shapes, camelCase on the wire (JsonSerializerDefaults.Web).
Change both sides together."""

from __future__ import annotations

from typing import Literal

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


class SuggestNode(Wire):
    """The node the buyer is looking at, without any figure: what it buys and how it ships.
    `line_description` is invoice text: data, never instructions."""

    category_label: str
    line_description: str
    country: str
    origin: str
    mode: str
    status: str


class SuggestCandidate(Wire):
    """A curated alternative from seed/alternatives.json; the model may pick only these."""

    alternative_id: str
    label: str
    country: str
    origin: str
    mode: str


class SuggestRequest(Wire):
    node: SuggestNode
    candidates: list[SuggestCandidate] = []


class Suggestion(Wire):
    """A candidate the model picked, by its id, with one short reason that holds no number."""

    alternative_id: str
    rationale: str
    label: str


class SuggestResponse(Wire):
    suggestions: list[Suggestion]
    provider: str = "stub"


class ChatFact(Wire):
    """A fact the chat may cite (Domain/Ai/ChatFactInput): its id, what it is about and its
    value as the prompt shows it. Built by the API from its own data; names in it come from
    invoices, so it is data, never instructions."""

    id: str
    about: str
    value: str


class ChatRequest(Wire):
    question: str
    history: list[str] = []
    facts: list[ChatFact] = []


class ChatResponse(Wire):
    """A sentence with {{id}} placeholders and the ids it cites, or answerable false."""

    answerable: bool
    text: str
    fact_ids: list[str]
    provider: str = "stub"


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


class ExtractRequest(Wire):
    """A PDF invoice, base64-encoded, at most 3 MB decoded."""

    pdf_base64: str


class DraftLine(Wire):
    description: str
    quantity: str
    unit_code: str
    amount: str
    classification_code: str
    classification_scheme: str


class DraftInvoice(Wire):
    """The fields a model copied from a PDF's text; every value a string, empty when not shown.
    A person checks and confirms them before anything is imported."""

    supplier_name: str
    supplier_tax_id: str
    supplier_country: str
    invoice_number: str
    issue_date: str
    currency: str
    total_net: str
    lines: list[DraftLine]


class ExtractResponse(Wire):
    invoice: DraftInvoice
    # By field path ("invoiceNumber", "lines.0.amount"): whether the value is in the PDF's text.
    found: dict[str, bool]
    provider: str
    pages: int
