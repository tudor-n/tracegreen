from __future__ import annotations

import os
from dataclasses import dataclass
from pathlib import Path


@dataclass(frozen=True)
class Settings:
    """Process settings read from the environment once at import time."""

    seed_dir: Path
    ai_model: str | None
    anthropic_api_key: str | None
    ai_timeout_s: float

    @property
    def model_configured(self) -> bool:
        return bool(self.ai_model) and bool(self.anthropic_api_key)

    @classmethod
    def from_env(cls, env: dict[str, str] | None = None) -> Settings:
        source = os.environ if env is None else env
        seed_dir = Path(source.get("SEED_DIR") or _default_seed_dir())
        return cls(
            seed_dir=seed_dir,
            ai_model=(source.get("AI_MODEL") or "").strip() or None,
            anthropic_api_key=(source.get("ANTHROPIC_API_KEY") or "").strip() or None,
            ai_timeout_s=float(source.get("AI_TIMEOUT_S") or 10),
        )


def _default_seed_dir() -> str:
    """Walk upward from this file until a directory containing seed/factors.json is found."""
    here = Path(__file__).resolve()
    for parent in here.parents:
        candidate = parent / "seed"
        if (candidate / "factors.json").exists():
            return str(candidate)
    return str(here.parents[1] / "seed")


settings = Settings.from_env()
