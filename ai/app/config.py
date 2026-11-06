"""Process settings, read from the environment once at import time. A bad value fails here, so
a misconfigured service never starts."""

from __future__ import annotations

import math
import os
from collections.abc import Mapping
from dataclasses import dataclass, field
from pathlib import Path

# The provider prefix of an AI_MODELS entry and the variable that holds its key (None: no key).
PROVIDER_KEYS: dict[str, str | None] = {
    "anthropic": "ANTHROPIC_API_KEY",
    "openai": "OPENAI_API_KEY",
    "google_genai": "GOOGLE_API_KEY",
    "ollama": None,
}

DEFAULT_OLLAMA_BASE_URL = "http://host.docker.internal:11434"
# The API's default timeout for this service (AI_TIMEOUT_MS 10000). AI_TIMEOUT_S must stay
# under it, or the API may give up first; a scoring run sets it higher on purpose.
API_TIMEOUT_S = 10.0
# The API's defaults for the longer tasks (AI_CHAT_TIMEOUT_MS 20000, AI_EXTRACT_TIMEOUT_MS
# 25000); AI_CHAT_TIMEOUT_S and AI_EXTRACT_TIMEOUT_S stay under them for the same reason.
API_CHAT_TIMEOUT_S = 20.0
API_EXTRACT_TIMEOUT_S = 25.0


@dataclass(frozen=True)
class ModelSpec:
    """One AI_MODELS entry. A model whose key is missing stays in the list, not ready, so
    /health can say why; the chain skips it."""

    provider: str
    model: str
    timeout_s: float
    ready: bool
    reason: str | None
    # The most tokens one answer may take, for a task whose answer is short (the chat); None:
    # the model's own limit. Applied to Ollama models (num_predict), so a confused local model
    # stops in seconds instead of writing until its cap.
    max_tokens: int | None = None

    @property
    def id(self) -> str:
        return f"{self.provider}:{self.model}"


@dataclass(frozen=True)
class Settings:
    seed_dir: Path
    models: tuple[ModelSpec, ...]
    ai_timeout_s: float
    ollama_base_url: str
    dev_routes: bool
    # The longer tasks (chat F13, PDF extraction F9): their total budget, and the cap for each
    # model attempt, which replaces the model's own cap for those tasks.
    chat_timeout_s: float = 15.0
    extract_timeout_s: float = 20.0
    long_model_timeout_s: float = 12.0
    # AI_CHAT_MODELS: the models the chat may use, when it should not use all of AI_MODELS (the
    # chat sends supplier names and figures, which /classify never does). None: AI_MODELS.
    chat_models: tuple[ModelSpec, ...] | None = None
    # OLLAMA_NUM_CTX: Ollama's context window in tokens, the same for every task, since Ollama
    # reloads the model whenever it changes. Ollama's own 4096 is too small for the chat's facts
    # (about 5,700 tokens with eight deliveries), and it cuts a longer prompt without a word.
    ollama_num_ctx: int = 12288
    # OLLAMA_DISCOVER: the chat box also offers the models the Ollama server has (GET /api/tags),
    # to be picked by name; Automatic keeps to AI_CHAT_MODELS, else AI_MODELS.
    ollama_discover: bool = True
    # OLLAMA_KEEP_ALIVE: how long Ollama keeps a model loaded after a call ("30m", "-1" for ever),
    # sent with every call, so the next question does not pay the load (seconds, past the cap).
    ollama_keep_alive: str = "30m"
    # Keys stay out of repr, so logging the settings never prints one.
    anthropic_api_key: str | None = field(default=None, repr=False)
    openai_api_key: str | None = field(default=None, repr=False)
    google_api_key: str | None = field(default=None, repr=False)

    @property
    def chat_specs(self) -> tuple[ModelSpec, ...]:
        """The models the chat uses: AI_CHAT_MODELS when set, else AI_MODELS."""
        return self.chat_models if self.chat_models is not None else self.models

    @property
    def model_configured(self) -> bool:
        return any(spec.ready for spec in self.models)

    def api_key(self, provider: str) -> str | None:
        return {
            "anthropic": self.anthropic_api_key,
            "openai": self.openai_api_key,
            "google_genai": self.google_api_key,
        }.get(provider)

    @classmethod
    def from_env(cls, env: Mapping[str, str] | None = None) -> Settings:
        source = os.environ if env is None else env
        keys = {name: _text(source, name) for name in PROVIDER_KEYS.values() if name is not None}
        return cls(
            seed_dir=Path(source.get("SEED_DIR") or _default_seed_dir()),
            models=parse_models(
                _text(source, "AI_MODELS") or "",
                legacy_model=_text(source, "AI_MODEL"),
                keys=keys,
                cloud_timeout_s=_seconds(source, "AI_MODEL_TIMEOUT_S", 4),
                ollama_timeout_s=_seconds(source, "OLLAMA_TIMEOUT_S", 6),
            ),
            ai_timeout_s=_seconds(source, "AI_TIMEOUT_S", 8),
            chat_timeout_s=_seconds(source, "AI_CHAT_TIMEOUT_S", 15),
            extract_timeout_s=_seconds(source, "AI_EXTRACT_TIMEOUT_S", 20),
            long_model_timeout_s=_seconds(source, "AI_LONG_MODEL_TIMEOUT_S", 12),
            chat_models=parse_models(
                _text(source, "AI_CHAT_MODELS") or "",
                legacy_model=None,
                keys=keys,
                cloud_timeout_s=_seconds(source, "AI_MODEL_TIMEOUT_S", 4),
                ollama_timeout_s=_seconds(source, "OLLAMA_TIMEOUT_S", 6),
            )
            or None,
            ollama_base_url=_text(source, "OLLAMA_BASE_URL") or DEFAULT_OLLAMA_BASE_URL,
            ollama_num_ctx=_tokens(source, "OLLAMA_NUM_CTX", 12288),
            ollama_discover=_flag(source, "OLLAMA_DISCOVER", default="1"),
            ollama_keep_alive=_text(source, "OLLAMA_KEEP_ALIVE") or "30m",
            dev_routes=_flag(source, "AI_DEV_ROUTES"),
            anthropic_api_key=keys["ANTHROPIC_API_KEY"],
            openai_api_key=keys["OPENAI_API_KEY"],
            google_api_key=keys["GOOGLE_API_KEY"],
        )


def parse_models(
    raw: str,
    *,
    legacy_model: str | None,
    keys: Mapping[str, str | None],
    cloud_timeout_s: float,
    ollama_timeout_s: float,
) -> tuple[ModelSpec, ...]:
    """AI_MODELS, in order: `<provider>:<model>` entries separated by commas. The provider is
    the text before the first colon, so Ollama tags such as `ollama:qwen3.5:4b` keep theirs.
    When AI_MODELS is empty, the older AI_MODEL counts as `anthropic:<AI_MODEL>`; a leading
    `anthropic:` in it is dropped, and another provider's prefix fails."""
    entries = [entry.strip() for entry in raw.split(",") if entry.strip()]
    if not entries and legacy_model:
        prefix, colon, rest = (part.strip() for part in legacy_model.partition(":"))
        if colon and prefix == "anthropic":
            legacy_model = rest
        elif colon and prefix in PROVIDER_KEYS:
            raise ValueError(
                f"AI_MODEL is for an Anthropic model; put {legacy_model!r} in AI_MODELS"
            )
        entries = [f"anthropic:{legacy_model}"]
    specs: list[ModelSpec] = []
    for entry in entries:
        provider, colon, model = (part.strip() for part in entry.partition(":"))
        if not colon or not provider or not model:
            raise ValueError(f"AI_MODELS entry {entry!r} is not <provider>:<model>")
        if provider not in PROVIDER_KEYS:
            known = ", ".join(PROVIDER_KEYS)
            raise ValueError(f"AI_MODELS entry {entry!r}: unknown provider; use one of {known}")
        spec_id = f"{provider}:{model}"
        if any(spec.id == spec_id for spec in specs):
            raise ValueError(f"AI_MODELS lists {spec_id!r} twice")
        key_name = PROVIDER_KEYS[provider]
        ready = key_name is None or bool(keys.get(key_name))
        specs.append(
            ModelSpec(
                provider=provider,
                model=model,
                timeout_s=ollama_timeout_s if provider == "ollama" else cloud_timeout_s,
                ready=ready,
                reason=None if ready else f"{key_name} not set",
            )
        )
    return tuple(specs)


def _text(source: Mapping[str, str], name: str) -> str | None:
    return (source.get(name) or "").strip() or None


def _seconds(source: Mapping[str, str], name: str, default: float) -> float:
    raw = _text(source, name)
    if raw is None:
        return float(default)
    try:
        value = float(raw)
    except ValueError:
        raise ValueError(f"{name} must be a number of seconds, not {raw!r}") from None
    if not math.isfinite(value) or value <= 0:
        raise ValueError(f"{name} must be a positive number of seconds, not {raw!r}")
    return value


MIN_OLLAMA_NUM_CTX = 2048


def _tokens(source: Mapping[str, str], name: str, default: int) -> int:
    raw = _text(source, name)
    if raw is None:
        return default
    if not raw.isdigit() or int(raw) < MIN_OLLAMA_NUM_CTX:
        raise ValueError(f"{name} must be a whole number >= {MIN_OLLAMA_NUM_CTX}, not {raw!r}")
    return int(raw)


def _flag(source: Mapping[str, str], name: str, default: str = "0") -> bool:
    raw = _text(source, name) or default
    if raw not in ("0", "1"):
        raise ValueError(f"{name} must be 0 or 1, not {raw!r}")
    return raw == "1"


def _default_seed_dir() -> str:
    """Walk upward from this file until a directory containing seed/factors.json is found."""
    here = Path(__file__).resolve()
    for parent in here.parents:
        candidate = parent / "seed"
        if (candidate / "factors.json").exists():
            return str(candidate)
    return str(here.parents[1] / "seed")


settings = Settings.from_env()
