"""The AI service's contract with the C# API (HttpAiClient), its cache and its stub (task C7)."""

from __future__ import annotations

import json
from pathlib import Path

import pytest
from fastapi.testclient import TestClient

from app.cache import cache_key, load_cache
from app.config import Settings
from app.main import create_app

SEED = Path(__file__).resolve().parents[2] / "seed"


def settings(seed_dir: Path = SEED, **env: str) -> Settings:
    return Settings.from_env({"SEED_DIR": str(seed_dir), **env})


def client(seed_dir: Path = SEED, **env: str) -> TestClient:
    return TestClient(create_app(settings(seed_dir, **env)))


def line(line_id: str, description: str, **extra: object) -> dict[str, object]:
    """One line as HttpAiClient serialises ClassificationInput: camelCase, nulls included."""
    return {
        "lineId": line_id,
        "description": description,
        "quantity": 4000.0,
        "unitCode": "H87",
        "classificationCode": None,
        "classificationListId": None,
        **extra,
    }


def classify(c: TestClient, *lines: dict[str, object]) -> dict[str, object]:
    response = c.post("/classify", json={"lines": list(lines)})
    assert response.status_code == 200, response.text
    return response.json()


def test_a_cached_description_is_served_from_the_cache_whatever_its_case_and_spacing() -> None:
    body = classify(client(), line("L1", "  zinc DIE-CAST   sensor housings,\ttype ZH-220 "))
    assert body["classifications"] == [
        {
            "lineId": "L1",
            "categoryId": "zinc_diecast",
            "confidence": 0.92,
            "rationale": "Die-cast zinc housing for a sensor module",
            "source": "cache",
        }
    ]
    assert body["provider"] == "cache,stub"


def test_an_unknown_description_falls_back_to_the_stub() -> None:
    body = classify(client(), line("L2", "Titanium bolts, M6"))
    assert body["classifications"] == [
        {
            "lineId": "L2",
            "categoryId": "UNCLASSIFIED",
            "confidence": 0.0,
            "rationale": "AI not configured",
            "source": "stub",
        }
    ]


def test_every_line_gets_exactly_one_answer_in_request_order() -> None:
    body = classify(
        client(),
        line("a", "Titanium bolts, M6"),
        line("b", "4-layer FR4 printed circuit boards, 120x80 mm"),
    )
    assert [c["lineId"] for c in body["classifications"]] == ["a", "b"]
    assert [c["source"] for c in body["classifications"]] == ["stub", "cache"]


def test_a_cached_category_outside_the_allow_list_becomes_unclassified(tmp_path: Path) -> None:
    (tmp_path / "categories.json").write_text(
        json.dumps([{"id": "pcb"}, {"id": "UNCLASSIFIED"}]), encoding="utf-8"
    )
    description = "Steel brackets"
    cache = {
        cache_key(description): {
            "categoryId": "steel",
            "confidence": 0.9,
            "rationale": "Steel part",
            "source": "cache",
            "description": description,
        }
    }
    (tmp_path / "ai-cache.json").write_text(json.dumps(cache), encoding="utf-8")
    body = classify(client(tmp_path), line("L3", description))
    assert body["classifications"] == [
        {
            "lineId": "L3",
            "categoryId": "UNCLASSIFIED",
            "confidence": 0.0,
            "rationale": "Category outside the allow-list: steel",
            "source": "cache",
        }
    ]


def test_every_cache_entry_hashes_back_to_its_own_key() -> None:
    """Parity with Domain/Ai/CacheKey.cs, which computed these keys."""
    raw = json.loads((SEED / "ai-cache.json").read_text(encoding="utf-8"))
    assert raw, "seed/ai-cache.json is empty"
    for key, entry in raw.items():
        assert cache_key(entry["description"]) == key, entry["description"]
    assert len(load_cache(SEED)) == len(raw)


def test_the_cache_key_trims_lower_cases_and_collapses_whitespace() -> None:
    assert cache_key("  A\t\nB  c ") == cache_key("a b c")
    # Lower-case hex, as Hashing.Sha256Hex writes it.
    key = cache_key("a b c")
    assert len(key) == 64 and key == key.lower() and int(key, 16) >= 0


def test_the_adversarial_line_comes_back_allow_listed_or_unclassified() -> None:
    adversarial = next(
        e["description"]
        for e in json.loads((SEED / "ai-cache.json").read_text(encoding="utf-8")).values()
        if "IGNORE PREVIOUS INSTRUCTIONS" in e["description"]
    )
    allow_list = {c["id"] for c in json.loads((SEED / "categories.json").read_text("utf-8"))}
    body = classify(client(), line("L4", adversarial))
    (answer,) = body["classifications"]
    assert answer["categoryId"] in allow_list


@pytest.mark.parametrize(
    "env",
    [{}, {"AI_MODEL": "some-model"}, {"ANTHROPIC_API_KEY": "sk-test"}],
    ids=["neither", "model only", "key only"],
)
def test_no_anthropic_client_is_built_unless_model_and_key_are_both_set(
    monkeypatch: pytest.MonkeyPatch, env: dict[str, str]
) -> None:
    import anthropic

    def refuse(*_args: object, **_kwargs: object) -> None:
        raise AssertionError("an Anthropic client was constructed")

    monkeypatch.setattr(anthropic, "Anthropic", refuse)
    monkeypatch.setattr(anthropic, "AsyncAnthropic", refuse)
    c = client(**env)
    classify(c, line("L5", "Titanium bolts, M6"))
    assert c.get("/health").json()["modelConfigured"] is False


def test_health_reports_the_provider_chain_and_the_cache_size() -> None:
    entries = len(json.loads((SEED / "ai-cache.json").read_text(encoding="utf-8")))
    body = client().get("/health").json()
    assert body == {
        "ok": True,
        "provider": "cache,stub",
        "cacheEntries": entries,
        "modelConfigured": False,
    }


def test_suggest_accepts_the_api_request_and_returns_no_suggestions_yet() -> None:
    request = {
        "node": {"id": "11111111-1111-4111-8111-000000000001", "categoryId": "zinc_diecast"},
        "candidates": [{"id": "N1-alt", "label": "Alternative", "nodeCategory": "zinc_diecast"}],
    }
    response = client().post("/suggest", json=request)
    assert response.status_code == 200
    assert response.json() == {"suggestions": []}


def test_extract_pdf_is_not_implemented() -> None:
    assert client().post("/extract-pdf").status_code == 501
