"""The classification cache, seed/ai-cache.json, keyed exactly as Domain/Ai/CacheKey.cs keys it."""

from __future__ import annotations

import hashlib
import json
import re
from dataclasses import dataclass
from pathlib import Path

_WHITESPACE = re.compile(r"\s+")


def cache_key(description: str) -> str:
    """SHA-256 hex (lower case) of the description trimmed, lower-cased and with every
    whitespace run collapsed to one space, UTF-8 encoded: CacheKey.Compute byte for byte.
    Keep both in step; the parity test checks every seed entry."""
    normalised = _WHITESPACE.sub(" ", description.strip().lower())
    return hashlib.sha256(normalised.encode("utf-8")).hexdigest()


@dataclass(frozen=True)
class CacheEntry:
    category_id: str
    confidence: float
    rationale: str


def load_cache(seed_dir: Path) -> dict[str, CacheEntry]:
    """Every entry of seed/ai-cache.json by its key. A missing or malformed file fails at
    startup, not per request."""
    raw = json.loads((seed_dir / "ai-cache.json").read_text(encoding="utf-8"))
    return {
        key: CacheEntry(
            category_id=entry["categoryId"],
            confidence=float(entry["confidence"]),
            rationale=entry.get("rationale") or "",
        )
        for key, entry in raw.items()
    }


def load_allow_list(seed_dir: Path) -> frozenset[str]:
    """The category ids of seed/categories.json, UNCLASSIFIED among them."""
    raw = json.loads((seed_dir / "categories.json").read_text(encoding="utf-8"))
    return frozenset(category["id"] for category in raw)
