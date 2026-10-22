# Dev C: supplier page, data quality and juror-facing copy

Your work covers the phone half of the live demo, the numbers every node stands on, and the checks that keep what a juror reads honest. These are your tasks in order. Each one lists what "done" means, the files, and a suggested prompt sequence: paste the first prompt, read what comes back, and steer from there. Read [README.md](README.md) first. The loop in README §6.2 applies to every task.

Rules that hold across your track:

- Numbers in `seed/factors.json`, the lanes, the masses and `ETS_PRICE_EUR_PER_T` come from a source a person has read, never from Claude's memory (`PRIORITY-CHANGES.md` §2). Claude may make the edit once you give it the value and the source row.
- `/verify/*` uses no cookies, login, email, camera or clipboard APIs and asks for no personal data. Everything renders as text (`CLAUDE.md`).
- People write `docs/run-log.md` and `docs/field-notes.md`; Claude doesn't create them.

## C0: Setup and a read-only first session (Thu 22 Oct, about 1 h)

1. Clone the repo, run `docker compose build && docker compose up`, then open http://localhost:3000 and http://localhost:8080/api/health.
2. In a second terminal, run the tests inside the containers (`RUNBOOK.md`): `docker compose exec api dotnet test`, `docker compose exec ai uv run pytest`, `docker compose exec web pnpm test`.
3. Once B0 is merged, pull `main` so the shared settings and the copy-guard hook apply to your sessions.
4. Start `claude` in the repo root, press Shift+Tab until the footer shows plan mode, and ask:

   > Using the code, explain how a UBL invoice becomes a SupplierNode today (parser → classification → estimate → repository), what is still missing for POST /api/ingest/ubl, and how a verification token is built and checked. Read only; don't edit anything.

5. The controls you'll use most:
   - Esc stops Claude mid-step.
   - `/rewind` goes back to an earlier point. It restores Claude's own file edits but not changes made by shell commands, so commit each time the checks pass.
   - `/clear` starts a fresh context.
   - `claude --continue` reopens your last session.
   - `/output-style explanatory` makes Claude explain its choices as it works, which helps in unfamiliar territory such as UBL, FastAPI or the ANAF rules. It saves only to your own local settings.

## C1: T4 factor sources (skeleton Thu 22 Oct; real rows with the team by Sat 31 Oct)

**Part 1: the skeleton (Claude Code).** Create `docs/factor-sources.md` with a short header stating the rules from `PRIORITY-CHANGES.md` §2, and one table row per entry in `seed/factors.json` (19 rows). The columns are id, kind, key, value, unit, source, year, row label and "WTT included?" (left blank). Add a row for `ETS_PRICE_EUR_PER_T` and a line for the spend-factor source decision (D1).

> Create docs/factor-sources.md as a table skeleton pre-filled from seed/factors.json, following docs/PRIORITY-CHANGES.md §2. Copy values and source text exactly, leave "WTT included?" blank, and add a row for ETS_PRICE_EUR_PER_T. Don't modify seed/factors.json and don't add any number that isn't already in the repo.

PR: `T4: factor-sources skeleton from factors.json`, reviewed by A.

**Part 2: the real rows (you, with A and B for D1 and D6).**

- Download the DESNZ 2026 conversion factors (the flat file) yourself. For each transport and material row, find the matching table ("Freighting goods", "Material use") and row label, and write the table, year, row label, and whether well-to-tank is included into `factor-sources.md`. Where there is no exact match (zinc die-cast, PCB), write down the nearest row and why you chose it.
- Grid rows come from EEA, Ember or ANRE, never from the DESNZ electricity factor, which is not used for Romania (roadmap FB14, FV5). No formula reads the grid rows today, so for D6 either source them or propose deleting them.
- Spend rows come from the source chosen in D1.
- Then replace the rows in `seed/factors.json` in one PR. Claude Code can make the edit from your table:

  > Update seed/factors.json from the confirmed rows in docs/factor-sources.md: value, unit, source (table name and year) and rowLabel. Change nothing else. Then run `docker compose exec api dotnet test` and list every failing test with its expected and actual values.

- Expect failures in tests that pin numbers derived from the seed: `EstimateTests`, `SimulatorTests` and `TestNodes`. Update those expectations in the same PR, and show the arithmetic in the PR description. If `N1AirToSeaIsWinWin` or `N1ToN1AltIsPremiumOrBelowEts` fails, stop: that is D5. Report it in `docs/NEXT.md`, and don't change lanes, masses or prices to bring the badge back (§4).
- `ETS_PRICE_EUR_PER_T` is the day's EUA price. A person sets it on 5 Nov, with the date in a comment.

Reviewer: A.

## C2: Slice 2, ANAF validation (Fri 23 Oct; T5 is due that day)

**Goal:** `scripts/validate-seed-anaf.mjs` sends every file in `seed/invoices/` to ANAF's public validator and prints, for each file, either OK or the exact ANAF message. It exits with code 1 if any file fails. Then fix the XMLs until all six pass, and paste the final output into `docs/NEXT.md`.

**Constraints:**

- A Node 24 `.mjs` script with no new dependency (the built-in `fetch` is enough).
- The endpoint is `https://webservicesp.anaf.ro/prod/FCTEL/rest/validare/FACT1` (public, no authentication). Have Claude Code read ANAF's documentation for the exact request headers and response fields, and print the raw response on failure so nothing is lost.
- When fixing an XML, keep the amounts, quantities, unit codes, origin countries, classification codes and line descriptions exactly as they are. The parser tests pin them (for example N1's total of 48000.00 and its 2300 KGM), and demo numbers are never tuned. If ANAF seems to need one of them changed, stop and ask A.
- `adversarial.xml` keeps its prompt-injection line on purpose (roadmap A3). Fix only what ANAF complains about.
- After every XML change, run `docker compose exec api dotnet test`, then the script again.

**Prompt sequence:**

1. In plan mode:

   > Read docs/plan/dev-c.md task C2 and docs/PRIORITY-CHANGES.md slice 2. Find ANAF's documentation for the validare/FACT1 endpoint and tell me the exact request (method, headers, body) and the response fields, with the URL you read. Then plan scripts/validate-seed-anaf.mjs. Don't write code yet.

2. > Implement it, run `node scripts/validate-seed-anaf.mjs`, and show me the output.

3. For each failure:

   > Fix seed/invoices/<file> for this ANAF message: <paste the message>. Don't change amounts, quantities, unit codes, origin, classification codes or descriptions. Run the api tests in the container, then re-run the script.

4. > Add the final script output to docs/NEXT.md under "Seed XML validation (<date>)".

PR: `F1/T5: ANAF validation script and seed fixes`, reviewed by A.

## C3: Copy module (Sat 24 Oct)

Create `web/src/copy/labels.ts` and `web/src/copy/verify.ts`:

- **KPI:** the label "Supplier-confirmed share", the subtitle "by emissions, not by node count", and the tooltip from ruling 2, word for word.
- **Other labels:** the provenance templates (ruling 3); the uncertainty labels (ruling 4); the AI labels (`AI-mapped`, `AI-suggested · illustrative`, `AI-extracted · check fields`); "illustrative"; the MAC badge names (win-win, below the carbon price, premium option, not an abatement option); and the audit words ("tamper-evident audit log", chain valid, chain broken).
- **`/verify` strings in Romanian and English:**
  - the two questions and their options: Road / Rail / Sea / Air, and Grid / Mostly renewable / Don't know;
  - skip and submit;
  - the results: thanks, already used, link expired, invoice changed, link not valid, too many attempts, no connection;
  - the reconfirm banner text for each plausibility warning.
- Type the dictionaries so that a missing Romanian or English string fails `pnpm typecheck`.
- Claude can draft the Romanian wording, but check every string yourself.

> Create web/src/copy/labels.ts and web/src/copy/verify.ts with the strings listed in docs/plan/dev-c.md task C3. Use the exact wording of docs/PRIORITY-CHANGES.md rulings 2–4 and the AI labels in CLAUDE.md. Type the RO/EN dictionaries so a missing key fails `pnpm typecheck`. Touch no other files. Run pnpm typecheck and pnpm lint.

PR: `F2/F3: juror-facing copy (RO/EN)`, reviewed by B.

## C4: M1 acceptance run and recording (Sun 25 Oct)

- Run the M1 checklist in README §8 twice: once with `docker compose up` (Postgres), once with `DATA_MODE=memory docker compose up` (which works after B0). Report anything that's off in the team chat.
- Record the M1 screen recording yourself.

## C5: Slice 4, the supplier page `/verify/:nodeId` (Mon–Thu 26–29 Oct)

**What the page does:**

- It reads the token from `?t=` and the language from `?lang=`: Romanian by default, English as the alternative, with a visible toggle.
- It loads `GET /api/verify/context?t=` and shows the supplier and the item, then two big button groups, both pre-selected:
  - transport, offering only the modes the lane has, with the lane's default selected;
  - energy: Grid, Mostly renewable or Don't know, with Grid selected.

  Either question can be skipped, which makes the result PARTIAL.
- It submits with `POST /api/verify` and shows the result.
- **States:** loading; ready; submitting; done (verified or partial); already used (409); expired or invoice changed (410); link not valid (401); too many attempts (429); no connection (with retry). Plausibility warnings appear as a "please reconfirm" banner that doesn't block (D2).
- It works one-handed at 360 px wide, with tap targets of at least 48 px, and it is readable through the stage camera.
- **Hard rules:**
  - no cookies, storage, login, email, camera or clipboard APIs;
  - no fields for personal data;
  - never log the URL or the token;
  - strings come from `web/src/copy/verify.ts`.

**How to build it:**

- **Mon:** the page skeleton and its states, typed against the slice-3 contract (A5), with a local fixture standing in for the context response.
- **Tue–Wed:** call the real endpoints through `web/src/api/client.ts` as A6 lands.
- **Wed–Thu:** test on a real phone over the laptop's hotspot, with `PUBLIC_BASE_URL` set as `RUNBOOK.md` describes. With B's dashboard open, the node should turn green in under 2 s.
- Keep the decision logic in a pure module, for example `web/src/lib/verify-state.ts`, with vitest tests: status code → state, language resolution, which modes to offer. Vitest here runs without a browser DOM, so logic inside components can't be tested.

**Prompt sequence:**

1. In plan mode:

   > Read docs/plan/dev-c.md task C5, docs/PRIORITY-CHANGES.md slice 4, the rules for /verify/* in CLAUDE.md, and the verify endpoints in web/src/api/schema.d.ts. Plan the page: components, the state machine, which strings come from web/src/copy/verify.ts, and which pure helpers get tests. No code yet.

2. > Build the page against a local fixture for the context response. Put the decision logic in web/src/lib/verify-state.ts with tests. Run pnpm typecheck, pnpm lint and pnpm test, then tell me which URL to open to see each state.

3. > Replace the fixture with calls through web/src/api/client.ts. Map 401, 409, 410 and 429 to their states. Run the checks again.

4. By hand: request a link from the dashboard, open it on the phone, tap through, and watch the node.

Two PRs: first the skeleton and states, then the wiring and the phone test. B reviews, and A looks over how the API is used.

## C6: Preflight and M2 (Fri 30 Oct – Sun 1 Nov)

`scripts/preflight.mjs` (Node, no dependencies) prints a readable report and fails when:

- any `source` in `seed/factors.json` contains PLACEHOLDER;
- `ETS_PRICE_EUR_PER_T` is unset in `.env.demo`. The `70` there is itself a placeholder (§2), so blank it in this PR to make the check mean something. `AppConfig` still defaults to 70 for development;
- a banned term, including the retired product name from `CLAUDE.md`, appears in a tracked file outside the documents that state the rules (reuse `scripts/lib/banned-terms.mjs` and its list of exempt paths from B0).

It is expected to fail today, and it turns green when T4 is done. Reviewer: A.

> Read docs/plan/dev-c.md task C6 and docs/PRIORITY-CHANGES.md §2 and §4. Write scripts/preflight.mjs as a Node script with no dependencies, reusing scripts/lib/banned-terms.mjs. Each check prints PASS or FAIL with the file and line. Run it and show me the report; it should fail on the placeholders today.

M2, Sun 1 Nov: you drive the phone in the T16 run-through and write every failure into `docs/run-log.md` yourself.

## C7: Slice 7, the AI service's contract, cache and stub (Tue–Wed 3–4 Nov)

This starts once slice 6 is green, because of the order rule. Its scope follows D4; by default there is no live model before 5 Nov.

- **`ai/app/schemas.py`:** pydantic models that serialise exactly the JSON `HttpAiClient` sends and expects, in camelCase:
  - `lines`;
  - `classifications[]` with `lineId`, `categoryId`, `confidence`, `rationale` and `source`;
  - `provider`;
  - `suggestions[]` with `alternativeId`, `rationale` and `label`;
  - health: `ok`, `provider`, `cacheEntries` and `modelConfigured`.
- **`ai/app/cache.py`:** `cache_key(description)` is the SHA-256 hex of the description after trimming, lower-casing and collapsing whitespace, byte for byte the same as `Domain/Ai/CacheKey.cs`. It loads `seed/ai-cache.json`.
- **Routes:**
  - `POST /classify` tries the cache, then the stub. Each result is checked against `seed/categories.json`, and anything outside it becomes `UNCLASSIFIED`.
  - `POST /suggest` returns an empty list for now.
  - `POST /extract-pdf` returns 501.
  - `GET /health` reports the real provider chain and the cache size.
- **Tests:**
  - a cache hit;
  - the stub fallback;
  - no Anthropic client is constructed unless both `AI_MODEL` and `ANTHROPIC_API_KEY` are set;
  - allow-list enforcement;
  - every entry in `seed/ai-cache.json` hashes back to its own key, which is the parity test with the C# side;
  - the adversarial line comes back allow-listed or `UNCLASSIFIED`.

**Prompt sequence:**

1. In plan mode:

   > Read docs/plan/dev-c.md task C7, api/src/TraceGreen.Infrastructure/Ai/HttpAiClient.cs (it defines the JSON contract), api/src/TraceGreen.Domain/Ai/CacheKey.cs and ai/app. Plan schemas.py, cache.py, the provider chain (cache, then stub), the routes and the tests. Show the exact JSON field names and where each one comes from in HttpAiClient.

2. > Write the tests first and run `uv run pytest` to show they fail, then implement until they pass. Finish with `uv run ruff check . && uv run ruff format --check .`.

3. > Bring the stack up and show me GET /api/health with the AI provider.

PR: `F6/A2: AI service contract, cache and stub`, reviewed by A. If D4 moves the live provider before 5 Nov, it goes in a separate PR, and A reviews the prompt handling: invoice text inside a delimited block, instructions in it ignored, output validated against the schema and the allow-list, length capped, 10 s timeout.

## Copy review (ongoing)

On every PR that changes text a juror can read (UI, seed data, README, OpenAPI descriptions), check it against `PRIORITY-CHANGES.md` §4 and rulings 2–4:

- no "primary data" without ruling 2's definition;
- no "verified PCF", no "blockchain", no "first" or "only";
- no real company names;
- the AI and "illustrative" labels are present.

Leave a review comment either way, so the author knows the copy was checked.
