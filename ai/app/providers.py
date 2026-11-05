"""The provider chain for /classify: the cache, then the stub. A live model joins the chain
between them only when AI_MODEL and ANTHROPIC_API_KEY are both set, and not before D4's
hackathon slot (docs/DECISIONS.md); until then no Anthropic client is ever built. Whatever
answers, the result is checked against the allow-list."""

from __future__ import annotations

from dataclasses import dataclass
from typing import Protocol

from app.cache import CacheEntry, cache_key
from app.schemas import Classification, ClassificationInput

UNCLASSIFIED = "UNCLASSIFIED"


class Provider(Protocol):
    name: str

    def classify(self, line: ClassificationInput) -> Classification | None:
        """The answer for one line, or None to pass it to the next provider."""
        ...


@dataclass(frozen=True)
class CacheProvider:
    entries: dict[str, CacheEntry]
    name: str = "cache"

    def classify(self, line: ClassificationInput) -> Classification | None:
        entry = self.entries.get(cache_key(line.description))
        if entry is None:
            return None
        return Classification(
            line_id=line.line_id,
            category_id=entry.category_id,
            confidence=entry.confidence,
            rationale=entry.rationale,
            source="cache",
        )


@dataclass(frozen=True)
class StubProvider:
    """The last resort, answering like the C# StubAiClient: UNCLASSIFIED with no confidence."""

    name: str = "stub"

    def classify(self, line: ClassificationInput) -> Classification:
        return Classification(
            line_id=line.line_id,
            category_id=UNCLASSIFIED,
            confidence=0.0,
            rationale="AI not configured",
            source="stub",
        )


@dataclass(frozen=True)
class Chain:
    providers: tuple[Provider, ...]
    allow_list: frozenset[str]

    @property
    def name(self) -> str:
        return ",".join(p.name for p in self.providers)

    def classify(self, line: ClassificationInput) -> Classification:
        for provider in self.providers:
            answer = provider.classify(line)
            if answer is not None:
                return self._allowed(answer)
        raise RuntimeError("the provider chain must end with the stub")

    def _allowed(self, answer: Classification) -> Classification:
        """Domain/Ai/LineClassification.EnforceAllowList, mirrored: a category outside the
        list is UNCLASSIFIED."""
        if answer.category_id in self.allow_list:
            return answer
        return answer.model_copy(
            update={
                "category_id": UNCLASSIFIED,
                "confidence": 0.0,
                "rationale": f"Category outside the allow-list: {answer.category_id}",
            }
        )


def build_chain(entries: dict[str, CacheEntry], allow_list: frozenset[str]) -> Chain:
    return Chain(providers=(CacheProvider(entries), StubProvider()), allow_list=allow_list)
