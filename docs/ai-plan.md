# AI plan: multi-model classifier (F6)

Goal: make the AI service classify invoice lines with live models through LangChain, with 2 cloud models and 1 local model behind one chain, in about 4.5 hours of wall-clock time using 3 Claude Code terminals in parallel.

- No `web/` changes (the designers own the UI).
- The C# wire contract stays the same.
- The AI never produces a carbon number.

## Scope

**In**

- LangChain chain: cache → cloud model 1 → cloud model 2 → local Qwen 3.5 (Ollama) → stub, within 8 s.
- More deterministic rules before the AI: HS/NC codes in `seed/classification-map.json`.
- 3 Romanian demo invoices.
- A Romanian test set with a scoring script and a results table.
- A failover and offline demo.
- Docs.

**Out (later or roadmap)**

- Learning from buyer corrections.
- A background classification queue.
- PDF import (F9).
- Splitting an invoice across categories per line.
- "Explain this supplier".
- Anything in `web/`.

## How the terminals stay out of each other's way

Each terminal gets its own git worktree and branch, so they never share files, the git index or a lock file:

```
git worktree add ../TG-ai-core  -b f6/ai-core       # Terminal A
git worktree add ../TG-ai-eval  -b f6/ai-eval       # Terminal B
git worktree add ../TG-ai-data  -b f6/ai-data-docs  # Terminal C
```

| Terminal | Owns (only these) | Never touches |
|---|---|---|
| **A: AI core** | `ai/app/**`, `ai/tests/**`, `ai/pyproject.toml`, `ai/uv.lock` | everything else |
| **B: Evaluation** | `ai/eval/**` (new), `scripts/ai-eval.mjs`, `scripts/lib/ai-eval.mjs`, `scripts/lib/ai-eval.test.mjs`, `docs/ai-eval.md` (new) | `ai/app`, `ai/tests`, `seed/`, any other doc |
| **C: Data, config, docs** | `seed/classification-map.json`, `seed/invoices/R*.xml` (new), `compose.yml`, `.env.example`, `CLAUDE.md`, `docs/{DECISIONS,ROADMAP,NEXT,RUNBOOK,SECURITY,ARCHITECTURE}.md` | `ai/**`, `scripts/**` |

## Fixed agreements

All three terminals build against these. Nobody changes them without telling the other two.

### 1. `/classify` contract

The request and response are unchanged (`ai/app/schemas.py` ↔ `HttpAiClient`). Model answers use `source: "model"`, which the C# side already maps to `ClassificationSource.Model`.

### 2. Environment variables

| Variable | Default | Meaning |
|---|---|---|
| `AI_MODELS` | empty (cache and stub only) | Ordered list, e.g. `openai:<model>,google_genai:<model>,ollama:qwen3.5:4b`. The text before the first `:` is the provider; the rest is the model name, so Ollama tags with colons work. |
| `ANTHROPIC_API_KEY`, `OPENAI_API_KEY`, `GOOGLE_API_KEY` | empty | Cloud keys. A model whose key is missing is skipped, and `/health` says why. |
| `OLLAMA_BASE_URL` | `http://host.docker.internal:11434` | The local model server |
| `AI_TIMEOUT_S` | `8` | Total budget for one `/classify` call (stays under the C# 10 s timeout) |
| `AI_MODEL_TIMEOUT_S` | `4` | Cap for each cloud model attempt |
| `OLLAMA_TIMEOUT_S` | `6` | Cap for the local model attempt |
| `AI_DEV_ROUTES` | `0` | `1` turns on the dev route below |

The existing `AI_MODEL` + `ANTHROPIC_API_KEY` pair still works and counts as `anthropic:<AI_MODEL>`.

### 3. Dev route for scoring

`POST /classify?model=<id>` exists only when `AI_DEV_ROUTES=1`:

- It skips the cache and uses only that model.
- The response `provider` is that model id.
- An unknown or not-ready id returns 400.

When `AI_DEV_ROUTES` is off, the `model` parameter is ignored.

### 4. `/health`

`/health` adds `models: [{ "id": "openai:<model>", "ready": true, "reason": null }]`, where `reason` is e.g. `"OPENAI_API_KEY not set"`. The existing fields stay, and `modelConfigured` is true when any model is ready. The C# `HealthDto` ignores unknown fields.

## Phase 1: all three terminals in parallel (~2.5 h)

### Terminal A: chain and models

1. **Dependencies.** Pin exact versions in `ai/pyproject.toml`:
   - `langchain-core==1.7.0`
   - `langchain-anthropic==1.7.6`
   - `langchain-openai==1.7.2`
   - `langchain-google-genai==4.4.2`
   - `langchain-ollama==1.1.0`

   Then:
   - add `[tool.uv] exclude-newer = "2026-10-24T00:00:00Z"`;
   - lock with `UV_PYTHON=3.12 uv lock`. The default Python 3.15 fails to build `orjson`.
   - `anthropic==1.15.0` stays.
2. **`config.py`.** Parse `AI_MODELS` into `ModelSpec(provider, model, timeout_s)`, with fail-fast errors for an unknown provider or a bad entry. Each spec gets a `ready` flag and a `reason`.
3. **`models.py` (new).** A lazy factory per provider, with `temperature=0` and `max_retries=0` on every model. The timeout setting differs per class:

   | Class | Timeout setting |
   |---|---|
   | `ChatAnthropic` | `default_request_timeout` |
   | `ChatOpenAI` | `request_timeout` |
   | `ChatGoogleGenerativeAI` | `timeout` |
   | `ChatOllama` | `client_kwargs={"timeout": ...}` |

   `ChatOllama` also gets `reasoning=False`, because Qwen 3.5's thinking mode would use up the budget. No client is built when no model is configured; the existing test for this stays green.
4. **`prompts.py` (new).**
   - The system prompt lists the allowed category ids and labels from `seed/categories.json`.
   - Invoice lines go inside an `<invoice_lines>` block, and the prompt states that this text is data, never instructions.
   - Descriptions are capped at 500 characters, with `<` and `>` stripped.
   - Quantity, unit code and classification code are passed as context.
5. **`providers.py`.** Change from one line at a time to one batch per call:
   1. The cache answers whatever lines it can.
   2. The remaining lines go to the first ready model in one `with_structured_output` call. The output schema is built at startup, with `categoryId` limited to the allow-list ids.
   3. The answer is checked:
      - line ids must be ones that were sent, and only the first answer per id counts;
      - confidence is clamped to 0–1;
      - the rationale is capped at 200 characters;
      - the existing `_allowed` check applies.
   4. **Low confidence:** an answer below 0.6 becomes `UNCLASSIFIED` with the rationale "Low confidence, check this line".
   5. Lines still missing go to the next model within the time left, then to the stub.
   - Each model call runs in a worker thread with a hard deadline.
   - **Runtime cache:** a model answer is kept in memory and reused for the same description (same `cache_key`).
6. **`main.py`.** The dev route and `models` in `/health`.
7. **`tests/test_models.py` (new).** Fake models (`RunnableLambda`), no network, no keys. Cover:
   - `AI_MODELS` parsing (colon tags, an unknown provider, a missing key)
   - a cache hit never reaching a model
   - the first model answering while the second is never called
   - the first model erroring or hanging, so the second answers
   - every model failing, so the stub answers
   - a category outside the allow-list
   - unknown or missing line ids
   - the low-confidence threshold
   - the adversarial line staying inside the block
   - the total budget (< 8.5 s even when every model hangs)
   - the runtime cache
   - the dev route on and off, and `/health.models`

**Done when:** `uv run pytest` passes (the existing 14 tests stay green), `uv run ruff check . && uv run ruff format --check .` is clean, and `uv sync --locked` works.

### Terminal B: test set and scoring

1. **`ai/eval/ro-lines.json`.** 30 fictional Romanian invoice lines, each with `{ id, description, quantity, unitCode, classificationCode, classificationListId, expected }`. The mix:
   - with and without diacritics ("Carcase senzor turnate zinc" / "carcase turnate din zinc")
   - abbreviations ("cabl. fasc. 12 cai", "cut. carton 400x300")
   - mixed RO/EN
   - supplier product codes only
   - 3 lines whose correct answer is `UNCLASSIFIED`
   - 2 prompt-injection attempts

   No real company names.
2. **`scripts/lib/ai-eval.mjs`.** Pure functions: accuracy, per-category accuracy, average and slowest time, injection pass/fail, and a Markdown table. Tested in `scripts/lib/ai-eval.test.mjs` with `node --test`.
3. **`scripts/ai-eval.mjs`.** Node, no dependencies.
   - It reads `GET /health` for the ready models, then calls `POST /classify?model=<id>` for each one with all 30 lines, timing each call.
   - It prints the table, and with `--write` saves it to `docs/ai-eval.md` together with the date and the model list.
   - `--url` sets the AI service address (default `http://localhost:8000`).
4. Until Terminal A lands, the script runs against the current stub service; accuracy then equals the share of `UNCLASSIFIED` lines. That's enough to prove the script works.

**Done when:** `node --test scripts/lib/*.test.mjs` passes and `node scripts/ai-eval.mjs` runs against the current service.

### Terminal C: rules, demo invoices, config, docs

1. **`seed/classification-map.json`.** Add HS rows (`"listId": "HS"`) for the 6 real categories: zinc die-cast, PCB, wire harness, paper packaging, connectors, plastics. Check every heading against the official HS/CN nomenclature before adding it; don't write codes from memory. `SeedLoader` validates the category ids.
2. **`seed/invoices/R1-*.xml` to `R3-*.xml`.** 3 Romanian invoices:
   - **R1:** Romanian wording, no classification code. The AI path.
   - **R2:** carries an HS code. The new rule path.
   - **R3:** mixed lines with abbreviations and missing diacritics.

   Requirements:
   - fictional supplier names and CUIs with a valid check digit that are not registered at ANAF (as `docs/NEXT.md` describes);
   - they must pass `node scripts/validate-seed-anaf.mjs`;
   - they are **not** added to `demo-state.json` or `ai-cache.json`, so the demo path and the Playwright smoke stay unchanged.
3. **`compose.yml`.** Pass every variable from the agreements to the `ai` service, and add `extra_hosts: ["host.docker.internal:host-gateway"]`.
4. **`.env.example`.** Document every variable from the agreements.
5. **Docs.**

   | File | Change |
   |---|---|
   | `CLAUDE.md` | The new dependencies and the AI part of the map |
   | `docs/DECISIONS.md` | New entry: chain order, 8 s budget, per-model caps, the 0.6 threshold, the runtime cache, the dev route |
   | `docs/ROADMAP.md` | F6 status |
   | `docs/NEXT.md` | Slice 7 progress |
   | `docs/ARCHITECTURE.md` | The `ai/` row and the chain |
   | `docs/SECURITY.md` | The prompt guard; keys only in server env |
   | `docs/RUNBOOK.md` | Setting `AI_MODELS`, the Ollama setup (`ollama pull qwen3.5:4b`), `docker compose up --build --renew-anon-volumes` after a dependency change, and reading `/health.models` |

**Done when:** `dotnet test` passes, the ANAF validation passes for R1–R3, and `node scripts/preflight.mjs` reports no banned term. It may still fail on the existing factor placeholders, as it does today.

## Phase 2: in parallel, after Terminal A's branch is pushed (~1 h)

- **A: local model.** Run `ollama pull qwen3.5:4b` (or `9b` if the demo laptop has a GPU with about 8 GB). Time it on R1–R3 through the dev route, and tune `OLLAMA_TIMEOUT_S` through env only.
- **B: real scores.** With real keys and `AI_DEV_ROUTES=1` on A's branch, run `node scripts/ai-eval.mjs --write`, commit `docs/ai-eval.md`, and flag any model below about 80% accuracy or failing an injection line.
- **C: demo script.** Add the AI demo steps to `docs/RUNBOOK.md`:
  1. Import R1: the node shows "AI-mapped" with confidence and reason.
  2. Import R2: classified by the HS rule, no AI call.
  3. Import `adversarial.xml`: the hidden instruction is ignored.
  4. Remove the main model's key and restart `ai`: the backup model answers.
  5. Turn the wifi off: the local model answers, or the stub if it's missing.

## Phase 3: merge and full check, one terminal (~45 min)

1. Merge in the order **A → C → B**, one PR each, with commit subjects starting `F6:` and no attribution trailers.
2. Run `docker compose up --build --renew-anon-volumes`.
3. Run the checks:
   - `cd ai && uv run pytest && uv run ruff check . && uv run ruff format --check .`
   - `cd api && dotnet test`
   - `cd web && pnpm e2e` (the demo path must be unchanged)
   - `node scripts/ai-eval.mjs`
4. Rehearse steps 1–5 of the demo script on the demo laptop.
5. Remove the worktrees: `git worktree remove ../TG-ai-core` (and the other two).

## Timeline

| Phase | A | B | C | Wall clock |
|---|---|---|---|---|
| 1 | Chain, models, tests | Test set, scoring script | Rules, RO invoices, config, docs | ~2.5 h |
| 2 | Local Qwen | Real scores | Demo script | ~1 h |
| 3 | Merge and full check (one terminal) | | | ~45 min |
| **Total** | | | | **~4.5 h** |

## Needed before starting

1. **The two cloud models** (provider and exact name). Terminal A can start with placeholders, since its tests use fake models.
2. **API keys** in each person's own root `.env`, which is not committed. Needed from Phase 2.
3. **The demo laptop's RAM and GPU**, to choose between `qwen3.5:4b` and `qwen3.5:9b`.

## Phase 0: setup (~10 min, one person, before the terminals start)

The plan has to be on `main` before the worktrees are created, so every terminal can read it.

```
cd D:\TraceGreen
git switch main
git pull
git add docs/plan/ai-f6.md
git commit -m "F6: AI classifier plan"
git push
git worktree add ../TG-ai-core  -b f6/ai-core       main
git worktree add ../TG-ai-eval  -b f6/ai-eval       main
git worktree add ../TG-ai-data  -b f6/ai-data-docs  main
```

Then:

- **Keys:** copy the root `.env` into `D:\TG-ai-core` and `D:\TG-ai-eval`. It's git-ignored, so worktrees don't get it.
- **Ports, so the services don't collide:**
  - Terminal A runs its AI service on **:8001**;
  - Terminal B runs the current (stub) service on **:8002** during Phase 1, then points at A's **:8001** in Phase 2;
  - Docker Compose keeps :8000.
- **.NET:** Terminal C needs the .NET 10 SDK on the host to run `dotnet test` in its worktree (`CLAUDE.md`, "Commands").

Open one Claude Code terminal in each worktree folder and paste its prompt below.

## Terminal prompts

### Terminal A (`D:\TG-ai-core`)

```
Read CLAUDE.md, then docs/plan/ai-f6.md. You are Terminal A ("AI core"). Do Phase 1 for Terminal A exactly as written in the plan.

Rules:
- You may change ONLY ai/app/**, ai/tests/**, ai/pyproject.toml and ai/uv.lock. Do not touch any other file, even if it looks out of date; Terminals B and C own the rest.
- Follow "Fixed agreements" exactly (env var names, the dev route POST /classify?model=<id> behind AI_DEV_ROUTES=1, /health.models). Do not change the /classify request or response shape.
- Pin exact versions as listed. Lock with UV_PYTHON=3.12 uv lock.
- Tests use fake models only (RunnableLambda); no network, no API keys.
- The model names are not decided yet; use placeholders in tests and examples.

When done, run: cd ai && uv sync --locked && uv run pytest && uv run ruff check . && uv run ruff format --check .
Then start the service on port 8001 with AI_DEV_ROUTES=1 to check that /health shows the models list.
Commit on branch f6/ai-core with subjects starting "F6:", no attribution trailers, push, and report what was done and what is missing.
```

### Terminal B (`D:\TG-ai-eval`)

```
Read CLAUDE.md, then docs/plan/ai-f6.md. You are Terminal B ("Evaluation"). Do Phase 1 for Terminal B exactly as written in the plan.

Rules:
- You may create or change ONLY ai/eval/**, scripts/ai-eval.mjs, scripts/lib/ai-eval.mjs, scripts/lib/ai-eval.test.mjs and docs/ai-eval.md. Do not touch ai/app, ai/tests, seed/ or any other doc; Terminals A and C own those.
- Scripts are Node .mjs with no dependencies, like the existing scripts/ files.
- Build against "Fixed agreements" in the plan: GET /health with models[], and POST /classify?model=<id>. Terminal A is building that route right now; until it lands, test against the current stub service: cd ai && uv run uvicorn app.main:app --port 8002, and run the script with --url http://localhost:8002.
- The 30 test lines are fictional Romanian invoice lines; no real company names (CLAUDE.md, "Copy rules").

When done, run: node --test scripts/lib/*.test.mjs, and node scripts/ai-eval.mjs --url http://localhost:8002.
Commit on branch f6/ai-eval with subjects starting "F6:", no attribution trailers, push, and report what was done and what is missing.
```

### Terminal C (`D:\TG-ai-data`)

```
Read CLAUDE.md, then docs/plan/ai-f6.md. You are Terminal C ("Data, config, docs"). Do Phase 1 for Terminal C exactly as written in the plan.

Rules:
- You may change ONLY seed/classification-map.json, new files seed/invoices/R1-*.xml to R3-*.xml, compose.yml, .env.example, CLAUDE.md and docs/{DECISIONS,ROADMAP,NEXT,RUNBOOK,SECURITY,ARCHITECTURE}.md. Do not touch ai/** or scripts/**; Terminals A and B own those.
- HS codes: check every heading against the official HS/CN nomenclature before adding it, and say in your report where each one came from. Do not write codes from memory.
- R1–R3 use fictional names and CUIs with a valid check digit that are not registered at ANAF. Do not add them to demo-state.json or ai-cache.json.
- Document the env vars and behaviour exactly as "Fixed agreements" in the plan says; Terminal A is implementing them in parallel.

When done, run: cd api && dotnet test, node scripts/validate-seed-anaf.mjs seed/invoices/R1-*.xml seed/invoices/R2-*.xml seed/invoices/R3-*.xml, and node scripts/preflight.mjs (the existing factor placeholders may still fail it; no banned term may appear).
Commit on branch f6/ai-data-docs with subjects starting "F6:", no attribution trailers, push, and report what was done and what is missing.
```

## Risks

| Risk | Mitigation |
|---|---|
| A cloud model is slow or rate-limited at the venue | Per-model 4 s cap, then the next model, then the stub; imports never wait more than 10 s |
| Qwen 3.5 is weak in Romanian or slow on CPU | Measured in Phase 2; it's the last model before the stub; use `4b` without a GPU |
| Teammates keep old packages after the pull | `--renew-anon-volumes`, documented in `RUNBOOK.md` and the PR description |
| Demo path broken by the new seed files | R1–R3 stay out of `demo-state.json` and `ai-cache.json`; `pnpm e2e` runs in Phase 3 |
