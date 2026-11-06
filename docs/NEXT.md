# Status and next steps

Last updated: 2026-11-06 (the AI follow-ups: the AI's reason on screen, AI-suggested alternatives, the data chat and the PDF import, see "AI follow-ups (F6, F11, F13, F9)" below; the classification review, F6: the API, its contract and both repositories, see "Classification review (F6)" below; the AI classifier F6: the HS rules, the Romanian invoices R1–R3, the model variables in Compose and the docs, Terminal C of `docs/plan/ai-f6.md`; the v2 layout on `b/v2-layout`, now with the supplier page, its terms and the supplier page link in the v2 style: see "v2 layout (2026-11-06)" below; the supplier page, F12, a thin slice built on hackathon day, merged with the Supplier Terms: see "Supplier page (F12)" below; and the Supplier Terms on the verify page, F3: see "Supplier terms (F3)" below); before that, 2026-11-05 (the demo tooling, task B7 of `docs/plan/dev-b.md`; slice 6, the Playwright smoke, task B6; slice 5, the simulator panel, task B5; the hand-over docs, task A10 of `docs/plan/dev-a.md`; CI, task A9; and slice 5, the simulator backend, task A7; before that, 2026-10-29, the M1 acceptance run in both data modes and its recording, task C4 of `docs/plan/dev-c.md`; 2026-10-27, slice 3, the verify loop's backend, tasks A5–A6 of `docs/plan/dev-a.md`; 2026-10-24, the M1 integration run in both data modes, task B3 of `docs/plan/dev-b.md`; 2026-10-23, slice 2, the seed invoices validated at ANAF, task C2 of `docs/plan/dev-c.md`; and 2026-10-22, the slice-1 backend and client transport, tasks A1–A3 of `docs/plan/dev-a.md`). Order of work: "What remains (in order)" below and `docs/plan/`. Decisions: `docs/DECISIONS.md`.

## What exists

Monorepo with three services behind one browser origin, brought up by `docker compose build && docker compose up` (see `RUNBOOK.md`).

| Area | State | Tests |
|---|---|---|
| `api/` .NET 10 host | Config from env (fails fast), OpenAPI at `/openapi/v1.json`, `GET /api/health` (mode, db, seed nodes, `ai`, `baseUrl`), `GET /api/graph`, `POST /api/dev/reset`, `POST /api/ingest/{ubl,demo}`, `GET /api/events` (Server-Sent Events with `id:`/`Last-Event-ID` replay, `resync`, `ping`), `POST /api/request-link`, `GET /api/verify/context`, `POST /api/verify` (rate limited), `POST /api/audit/verify`, `POST /api/simulate`, `POST /api/scenarios`, `POST /api/dev/simulate-answer`, `POST /api/nodes/{nodeId}/classification` (the classification review, F6), startup flags `--migrate --seed --reset --exit` | integration tests via `TestWebAppFactory` (memory mode) |
| `api/` Domain (pure) | Model records; carbon math (`Carbon/`: factor table with provenance, spend and activity estimates, KPIs, MAC + badge, simulator); UBL 2.1 parser (namespace-agnostic, DTD rejected, 2 MB cap); canonical JSON + hashes; HMAC link tokens (`v1.` layout, hash-bound, constant-time); hash-chained audit log; `IRepository`, `IEventBus`, `IAiClient` ports; `LineClassification` (invoice code → cache → AI with timeout → stub); `DemoState` builder | unit tests, hand-computed expectations |
| `api/` Infrastructure | `MemoryRepository` (lock-atomic); `EfRepository` on Postgres (advisory lock per workspace, unique nonce, unique `(workspace_id, prev_hash)`, TRUNCATE-based reset); migrations `Init` + `Rls` (row-level security + append-only trigger on `audit_log`); `InMemoryEventBus` (ring buffer 500); `HttpAiClient` / `StubAiClient` | repository contract runs against memory always and against Postgres when `DATABASE_URL` is set |
| `ai/` FastAPI | The C7 contract with the F6 chain: `POST /classify` answers one batch through the cache, then the models in `AI_MODELS` in order (LangChain chat models for Anthropic, OpenAI, Google GenAI and Ollama), then the stub, within 8 s; categories outside `seed/categories.json` become `UNCLASSIFIED`, and so do answers below 0.6 confidence; model answers are kept in a runtime cache; `POST /classify?model=<id>` only with `AI_DEV_ROUTES=1`; `POST /suggest` (empty), `POST /extract-pdf` (501), `GET /health` (provider, cache size, `modelConfigured`, and `models` with `ready` and `reason`); camelCase schemas matching `HttpAiClient` | the 14 C7 pytest tests (cache hit, stub fallback, allow-list, cache-key parity with C#, adversarial line, no client without a model, routes) plus `tests/test_models.py` (fake models, no network or keys); ruff clean |
| `web/` Vite SPA | Since 2026-11-06 the v2 layout ("v2 layout" below) replaces the canvas dashboard described here; the store, actions, simulator and supplier pages are unchanged in behaviour. Dashboard at `/`: Zustand graph store (`hydrate`, `applyEvent`, `select`) with memoised React Flow selectors and dagre layout (B1); supplier nodes (status tone, hotspot ring, "i" factor tooltip, "AI-mapped" label), the KPI bar from API values and the toolbar (Import, Load demo invoice, Reset demo) (B2), kept live by `useGraphEvents` (A3). The node drawer with the QR card, the audit panel and the connection badge (B4). The simulator in the drawer (B5): mode buttons with the lane's missing legs disabled, the node's `alternatives` as swap buttons, the API's result with deltas (tonnes, euros, days; cost, lead time and the MAC labelled illustrative) and the MAC badge, a ghost node left of the supplier, and "Apply" (`POST /api/scenarios`); the base node never changes (see "Simulator panel (B5)" below). The supplier page `/verify/:nodeId` (C5): RO by default and EN through `?lang=`, the link's context, two pre-selected answer groups with skip (→ PARTIAL), the Supplier Terms with an unticked checkbox that the submit needs, and every result state (thanks with the non-blocking reconfirm banner, already used, expired, invoice changed, not valid, terms updated, too many attempts, no connection with retry); the printable terms at `/terms/supplier`; its rules live in the pure `web/src/lib/verify-state.ts` and its requests in `verify-api.ts` (a request hanging past 10 s counts as no connection). In development, React's StrictMode sends the context request twice on load, so each page load counts twice against the verify rate limit; the production build sends it once. Tailwind v4, shadcn (button, card, badge, sheet, select, tooltip, separator), Vite proxy `/api` → API with `X-Forwarded-Host`, oxlint with `react/no-danger` as error | vitest (store, selectors, formatting, sync, toolbar and simulator requests, scenario display, verify page states and requests), typecheck, lint, build; the Playwright smoke `web/e2e/demo-path.spec.ts` against Compose (B6) |
| CI | `.github/workflows/ci.yml` on every pull request and on pushes to `main`: **api** (`dotnet build` with warnings as errors, then `dotnet test` with a Postgres 17 service, so the contract tests run against `tracegreen_test`), **ai** (`uv sync --locked`, pytest, `ruff check`, `ruff format --check`), **web** (`pnpm typecheck`, `lint`, `test`, `build`), **types drift** (API in memory mode, `pnpm gen:api`, `git diff --exit-code web/src/api/schema.d.ts`). Every action is pinned to a release commit. **e2e** (the demo image built and started, `pnpm e2e` against it on Postgres, then again with the API recreated in memory mode; container logs printed on failure) | all five jobs green on GitHub; the e2e job first on its own pull request (#33, 2 min 27 s for both modes) |
| `seed/` | Fixed-UUID demo data: 5 UBL invoices + adversarial file, and the Romanian invoices R1–R3 for the AI and HS-rule paths (not in the demo state), factors (DESNZ 2026 freight and material rows, US EPA spend rows), lanes (illustrative), alternatives, categories (= AI allow-list), classification map (CPV and HS prefixes), ai-cache, demo-state | validated by `SeedLoader` tests |
| Compose | `db`, `ai` (reload; the model variables and keys come from the root `.env`, and `host.docker.internal` reaches Ollama on the host), `api` (`dotnet run`, migrates + seeds on boot), `web` (Vite); `compose.demo.yml` with one-shot `migrate`, runtime images and nginx | brought up and checked by hand |

Test counts at hand-off: 125 .NET tests (109 before Task 8 plus the event bus, AI client, classification and SSE tests), 1 Python test, web typecheck/lint/build.

Test counts after A3: 30 web unit tests (`pnpm test`: the sync reducer and the driver with a fake EventSource and fake timers); 193 .NET tests; in memory mode 180 run and the 13 Postgres contract tests are skipped, with `DATABASE_URL` all 193 run.

Test counts after C2: 197 .NET tests; in memory mode 184 run and the 13 Postgres contract tests are skipped, with `DATABASE_URL` all 197 run.

Test counts at M1 (B3): 206 .NET tests; in memory mode 193 run and the 13 Postgres contract tests are skipped, with `DATABASE_URL` all 206 run; 78 web unit tests.

Test counts after A6: 255 .NET tests; 241 in memory mode (the 14 Postgres contract tests skipped), 255 with `DATABASE_URL`; 78 web unit tests.

Test counts at the M1 re-run (C4, 2026-10-29, after B4): 255 .NET tests; 241 in memory mode (the 14 Postgres contract tests skipped), 255 with `DATABASE_URL` on the scratch database `tracegreen_test`; 105 web unit tests.

Test counts after A7: 266 .NET tests; 252 in memory mode (the 14 Postgres contract tests skipped), 266 with `DATABASE_URL`; 143 web unit tests.

Test counts after B5: 266 .NET tests, 252 in memory mode (run with `dotnet test` inside the `tracegreen-api` dev image); 171 web unit tests.

Test counts after B6: the same, plus one Playwright smoke (`pnpm e2e`, seven steps).

Test counts after B7: the same, the smoke with an eighth step (no CSP violation), and 22 script tests (`node --test scripts/lib/*.test.mjs`, 10 of them for demo-check).

Test counts after the F6 seed data (Terminal C): 266 .NET tests, 252 in memory mode, all passing (run with `dotnet test` inside the `tracegreen-api` dev image on the worktree); `AllSeedInvoicesParse` now reads R1–R3 as well.

Test counts after F12 and the supplier terms (2026-11-06): 326 .NET tests, 309 in memory mode (the 17 Postgres contract tests skipped), 326 with `DATABASE_URL` on `tracegreen_test`; 253 web unit tests; 28 script tests, which CI runs in the web job; two Playwright specs: the demo-path smoke with nine steps (the terms tick and link in step 3, the printable terms in step 6) and `supplier-portal.spec.ts`.

Test counts after the classification review (F6, 2026-11-06): 361 .NET tests, 336 in memory mode (the 25 Postgres contract tests skipped), 361 with `DATABASE_URL` on a scratch database; 255 web unit tests.

## Wire contract (slice 1)

The OpenAPI document at `/openapi/v1.json` is the contract; `web/src/api/schema.d.ts` is generated from it with `pnpm gen:api` (API running on :8080) and committed. Nobody hand-writes API types. A change after merge needs a new contract PR.

| Route | Success | Errors |
|---|---|---|
| `GET /api/graph` | 200 `GraphSnapshot`: `buyer`, `nodes` (`NodeView[]`), `kpis`, `audit` (whole chain, ascending), `lastSeq`, `mode` | — |
| `POST /api/ingest/ubl` (multipart field `file`, or the XML as a `text/xml` / `application/xml` body) | 201 `IngestResult { node, kpis }` | `ApiProblem` with `code`: `MissingFile`, `DoctypeForbidden`, `NotUblInvoice` → 400; `Duplicate` → 409 with `nodeId` of the existing node; `TooLarge` → 413; `UnsupportedMediaType` → 415; `Schema`, `UnsupportedCurrency`, `NoLane` → 422 |
| `POST /api/ingest/demo` | 201 `IngestResult` (seed N1 with its fixed node and invoice ids) | `Duplicate` → 409 |
| `POST /api/dev/reset` | 204, then `graph.reset` on the stream | 404 unless `DEMO_TOOLS=1` |
| `GET /api/health` | adds `ai` (provider name, or `error: …` after at most 1.5 s; never affects `ok`) and `baseUrl { url, loopback, source: configured \| forwarded \| request }` | 503 when the database is down |
| `GET /api/events` | `text/event-stream`; each domain message's data is a `DomainEvent` | — |

Decisions behind it: `docs/DECISIONS.md`, "Wire contract, slice 1 (A1–A3)".

## Wire contract (slice 3)

| Route | Success | Errors |
|---|---|---|
| `POST /api/request-link {nodeId}` | 201 `RequestLinkResponse { nodeId, url, expiresAt, loopback }`; the token exists only inside `url` (`<baseUrl>/verify/<nodeId>?t=<token>`); appends `link.requested` | `NodeNotFound` → 404 |
| `GET /api/verify/context?t=` | 200 `VerifyContext`: supplier label, buyer name, invoice number and date, the dominant line's description (invoice text: render as text), lane origin and destination, `modes` the lane has with `defaultMode`, `energySources` with `defaultEnergy: grid`, `massT`, `expiresAt`. Nothing personal, no nonce, no address | `InvalidToken` → 401; `AlreadyUsed` → 409; `LinkExpired`, `InvoiceChanged` → 410; `RateLimited` → 429 |
| `POST /api/verify {t, transportMode?, energySource?, massT?}` | 200 `VerifyOutcome { nodeId, status, warnings }`: both answered → VERIFIED, one skipped (null or left out) → PARTIAL; then `node.verified` (fresh `NodeView` and `Kpis`) and `audit.appended` | as above, plus `InvalidAnswer` → 422 (nothing answered, a mode the lane lacks, a mass not in (0, 10⁶] t) |
| `POST /api/audit/verify` | 200 `ChainVerification { valid, length, brokenAt }` (1-based position of the first bad entry) | — |
| `POST /api/dev/simulate-answer {nodeId, transportMode?, energySource?, massT?}` | 200 `VerifyOutcome`, through the same link and verify path (same audit rows and events) | 404 unless `DEMO_TOOLS=1`; `NodeNotFound` → 404; `InvalidAnswer` → 422 |

`NodeView` gained `warnings: PlausibilityWarning[]` (`{ code: AIR_HEAVY | SEA_TOO_FAST, detail }`), derived on every read from the confirmed mode and mass, so the snapshot and every node event carry the dashboard marker.

Decisions behind it: `docs/DECISIONS.md`, "Wire contract, slice 3 (A5–A6)".

Checked over HTTP in Postgres mode (`tracegreen_test`) and through `TestWebAppFactory` in memory mode: link → context → air answer on N1 → VERIFIED with `AIR_HEAVY`, `carbonKg` 20 700 on the activity basis, replay 409, context after use 409, chain valid, simulate-answer PARTIAL, the 11th verify call in a minute 429, no token in the API log.

## Wire contract (slice 5)

| Route | Success | Errors |
|---|---|---|
| `POST /api/simulate {nodeId, mode?, alternativeId?}` | 200 `ScenarioResult { carbonKg, costEur, leadDays, delta, mac, badge, inputs, massUnknown, illustrative }` against the node as it is now; nothing stored, no event | `NodeNotFound`, `AlternativeNotFound` → 404; `InvalidChange` (neither or both fields, or an alternative of another category), `ModeNotAvailable`, `MassUnknown` → 422 |
| `POST /api/scenarios {nodeId, mode?, alternativeId?}` | 201 `Scenario { id, nodeId, kind, change, result, createdAt, … }`, recomputed on the server and saved next to the node; then `scenario.saved` | as above |

`NodeView` gained `alternatives: AlternativeView[]` (`{ id, label, country, origin, mode, illustrative }`): the seed alternatives of the node's category, so the drawer offers the swaps without another request. Pass `id` as `alternativeId`.

Decisions behind it: `docs/DECISIONS.md`, "Wire contract, slice 5 (A7)".

Checked over HTTP in Postgres mode (`tracegreen_test`) and through `TestWebAppFactory` in memory mode: N1 imported (ESTIMATED) → sea `win-win`, N1-alt `premium`; after an air answer the same badges; a saved scenario stored and published; road on `CN-RO` 422; `alternatives` on N1 and none on N4.

## Simulator panel (B5)

"Simulate" in the node drawer opens the panel. A mode button or an alternative-supplier button calls `POST /api/simulate`; the answer becomes the store's `scenarioPreview` (UI state, next to the domain nodes, never inside them), which both the panel and the ghost node on the canvas render. Every figure, delta, the MAC and the badge are the API's; the SPA only formats them. "Apply" calls `POST /api/scenarios` and marks the preview saved from the answer.

To see both badge states: Reset demo → Load demo invoice → click Shenzhen Diecast Co. → Simulate → **Sea** shows `Win-win` (−14.0 t, −€3,910, +33 days); **Cluj Metal Works SRL** shows `Premium option` (€305 per tCO₂e). After a verification by air the badges are the same (deltas −8.6 t and −9.1 t; €486 per tCO₂e). Figures since the DESNZ 2026 rows and the 90.01 carbon price (2026-11-06); before them the deltas read −15.1 t and €284.

Decisions behind it: `docs/DECISIONS.md`, "Simulator panel, slice 5 (B5)".

Checked on 2026-11-05 in headless Chromium (a throwaway Playwright script, not a committed spec) against Compose in Postgres mode with N1 ESTIMATED, and against a memory-mode API with N1 ESTIMATED and after an air answer: Air (current) and Road (no leg on `CN-RO`) disabled; Sea `win-win` and N1-alt `premium` in the panel and on the ghost; Apply → "Saved" on the panel and the ghost, 201 from the API with the `INSERT INTO scenarios` in the Postgres log; N1's status, carbon, cost and lead time unchanged; closing the simulator removes the ghost; no console errors. The Playwright smoke (B6) should assert the same `data-badge` values on `sim-result` and `ghost-<nodeId>`.

## v2 layout (2026-11-06)

The SPA follows the "TraceGreen Redesign" canvas, Version 2: one home for each kind of information. A sidebar (Logo 5 wordmark, a connection badge that appears only while live updates are down, Overview · Suppliers · Audit log, and Import / Load demo invoice / Reset demo at its foot) sits beside one screen at a time; on a phone it becomes a band above the screen. `AppShell` runs the one graph sync for all three screens, so switching screens keeps the store, the selection and the connection.

- **Overview** (`/`): the total, the supplier-confirmed share (label, subtitle and tooltip of ruling 2), how the total splits (the API's `primaryShare` rounded to a whole percent and the estimated share shown as the rest, since the API's `estimatedShare` is 1 − `primaryShare`; so the legend always adds up to 100%), the caption "From N deliveries" (the API's `nodeCount`, one node per invoice), and the next step: the largest node that is not VERIFIED, whose button opens it on the Suppliers screen on Summary. The sidebar's Suppliers count is distinct supplier names, since the graph has no supplier id.
- **Suppliers** (`/suppliers`): the tree. One row per supplier, largest `carbonKg` at the top, a supply line from each row into the buyer card (width from the store's `edgeWidth`, colour from status, PARTIAL with a dashed green overlay), the hotspot named in the row, a placeholder factor named in the row, and a green sweep across a row when it turns VERIFIED. Selecting a row opens the side panel: Summary (figure, ruling-3 provenance, ruling-4 uncertainty, supplier answers, the QR cards, and Request data and Supplier page link always below them, as "New …" once a link is out, since links are single-use or expire), Details (invoice and lane, the whole lane group tagged illustrative), Factors (the rows behind the figure, PLACEHOLDER visible, and for a confirmed node the rows behind the spend-based estimate it replaced) and What if (the B5 simulator; its ghost is a dashed row under the supplier). Escape or ✕ closes the panel; an Escape that closed a tooltip does not. Switching tabs keeps the links and a request on its way. In a narrow tree the lines and the buyer card step aside and the rows remain.
- **Audit log** (`/audit`): the chain check at the top, the entries below, newest first.
- **Supplier page**: grouped lists with a tick on the chosen answer, "skip" as a text button, the submit button pinned at the bottom, the Logo 5 loader while the context loads.

Tokens: `web/src/index.css` (grey ground, white surfaces, the platform system font, capsule buttons, amber/green/red status tokens, the green focus ring on every button, muted text at 5:1 or better on the sidebar and the fills). The frozen test hooks keep their names (RUNBOOK); the smoke now starts on `/suppliers`. Checked on 2026-11-06 against Compose in Postgres mode: `pnpm typecheck`, `pnpm lint`, `pnpm test` and `pnpm e2e` pass, and the screens were looked at at 1280×820 and on a Pixel 7.

Left over: React Flow (`@xyflow/react`) and dagre (`@dagrejs/dagre`) are no longer rendered, but `store/layout.ts`, `selectGraphView` and their tests still use them. Removing the two dependencies and that code is its own change. The unused shadcn parts (sheet, card, badge, select, separator) and the old drawer's Section/Rows/Row are gone; `Group`/`GroupRow` in `components/shell/Page.tsx` is the one label/value row, and `byCarbonDesc` in `store/selectors.ts` the one emissions order (tree, hotspot ring, next step).

## Playwright smoke (B6)

`web/e2e/demo-path.spec.ts` is one scripted run of the demo path against the running Compose stack, with the dashboard in a desktop page and the supplier page in a phone-sized one (Pixel 7): reset and load the demo invoice (N1 ESTIMATED) → request a link and read `qr-url` → the phone answers Air + Grid → N1 is VERIFIED on the dashboard within 2 s of the submit → the same link opened again says "already used" → simulator: Sea `win-win`, N1-alt `premium` or `below-ets` on the panel and the ghost, Apply saved, N1 still VERIFIED → audit chain valid. Every check reads a `data-testid` or another `data-*` attribute, never copy. How to run it, the rate-limit note and the frozen hooks: `docs/RUNBOOK.md`, "Playwright smoke". The supplier page (C5) gained test hooks only (`verify-page` with `data-phase` and `data-result`, the answer buttons, the submit button); its behaviour is unchanged.

Checked on 2026-11-05 against Compose: passes in Postgres mode and in memory mode (`DATA_MODE=memory docker compose up -d api`), about 5–6 s per run, twice in a row in each mode. A third run within the same minute fails at step 3 with `rateLimited`, which is the 10-a-minute limit on `/api/verify*` doing its job.

## Demo tooling (B7)

- `scripts/demo-check.mjs` (Node, no dependencies; logic in `scripts/lib/demo-check.mjs`, tested): the URL (`PUBLIC_BASE_URL` from `.env.demo`, or the argument when it is left empty, RUNBOOK step 2) is set, not loopback, answers `/api/health`, is where the API builds QR links (with `PUBLIC_BASE_URL` set, `baseUrl.source` must be `configured`, which catches a stack not restarted after editing `.env.demo`), and `/api/events` sends its `hello` event. Checked on 2026-11-05: all five pass against the dev stack started with `PUBLIC_BASE_URL=http://<lan-ip>:3000`, and with it empty and the LAN URL as the argument (the check then says to open the dashboard at that address); health and the event stream pass through nginx on the demo image.
- `scripts/firewall.ps1`: one inbound rule for TCP 3000 from the local subnet, idempotent, `-Remove` to delete it. It parses and its help loads; it has not been run elevated on a machine yet.
- CSP: the header in `web/nginx.conf` is unchanged. The smoke passes against `compose.demo.yml` with no CSP violation on the dashboard or the phone page, and the header reaches the pages, the `/verify` route and the assets. The Linux firewall commands were already in `RUNBOOK.md`; ruling R6's fallback phone and the checks are now there too.

## Sourced factors (T4)

On 2026-11-06 the eleven PLACEHOLDER rows in `seed/factors.json` were replaced with DESNZ 2026 rows chosen with a person, and `ETS_PRICE_EUR_PER_T` became 90.01, the clearing price of the 2026-11-05 EU primary auction. The rows, the files they were read from (with SHA-256) and why each nearest match was taken are in `docs/factor-sources.md`; the decisions are in `docs/DECISIONS.md` ("T4 …" and the carbon price). The tests that pin derived numbers were recomputed by hand from the same formula (`TestNodes.N1` after an air answer: 9,164.925 kg transport + 7,983.095622 kg material = 17,148.020622 kg).

What the real rows change in the demo (D5: reported, the data not adjusted):
- **Both badges hold.** N1 air → sea is `win-win` and N1 → N1-alt `premium`, before and after the air answer (figures under "Simulator panel (B5)").
- **The hotspot ring no longer lands on the imported N1.** N2, the seed's VERIFIED circuit-board supplier, now reads 22,596.5 kg (the "Electrical items - IT" row, 24,865.48 kg CO₂e per tonne), 34 kg above N1's spend estimate of 22,562.8 kg. With `HOTSPOT_COUNT = 1` the ring stays on N2 when N1 is imported, and after an air answer N1 drops to 17,148.0 kg. The M1 line "N1 imported → amber node with the hotspot ring" no longer holds: the demo script (`docs/RUNBOOK.md`, "Demo steps (F1–F4)", with every figure read off the screens) now says the new import lands just below the largest emitter, a supplier who already confirmed. Changing the PCB row, a mass or the ring count to bring the moment back would be the hand-tuning D5 rules out; a ring count of 2 is a product decision for the team.
- **The numbers on screen.** The seed total is 32.4 t (was 14.5 t), 54.9 t after importing N1; the supplier-confirmed share is 69.8 % of the seed and 41.1 % after the import.

Checked on 2026-11-06: `dotnet test` (326, all passing with `DATABASE_URL` on `tracegreen_test`; 309 in memory mode, 17 skipped), `pnpm test` (255), both Playwright specs against Compose after a demo reset, and `node scripts/preflight.mjs` (the factor, price and agreement checks pass; the operator check still fails).

## Supplier terms (F3)

Before submitting, the supplier ticks the Supplier Terms: the text is `seed/supplier-terms.json` (RO and EN, version `2026-11-06`), and the reasons and the lawyer's checklist are in `docs/legal/README.md`. The page shows one unticked checkbox above Submit. Its statement names §6 (liability), §7 (indemnity) and §10 (law and courts), and its words "Supplier Terms" link to the full text at `/terms/supplier` in a new tab (dev A asked for the terms behind a link, to keep the phone page short). Submit stays disabled until the box is ticked.

| Route | Success | Errors |
|---|---|---|
| `GET /api/verify/context?t=` | `VerifyContext` gained `terms: VerifyTerms { version, sha256, ro, en }`, each language the checkbox statement `{ before, link, after }` | as before |
| `POST /api/verify {t, transportMode?, energySource?, massT?, acceptedTerms?}` | as before; the verification row and the `verification.submitted` payload carry `termsVersion` and `termsSha256` | `TermsNotAccepted` → 422 when `acceptedTerms` is missing or is not the current `terms.sha256`, checked after single use and before the answers; the link stays unused |
| `GET /api/terms/supplier` | 200 `SupplierTerms { version, sha256, ro, en }`, each language `{ title, keyPoints, acceptance { before, link, after }, sections[{ number, heading, paragraphs }] }`; no token, not rate limited | — |

`sha256` is the SHA-256 of the file's canonical JSON, computed when the seed loads; reformatting the file keeps it, a changed word changes it. `seed/supplier-terms-versions.json` pins each published version to its SHA-256, and `node scripts/terms-hash.mjs` recomputes it without .NET (`docs/legal/README.md`, "Changing the terms"). Postgres gained the nullable columns `verifications.terms_version` and `terms_sha256` (migration `SupplierTerms`). `simulate-answer` accepts the current terms on the simulated supplier's behalf and marks its audit payload `simulated: true`.

Checked on 2026-11-06: `dotnet test` in memory mode and against `tracegreen_test`; `pnpm typecheck`, `lint`, `test`; the script tests; the Playwright smoke against Compose in Postgres mode and with the API in memory mode, and on the demo image; the page at Pixel 7 size in RO and EN.

Open, all for people:
- **Operator details.** Every `[PLACEHOLDER: …]` in the terms (legal name, CUI, registered seat, city, contact address) shows on screen until a person fills it in. Kept on purpose for the hackathon (decided 2026-11-06): the team has no legal entity, so the presentation shows the placeholders. `node scripts/preflight.mjs` keeps failing on them, so `v0.9-prehack` stays blocked until an operator exists.
- **A lawyer's review** of the text, using the checklist in `docs/legal/README.md`.
- **The buyer's paper contract** needs the reliance, no-warranty, liability-cap and indemnity clauses listed in `docs/legal/README.md`. The supplier terms cannot stop the buyer from claiming against the operator.
- **Retention.** Section 8 promises the IP hash is kept at most three years; nothing deletes old verification rows yet.

## Supplier page (F12)

One link per supplier, opened on the supplier's phone, that lists every delivery on the buyer's graph from that supplier. Each delivery is confirmed with the two questions of `/verify`. There is no account, sign-in or email: the link is signed, like the phone page's, and reusable until it expires (7 days). In the drawer, "Supplier page link" shows it in a second card under the verification link's. Why it is built this way: `DECISIONS.md`, "Supplier page, thin slice (F12)". Controls: `SECURITY.md`, "Supplier page links (F12)". What comes after it: `docs/after.md`.

| Route | Success | Errors |
|---|---|---|
| `POST /api/portal-link {nodeId}` | 201 `RequestLinkResponse { nodeId, url, expiresAt, loopback }`, where `url` is `<baseUrl>/supplier?t=p1.…`; appends `portal.requested` | `NodeNotFound` → 404 |
| `POST /api/portal/view {t}` | 200 `PortalView { supplierLabel, buyerName, expiresAt, items, terms }`, `terms` being the `VerifyTerms` of the phone page's context. Each `PortalItem` holds the invoice number and date, the dominant line's description, the lane's origin, destination and modes, `status`, `answeredMode` and `answeredEnergy`, and the answers to pre-select (`defaultMode`, `defaultEnergy`). Open deliveries come first. No carbon figure, nothing personal | `InvalidToken` → 401; `LinkExpired`, `InvoiceChanged` (the link's delivery is gone) → 410; `RateLimited` → 429 |
| `POST /api/portal/answer {t, nodeId, transportMode?, energySource?, acceptedTerms?}` | 200 `PortalAnswerResult { outcome: VerifyOutcome, view: PortalView }`, through a fresh single-use verification link, so `link.requested` (actor `portal`), `verification.submitted` (with `termsVersion` and `termsSha256`, never `simulated`), `node.verified` and `audit.appended` are those of a phone answer | as above, plus `NodeNotFound` → 404 (not this supplier's), `AlreadyConfirmed` → 409, `DeliveryChanged` → 409 (another answer to the same delivery landed first; nothing written), `InvalidAnswer` → 422, `TermsNotAccepted` → 422 (`acceptedTerms` missing or not the current `terms.sha256`; checked after `AlreadyConfirmed` and before a link is minted, so nothing is recorded) |

The Supplier Terms (F3, below) apply here as on the phone page: one unticked checkbox above the list, shown while a delivery is still open for answers, with the same statement and link to `/terms/supplier`. Confirm stays disabled until the supplier ticks it, once per page visit; every answer then carries the terms' SHA-256, and the verification row records the version and SHA-256. A page reloaded, or a list that arrives with other terms, starts unticked again. If the terms changed on the server meanwhile (422), the page reads the list again with the box unticked and keeps the open delivery's answers.

Pre-selection: the answer a delivery already has, else the lane's default mode and the power source of the supplier's most recent answer, else grid. A question skipped on this page keeps the answer the delivery already has, so a PARTIAL delivery only gains answers. The page's rate limit is 3 × `RATE_LIMIT_PER_MIN` per address, counted apart from `/api/verify*`.

On stage: Reset demo → Import `seed/invoices/adversarial.xml` (a second invoice from N3's seller, Oradea Harness SRL) → click N3 → "Supplier page link" → scan with the phone → confirm both deliveries; each flips on the dashboard. The adversarial line's injected instruction shows on its card as plain text, which is itself the proof that invoice text is only rendered. Skip the import to show N3 alone.

Checked on 2026-11-06 against this branch's API on the host (memory mode, and Postgres mode on `tracegreen_test`) behind its Vite dev server: both Playwright specs pass in both modes, before and after the merge with the Supplier Terms; before it, CI also ran both on the demo image (nginx, CSP) in both modes. Screenshots at 412 px (Pixel 7) in RO and EN looked right: the terms box above the list, one card open at a time, the status as a colour and a word on its own row. **Not yet checked:** a real phone on the hotspot. The Romanian strings in `web/src/copy/supplier.ts` need a native read.

## AI follow-ups (F6, F11, F13, F9)

Built in order from the plan of 2026-11-06: the AI's reason, buyer corrections (dropped for the classification review of #47, already in development), AI-suggested alternatives, the data chat and the PDF import. UI rule for all of them: nothing new on a screen's default state, every value in a labelled row, one AI label per block.

- **The AI's reason (F6).** The dominant line's one-line reason (the invoice code's, the cache's or the model's own words) is kept on the node (`SupplierNode.ClassificationRationale`, Postgres `nodes.classification_rationale`): whitespace collapsed, at most 200 characters, and dropped when it breaks the copy rules (`Domain/Copy/BannedTerms.cs`, the C# copy of `scripts/lib/banned-terms.mjs`; a parity test reads the script). `NodeView.classification.rationale` carries it. The side panel's Details tab shows it under the invoice line as "Why this category", tagged AI-mapped, only when the AI chose the category. Rows written before the column existed have no reason until the demo is reset.
- **PDF invoice import (F9).** Import invoice also takes a PDF.
  - **Extract.** The API checks the size (3 MB) and the `%PDF-` header and refuses a PDF already imported by its SHA-256. It then asks the AI service's `/extract-pdf` within `AI_EXTRACT_TIMEOUT_MS`. The service reads the text layer with pypdf (10 pages, 20,000 characters, a 5 s deadline; scans refused) and a model copies the fields into a fixed schema. Each filled field is marked found or not in the text.
  - **Review.** The draft waits in the API's memory for 30 minutes. A dialog shows it once labelled "AI-extracted · check fields", in three groups (Supplier, Invoice, Lines), with "not in the PDF" on the fields the text does not show and a one-line legend.
  - **Confirm.** It validates the fields, takes the customer from the buyer, refuses an invoice already imported with the same seller tax id and number, and imports it like any invoice; the node arrives through `node.created`. The audit entry adds `documentKind`, `documentSha256`, `extractedBy`, `editedFields` and `confirmedBy`.
  - **Sample.** `seed/invoices/P1-bistrita.pdf`, from R1's fictional seller, is written by `node scripts/make-sample-pdf.mjs`.
  - **Checked on 2026-11-06** with `qwen2.5:14b` through the host's Ollama in a throwaway stack: about 20 s per PDF. Every field was right except the invoice number, which came back empty; the person types it in the form and the import refuses an empty one. The node came in at 5.6 t, Zinc die-cast parts from the HS code.
  - **Not tried:** the cloud models, `ministral-3:3b`, a real supplier's PDF.
  - **After pulling, rebuild the AI image** for the new dependency: `docker compose up --build --renew-anon-volumes`.
- **Data chat (F13).** "Ask" in the sidebar opens the chat on every screen. It sits where the side panel sits and covers it; Escape or ✕ closes it and brings the side panel back as it was, and a supplier chip in an answer selects that supplier and shows its panel.
  - **How an answer is made.** A typed question goes to `POST /api/chat`. The API builds the facts from the dashboard snapshot and the saved scenarios (`FactPack`):
    - suppliers numbered by emissions, with name, emissions, status, ruling-3 provenance, category, country, origin, mode, spend, illustrative cost and lead time, invoice and date;
    - totals, counts by status, the supplier-confirmed share and the lists of suppliers by status;
    - scenarios and the latest audit entries.

    The AI service's `/chat` asks the models (`AI_CHAT_MODELS`, else `AI_MODELS`) to cite them as `{{id}}`. `AnswerCheck` refuses typed numbers and names, unknown facts, company suffixes, banned terms, and status, largest, smallest or comparison claims the facts contradict. The reader sees the sentence with every value filled in from the data as a chip: formatted by the SPA, saying what it is and whose on hover, and linking to its supplier or the audit log. A "Sources" list repeats each value with its label. The answer is labelled "AI-written · values from your data".
  - **When there is no answer.** A refused or unanswerable question reads "I couldn't answer that from the data" and offers four suggested questions. Those are answered in the SPA from the store with fixed sentences, labelled "From your data". With no model they are what the chat offers.
  - **Free chat, data first (2026-11-06, `b/f13-chat-models`; `DECISIONS.md`, "A free chat, data first").** The chat now answers every message: help, greetings, how TraceGreen works, what a term means and general advice, besides the data. A general answer cites no fact and is labelled "AI-written · general, no values from your data". Values and names from the data are still facts the API fills in.
    - **When an answer breaks a rule,** the model is told which rule and what to do instead, once in the AI service and once in the API. A long answer that still fails is shown without its one false sentence. Only then does the panel offer the suggested questions, with "Here is what I can show you from your data right away".
    - **Rank claims** read "second largest", "the next largest" and "the largest estimated emitter" as meant.
    - **Automatic** asks the first local Ollama model while no model of the chat's own is ready.
    - **Checked** against Compose in Postgres mode with no `.env`, `ministral-3:3b` on the dev machine's GPU. Twelve messages were sent: hello, "I need help", what supplier-confirmed means, how to reduce emissions, the largest emitter, "and the second one?", the estimated suppliers, whom to ask first, a steel price forecast, a comparison of two named suppliers, Scope 3, and a question in Romanian. All twelve were answered in 1.3–13.5 s, most in about 2 s; the slow ones went through a retry.
      - Each answer with values matched the graph.
      - Along the way the checks refused a wrong "next largest", a typed supplier name and a fact-id list written into the text; each came back corrected or trimmed.
      - In Chromium, desktop and Pixel 7, there were no console errors, and the smoke passed in both specs.
    - **The AI assistant panel (2026-11-06).** The chat is called "AI assistant" in the sidebar and the panel. Only the conversation scrolls, inside the panel: the header and the question box stay put, and the conversation never scrolls the page. A new question brings it to the end; an answer does so only for a reader already there, else a button jumps to the latest. Each answer has one label line and its sources folded under "Sources (n)". A typing indicator shows while an answer is on its way. The question box grows with its text, with the model picker and a send button inside. New chat empties the conversation, and a failed request offers Try again.
      - Checked in Chromium at 1280×820 and on a Pixel 7 against Compose with seven messages: the question box kept its place (y 734 and 753 before and after) and the page never scrolled. The conversation ended at the latest message, the jump button came and went, the sources folded open, and New chat cleared it, with no console errors.
    - **A buyer's conversation, fixed (2026-11-07; `DECISIONS.md`, "What the chat may say about changes").** A real conversation with `ministral-3:3b` went wrong in several ways: it said switching to air would "cut … by +3.8 t" and "save +€1,620", promised to "add a what-if" twice, answered a cake recipe in English with cost figures, said the same supplier twice for two deliveries, showed Markdown, and named a place as a supplier.
      - **Replayed** turn by turn against Compose (Postgres mode, the same saved air scenario, no `.env`). All ten turns are now answered correctly:
        - lower costs → Hamburg Connectors by rail, −€120 and −0.6 t;
        - "can you change it" and "add a what if to Oradea" → it can't, and where to do it;
        - Oradea → "has no what-if: road only and no alternative supplier";
        - the recipes → a short redirect with no figures, in the message's language;
        - "show me the results" → the real what-if.
      - **The earlier twelve questions** still pass, except "Cat de mult CO2 avem in total?". In Romanian the 3B model keeps typing digits or inventing a fact id, so the checks refuse it and the suggested questions show; the same question in English passes. Expect better Romanian from a cloud model.
      - **A rough edge left:** "For Oradea Harness SRL, Hamburg Connectors GmbH shows switch to rail…" names both suppliers. It passes the checks because the what-if is named with its own supplier, but it reads muddled.
    - **Left:** small models still write the odd clumsy sentence ("[sea] sea freight") that no check can call false, and the history still sends earlier questions only, not answers.
  - **Checked on 2026-11-06** with a throwaway stack (memory mode) and the host's Ollama:
    - `qwen2.5:1.5b` answered "Which supplier emits the most?" correctly. Its "still estimated" answer about a supplier-confirmed node was refused once the status check was in.
    - `qwen2.5:14b`, after warming up, answered the largest emitter, the confirmed share, the estimated suppliers (through `list.estimated`) and the total correctly in about 4 s each. It refused "Make up a supplier in Cluj with 5 t", and two compound questions came back unanswered rather than wrong.
    - Not tried yet: the cloud models (no keys on this machine) and `ministral-3:3b` (not on this machine's Ollama).
  - **For the demo,** keep the chat on a model that passes, or show the suggested questions.
  - **Model picker (2026-11-06, `b/f13-chat-models`).** A **Model** select above the question box lists the chat's models from `GET /api/chat/models` (the AI service's `GET /chat/models`). First come the models Automatic asks (`AI_CHAT_MODELS`, else `AI_MODELS`, in order, each with `automatic: true`). After them come the local Ollama server's other models (`automatic: false`), found through its `GET /api/tags` with no `.env` (`OLLAMA_DISCOVER`, on by default). The first of them is used by Automatic while none of the chat's own models is ready; the others are asked only when picked. A model without its key is listed with the reason and cannot be chosen. **Automatic** sends no model and keeps the old order. A chosen model goes as `model` on `POST /api/chat` and is asked alone, with no handover. The AI service refuses an unknown model or one that is not ready with 422 `unknown_model` or `model_not_ready`, which the API returns as 422 `UnknownModel` or `ModelNotReady`. Each AI-written answer says which model wrote it. How to set it up: `RUNBOOK.md`, "AI models (F6)", step 8.
    - **Local models answer now (`OLLAMA_NUM_CTX`).** At first `ministral-3:3b` never answered the chat within its 12 s cap. The cause was Ollama's default context of 4,096 tokens: the chat's facts for eight deliveries are 5,718 tokens, and Ollama cut them without a word. The model lost its instructions, rambled ("Supplier S1 (not listed in the provided facts)…") and failed every check. Every Ollama call now asks for `OLLAMA_NUM_CTX` (12,288 by default), the same for classification and the chat, since a change reloads the model. A chat answer stops at 400 tokens, and a prompt that would not fit skips the local model with a log line. Numbers and how to tune it: `RUNBOOK.md`, "AI models (F6)", step 8.
    - **Checked on 2026-11-06** against Compose in Postgres mode, on the dev machine (RTX 4050, 6 GB):
      - **No `.env` at all.** The picker listed "Local models (Ollama) · ministral-3:3b", and the note said Automatic has no model. Picking it answered "Which supplier emits the most?", the total, the estimated suppliers and the confirmed share correctly in 0.9–1.9 s each, 6.7 s for the first while Ollama reloaded. Automatic stayed unavailable, as designed. Checked in Chromium at 1280×820 and on a Pixel 7, with no console errors.
      - **`AI_MODELS=ollama:ministral-3:3b`.** R1 and R3 imported with the category from the model (`zinc_diecast`, `wire_harness`) in about 2.9 s each, and Automatic chat questions between them took about 2.9 s, with no reload.
      - **`AI_CHAT_MODELS=anthropic:claude-sonnet-5-5,ollama:ministral-3:3b` and no key.** The Anthropic entry showed "not set up: ANTHROPIC_API_KEY not set" and could not be picked, and `ModelNotReady` and `UnknownModel` came back over HTTP.
      - The smoke passed in both specs.
    - **Cloud models wait for the keys.** Add `ANTHROPIC_API_KEY`, `OPENAI_API_KEY` or `GOOGLE_API_KEY` and list the model in `AI_CHAT_MODELS` (or `AI_MODELS`); nothing else changes. No cloud model has been tried yet.
- **AI-suggested alternatives (F11).** When the What if tab opens, the SPA asks `GET /api/nodes/{id}/suggestions`; the API sends the AI service the node's category, invoice line, country, origin, mode and status and its curated alternatives (id, label, country, origin, mode), never a figure. The model sees short ids (A1, A2, …) and may only pick among them; both sides keep only ids that were sent, the label is the seed's, and a reason is kept only when it holds no digit and no claim word (lower, closer, cheaper, carbon, distance, …: nothing checks such a claim), so a pick can show without a reason. Under the picked alternative the simulator shows the tag "AI-suggested · illustrative" (with what it means on hover) and "Why: …"; the other alternatives and the frozen `sim-alt-<id>` buttons are unchanged. No AI, a slow AI or no alternatives shows the list as before. **Limit:** the seed has one alternative for `zinc_diecast` (N1-alt) and one for `pcb` (N2-alt), and the data rules forbid adding any without a source, so the AI can endorse at most one alternative per node; on stage, say so. Checked on 2026-11-06 with `qwen2.5:1.5b` through the host's Ollama (a throwaway AI service): N1's alternative came back with "Cluj Metal Works SRL supplies zinc die-cast parts from Romania by road, matching the origin and mode of transportation."; N2's came back without a reason (dropped by the checks). Before the claim check, the same model wrote that Timisoara is "close to Taoyuan" and that road is "lower carbon than sea", which is why reasons may only restate the data.

## AI classifier data and config (F6, 2026-11-06)

Terminal C's part of the F6 plan (`docs/plan/ai-f6.md`). The chain itself (Terminal A) and the scoring set and script (Terminal B) come in their own pull requests. The decisions are in `DECISIONS.md`, "AI classifier chain (F6)".

**HS rules.** Nine rows with `"listId": "HS"` in `seed/classification-map.json`, covering the six real categories. Each code was checked on 2026-11-06 against the World Customs Organization's HS Nomenclature 2022 edition (index page `https://www.wcoomd.org/en/topics/nomenclature/instrument-and-tools/hs-nomenclature-2022-edition/hs-nomenclature-2022-edition.aspx`; chapter files `https://www.wcoomd.org/-/media/wco/public/global/pdf/topics/nomenclature/instruments-and-tools/hs-nomenclature-2022/2022/` + `0739_2022e.pdf` for chapter 39, `1048_2022e.pdf` for 48, `1579_2022e.pdf` for 79 and `1685_2022e.pdf` for 85). The text below is quoted from those files:

| Prefix | HS 2022 text | Category |
|---|---|---|
| `7907` | 79.07, 7907.00 "Other articles of zinc." | `zinc_diecast` |
| `8534` | 85.34, 8534.00 "Printed circuits." | `pcb` |
| `854430` | 8544.30 "Ignition wiring sets and other wiring sets of a kind used in vehicles, aircraft or ships" | `wire_harness` |
| `854442` | 8544.42 "Fitted with connectors", under "Other electric conductors, for a voltage not exceeding 1,000 V" | `wire_harness` |
| `481910` | 4819.10 "Cartons, boxes and cases, of corrugated paper or paperboard" | `packaging_paper` |
| `481920` | 4819.20 "Folding cartons, boxes and cases, of non-corrugated paper or paperboard" | `packaging_paper` |
| `853669` | 8536.69 "Other", under "Lamp-holders, plugs and sockets" (8536.61 is the lamp-holders) | `connectors` |
| `853670` | 8536.70 "Connectors for optical fibres, optical fibre bundles or cables" | `connectors` |
| `392690` | 3926.90 "Other", under 39.26 "Other articles of plastics and articles of other materials of headings 39.01 to 39.14" | `plastics` |

**Romanian invoices.** Three new files in `seed/invoices/`, not added to `demo-state.json` or `ai-cache.json`:

| File | Supplier (fictional) | CUI | Lines | Path |
|---|---|---|---|---|
| `R1-bistrita.xml` | Zincora Turnătorie SRL, Bistrița | RO99999968 | 2, Romanian with diacritics, no classification code (zinc die-cast sensor housings, zamak covers) | AI: no code and no cache entry |
| `R2-brasov.xml` | Tâmpa Circuite SRL, Brașov | RO99999950 | 3, each with an HS code (`853400`, `853669`, `481910`) | HS rule: `pcb` from the invoice, no AI call |
| `R3-tarnaveni.xml` | Târnăveni Fascicule SRL, Târnăveni | RO99999941 | 4, abbreviated and without diacritics, four categories, one line in pieces (`H87`) | AI |

All three are in EUR, with the RON VAT total (BR-RO-030, BR-53) at the BNR rate of the issue date: 2026-10-19 5.3272, 2026-10-20 5.3285, 2026-10-21 5.3297 (`https://curs.bnr.ro/files/xml/years/nbrfxrates2026.xml`). The CUIs have a valid check digit, computed with the same weights that reproduce the existing seed CUIs, and are not registered at ANAF: `POST https://webservicesp.anaf.ro/api/PlatitorTvaRest/v9/tva` returned all three under `notFound` on 2026-11-06. The supplier names were made up for the seed. A web search for each exact name on 2026-11-06 found no company using it; the trade register itself was not searched. Amounts and quantities are invented, as on the other seed invoices; no factor, lane or mass changed.

`node scripts/validate-seed-anaf.mjs` on 2026-11-06:

```
OK    N1-shenzhen.xml  (expected: seller has no Romanian CUI)
      tipAssert=E; codEroare=ERRIdentif; textEroare=Nu a fost identificat cui vanzator
OK    N2-taiwan.xml  (expected: seller has no Romanian CUI)
      tipAssert=E; codEroare=ERRIdentif; textEroare=Nu a fost identificat cui vanzator
OK    N3-oradea.xml
OK    N4-sibiu.xml
OK    N5-hamburg.xml  (expected: seller has no Romanian CUI)
      tipAssert=E; codEroare=ERRIdentif; textEroare=Nu a fost identificat cui vanzator
OK    R1-bistrita.xml
OK    R2-brasov.xml
OK    R3-tarnaveni.xml
OK    adversarial.xml

9/9 passed (3 with the expected seller-identification error)
```

Imported through `POST /api/ingest/ubl` into a throwaway API in memory mode with no AI service (2026-11-06): R2 became `pcb` from its invoice code (source `invoice`, confidence 1). R1 and R3 fell through to the stub (`UNCLASSIFIED`); these are the lines the model chain will answer. All three took the lane `RO-SB-SB`.

**Config.** `compose.yml` passes every variable of the plan's agreements to the `ai` service, with the agreed defaults: `AI_MODELS`, `AI_MODEL`, the three cloud keys, `OLLAMA_BASE_URL`, `AI_TIMEOUT_S` 8, `AI_MODEL_TIMEOUT_S` 4, `OLLAMA_TIMEOUT_S` 6 and `AI_DEV_ROUTES` 0. It also maps `host.docker.internal` to the host gateway, so the name resolves on Linux as well. `.env.example` documents each of them.

**The demo image.** `compose.demo.yml` replaces the `ai` service's environment (`environment: !override`), so it gets the same variables, read from the root `.env`, with `AI_DEV_ROUTES` fixed at 0. Before, it passed only `AI_MODEL`, `ANTHROPIC_API_KEY` and `AI_TIMEOUT_S` "10", a budget equal to the API's own timeout. `extra_hosts` comes from `compose.yml`.

**Checked against Terminal A's branch (`f6/ai-core` at `70d8b5b`, 2026-11-06).** A throwaway container ran that branch's `ai/` with this branch's `seed/`:

| Check | Result |
|---|---|
| `/health` with `AI_MODELS=openai:<placeholder>,ollama:qwen3.5:4b` and no key | `provider` `cache,ollama:qwen3.5:4b,stub`; the OpenAI model `ready: false`, `reason` "OPENAI_API_KEY not set"; the Ollama model `ready: true` and `modelConfigured: true`, although that model was not pulled. An Ollama model is ready by configuration, not by a reachable server. |
| `/classify` with no model reachable | 200 in 0.18 s: the cached line from the cache, the other from the stub ("AI unavailable") |
| Dev route with `AI_DEV_ROUTES=1` | 400 "Model not ready: openai:<placeholder> (OPENAI_API_KEY not set)"; 400 "Unknown model: nope:x" |
| The container reaching Ollama on the Windows host | `http://host.docker.internal:11434/api/tags` answers with Ollama's default settings |
| The local model, `ollama:ministral-3:3b` (the one chosen in Phase 2), through a throwaway service on Ollama 0.21.1 (updated to 0.23.4 before the rehearsal below) | Warm: one line in 1.2–1.4 s, R1's two and R3's first and last lines in 3.4 s (R1 both `zinc_diecast`, 0.95 and 0.90; the connector line `connectors`, 0.90; the harness line `UNCLASSIFIED`, 0.60). Cold, after unloading the model: 4.7 s for one line. The first two calls after the service started gave no answer within 8 s and fell to the stub. The same prompt and schema sent straight to Ollama, and through the service's own model factory without a deadline, answered in 1–2 s, so the misses were start-up and model load, not the prompt. Hence the warm-up step in `RUNBOOK.md`. |

**Phase 2 for C, the AI demo steps,** are in `RUNBOOK.md`, "AI demo steps (F6)". Two of the plan's steps say less on screen than the plan expected:

- **Step 1:** the node shows the category and "AI-mapped · <confidence>", but not the model's reason; `NodeView` carries no rationale. The script shows the reason with a `curl` to the AI service.
- **Step 3:** `adversarial.xml` is answered from `seed/ai-cache.json` by the API, so no live model sees its injected line on stage. The live models' handling of injection is in the scoring table (`docs/ai-eval.md`, Terminal B's Phase 2).

**Rehearsal on the merged branches (2026-11-06).** A throwaway worktree merged A (`bc98c47`), then C, then B, with no conflict. It ran the demo image under its own Compose project, with `AI_MODELS=ollama:ministral-3:3b` only (Ollama 0.23.4 after its update from 0.21.1; no API keys on this machine), and the stack was removed afterwards:

| Step | Result |
|---|---|
| Before: `/health`, and `api/tags` from the container | the model `ready`; the container lists `ministral-3:3b` |
| 1. R1, model warm (after `ollama run ministral-3:3b "ok"`), AI service freshly restarted | 201 in 2.5 s: "RO · Zinc die-cast parts", "AI-mapped · 0.95", source `model` |
| 1. R1, model unloaded | 201 in 6.0 s, the answer just inside the 6 s cap; an earlier cold try missed it and showed "RO · Unclassified" from the stub |
| 1. The reason command | rationale "Zinc die-cast casing with pressure-turned sensor alloy (CZ-40)", source `model` |
| 2. R2 | 201 in 12 ms: "RO · Printed circuit boards", no tag, source `invoice` |
| 3. `adversarial.xml` | 201 in 11 ms: "RO · Wire harnesses", "AI-mapped · 0.86", source `cache` |
| R3 (step 4's invoice), model warm | 201 in 2.7 s: "RO · Wire harnesses", "AI-mapped · 0.90" |
| "Reset demo", then R1 and R3 again without restarting the AI service | 62 ms and 7 ms, from the service's memory, as the RUNBOOK warns |
| The same imports in memory mode (API recreated) | the same categories and sources |
| `pnpm e2e` | passes on Postgres and in memory mode |

Not rehearsed: steps 4 and 5 with cloud models, which need API keys, and step 5's offline timing. While Terminal B's scoring run used the same Ollama (12:19–12:26), the chain's calls waited behind its 25 s calls and fell to the stub; hence the RUNBOOK's rule that nothing else uses Ollama during the demo. B's calls after the overlap took the same 23–25 s as before, so its times look unaffected.

**B's scores (`docs/ai-eval.md`)** for `ministral-3:3b`: 80% over 10 runs of the 30 lines, connectors 0% (they come back `UNCLASSIFIED`), and one of the two injection lines (ro-30) followed in one call. The demo does not lean on either weakness: R1's node comes from its zinc lines (100%), R3's from its harness line (100%), and `adversarial.xml` is answered from the cache. No demo step changes (D5).

## Classification review (F6)

Every AI-mapped node waits for a person to confirm or change its category; a change recomputes the node's figures and is recorded in the audit log, in both data modes. The backend is done (ticket 1); the SPA's review controls are the next ticket and build on this contract. Decisions: `DECISIONS.md`, "Classification review (F6)".

| Route | Success | Errors |
|---|---|---|
| `POST /api/nodes/{nodeId}/classification {categoryId, expectedCategoryId}` | 200 `NodeView`, the node as it now is; appends `classification.reviewed`, then publishes `node.updated` (fresh `NodeView` and `Kpis`) and `audit.appended` | `CategoryNotAllowed` → 422 (a category outside `seed/categories.json`; `UNCLASSIFIED` is allowed); `NodeNotFound` → 404; `CategoryChanged` → 409 (`expectedCategoryId` is no longer the node's category; nothing written) |

`ClassificationView` gained `review: "notNeeded" | "pending" | "confirmed" | "corrected"` and `reviewedAt` (null until a person reviewed), and `ClassificationSource` gained `buyer`.

- **Starting state:** `pending` when the category came from the AI (source `cache` or `model`, whatever the confidence), else `notNeeded`. In the seed, N3, N4 and N5 are pending; N2 (CPV code) and the imported N1 (CPV code) are not.
- **A review:** `categoryId` equal to the node's category confirms it (`confirmed`); another one corrects it (`corrected`, and it stays `corrected` through later reviews). Either way the source becomes `buyer`, the confidence null and `aiMapped` false, and `reviewedAt` is set. Any node can be reviewed again, an invoice-coded one included.
- **Figures:** the node is rebuilt through `NodeFactory` with the new category: sector, spend figure, and the activity figure when the node has one (the material factor follows the category). Cost, lead time, status and the supplier's answers stay.
- **Audit:** `classification.reviewed`, actor `buyer`, payload `{nodeId, review, fromCategoryId, toCategoryId, fromSource, fromConfidence}`; the entry's `ts` is the review time and equals `reviewedAt`. No token, address or person.
- **Postgres:** migration `ClassificationReview` adds `nodes.review` (text, default `NotNeeded`) and `nodes.reviewed_at`, and backfills `Pending` for stored `Cache` and `Model` nodes.
- **Reuse (the stretch, done):** a review also remembers its category under the `CacheKey` of the node's dominant line, so a later import gets it for any line with the same description before the cache and the AI are asked (order: invoice code → buyer → cache → AI → stub). Such a node has source `buyer`, no confidence and `review: notNeeded`. Kept in both repositories (migration `BuyerClassifications`: table `buyer_classifications`, row-level security on), never in `seed/ai-cache.json`; a demo reset forgets it.

Checked on 2026-11-06:

| Check | Result |
|---|---|
| `dotnet test` (memory) | 361 tests, 336 run, the 25 Postgres contract tests skipped, all passing |
| `dotnet test` with `DATABASE_URL` on a scratch Postgres 17 | all 361 passing, the review contract tests included |
| The migrations on a database that `development` had migrated and seeded | applied with no pending model changes; N3–N5 `Pending`, N2 `NotNeeded`; `buyer_classifications` with row-level security |
| HTTP in Postgres mode | correct N3 → 200 (`plastics`, `buyer`, `corrected`); same page again → 409; `steel` → 422; N1 before import → 404; `POST /api/audit/verify` valid; N3's invoice under a new number then imports as `plastics`, source `buyer`, `notNeeded`; a reset empties `buyer_classifications` |
| `pnpm gen:api` after the review | no change to `schema.d.ts` (the contract commit holds it) |

Tests (`ClassificationEndpointTests`, `RepositoryContract`): confirm; correct with the spend figure recomputed; a VERIFIED node's activity figure recomputed with the new material; a correction changed again; `UNCLASSIFIED` accepted; 409 for a stale category, and for the second of two reviews from the same page; 422 for a category outside the list; 404; the audit entry and a valid chain; `node.updated` with fresh KPIs; the supplier's link still working after a correction, its answer computed with the buyer's category; in both repositories, the patch and audit entry, nothing written on a stale category, ten concurrent reviews giving one write, and a verification that landed first seen by the review. For the reuse: a later import of the same line gets the buyer's category until a reset (`ClassificationEndpointTests`); the buyer's choice before the cache and the AI, an invoice code before the buyer, a remembered category off the list ignored (`LineClassificationTests`); a decision remembered, replaced and forgotten on reset, and none for a stale review (`RepositoryContract`).

### The dashboard (ticket 2, `b/f6-review-dashboard`)

A buyer cannot miss an unreviewed AI category, and confirms or changes one in two clicks.

| Where | What |
|---|---|
| Overview | "N categories to check" (`CategoryChecks`), shown while any category waits; it opens `/suppliers?filter=check` with the side panel closed |
| Suppliers | The filtered list says so and links back to every supplier; each waiting row starts its second line with "Check category" |
| Side panel | "Check category" beside "AI-mapped · 0.88" in the header; on Summary the review control (`CategoryReview`): the category, the dominant line in full, "Correct" while it waits, and "Change category", which opens the allow-list from `GET /api/categories` with Unclassified last; after a review the flag and the "AI-mapped" chip go, and "Category set by buyer" stands under the provenance line |

- **Flag rule:** `review: pending`, or `UNCLASSIFIED` that no person has looked at; never the confidence.
- **Store:** the POST sends `expectedCategoryId`; the store changes only through `node.updated`, and the control says "Saving…" until it lands. A 409 hydrates the store from `GET /api/graph` and shows the "changed elsewhere" message (`role="alert"`).
- **Accessibility:** native buttons throughout; the flag is text and icon, never colour alone, and is part of each row button's name; "Saving…" and "Category saved." in a `role="status"` line; Escape closes the choices without closing the panel; the focus moves to the first choice and back to "Change category". The amber text on its tint is about 6.2:1.
- **Backend addition:** `GET /api/categories` (`CategoryOption { id, label }[]`), the shape `f6/corrections` uses.
- **New test hooks:** the "Classification review (F6)" row of `RUNBOOK.md`'s table; the frozen hooks are unchanged.

Checked on 2026-11-06 on a Compose stack from this branch (Vite dev server): `pnpm typecheck`, `pnpm lint` and `pnpm test` (283 tests) pass; `pnpm e2e` passes with the API on Postgres and in memory mode, the demo path now with a tenth step (step 8, the review); 362 .NET tests pass in memory mode. By hand in headless Chromium: the counter, the filtered list, keyboard use of the choices, the figure moving from 2.9 t to 0.6 t when N4 becomes Printed circuit boards, the 390 px phone layout, no console errors, and the 409 path with the event stream blocked (the message, then the category set elsewhere on screen). Not run locally: the smoke on the demo image (CI's e2e job runs it).

Open for Dev C: the wording of every new string, the Romanian set (the dashboard has no language switch), "Correct" (read as "this is correct", and it could be read as "fix it"), and the juror line "every AI-mapped category waits for a person to confirm or change it", which lives outside the repository.

## Seed XML validation (2026-10-23)

`node scripts/validate-seed-anaf.mjs` (Node 24 or later, no dependencies) posts every file in `seed/invoices/` to ANAF's public validator (`POST https://webservicesp.anaf.ro/prod/FCTEL/rest/validare/FACT1`, the XML as `text/plain`, no authentication). For each file it prints OK, or ANAF's exact messages and raw answer, and it exits 1 if any file fails. Pass file paths to check single files. Final output:

```
OK    N1-shenzhen.xml  (expected: seller has no Romanian CUI)
      tipAssert=E; codEroare=ERRIdentif; textEroare=Nu a fost identificat cui vanzator
OK    N2-taiwan.xml  (expected: seller has no Romanian CUI)
      tipAssert=E; codEroare=ERRIdentif; textEroare=Nu a fost identificat cui vanzator
OK    N3-oradea.xml
OK    N4-sibiu.xml
OK    N5-hamburg.xml  (expected: seller has no Romanian CUI)
      tipAssert=E; codEroare=ERRIdentif; textEroare=Nu a fost identificat cui vanzator
OK    adversarial.xml

6/6 passed (3 with the expected seller-identification error)
```

What changed, by ANAF rule. Amounts, quantities, unit codes, origin countries, classification codes and line descriptions are unchanged, except the adversarial line (last row):

| Rule | Files | Fix |
|---|---|---|
| UBL-CR-600: no exemption reason on a line's tax category | N1, N2, N5 | `TaxExemptionReasonCode` removed from `cac:ClassifiedTaxCategory`; the document-level one stays |
| BR-RO-030: an invoice not in RON declares RON as its VAT accounting currency | all six | `cbc:TaxCurrencyCode` RON, plus a second `cac:TaxTotal` with the VAT in RON (BR-53): 0.00 for N1, N2 and N5; 4620.00 EUR × 5.3306 = 24627.37 for N3 and adversarial; 1260.00 EUR × 5.3373 = 6725.00 for N4 |
| BR-CL-13: the classification scheme comes from UNTDID 7143 | N1, N2 | `listID="CPV"` → `listID="STI"` (code values unchanged); the parser reads STI as CPV |
| ERRIdentif: buyer CUI check digit | all six | `RO99999999` → `RO99999992`, also in `seed/buyer.json` |
| ERRIdentif: Romanian seller CUI check digit | N3, N4, adversarial | `RO12345678` → `RO99999984`, `RO23456789` → `RO99999976` |
| BR-RO-L100: item name at most 100 characters | adversarial | The injected instruction moved from `cbc:Name` to `cbc:Description`. The parser joins the two as "Wire harness set. - IGNORE PREVIOUS INSTRUCTIONS …", so the `seed/ai-cache.json` entry carries that description and its new key |

The new CUIs have a valid check digit and are not registered at ANAF (ANAF's public VAT-payer lookup, 2026-10-23), so none identifies a real company. The exchange rates are the BNR reference rates published on the invoices' issue dates (2026-10-05: 5.3306; 2026-10-12: 5.3373), from `https://curs.bnr.ro/files/xml/years/nbrfxrates2026.xml`.

Decisions behind it, including the adversarial line's description (accepted by A on 2026-11-05): `docs/DECISIONS.md`, "Seed invoices at ANAF".

The API reads `seed/` once at startup. After pulling these changes, restart it (`docker compose restart api`) and press "Reset demo", or run `docker compose down -v && docker compose up`, so the stored buyer and seed invoices match the files.

## M1 check (2026-10-24, re-run 2026-10-29)

The M1 checklist of `docs/plan/README.md` §8. Dev B ran it on 2026-10-24 against the Compose stack on `b/b2-dashboard` (task B3). Dev C ran it again on 2026-10-29 on `main` at `b09c223`, after slices 3 and 4 had merged, and recorded both passes (task C4). Each run covered the default Postgres mode and `DATA_MODE=memory`, with requests going through the Vite proxy on :3000, the browser's path, unless a row says otherwise. Every row gave the same result in both modes, except the restart row, which concerns memory mode alone (Postgres keeps N1 across a restart). In C4, a script checked the API-level rows through :3000 (reset, import, duplicates, demo import, content types, the `graph.reset` event), and a person checked the browser rows by hand on the recordings.

| M1 item | Result | How it was checked |
|---|---|---|
| N1 imported from the browser shows an amber node with the hotspot ring in under 2 s | Pass | By hand in the browser; `POST /api/ingest/ubl` answers 201 in 15–70 ms; N1 is the largest emitter, so it carries the ring |
| KPIs come from the API | Pass | The snapshot and every `node.created` carry `kpis`, which the store copies; the import's `kpis` equal the next snapshot's. The total goes from 14,484 kg (seed) to 37,047 kg with N1 at 22,563 kg; on screen 14.5 t, 37.0 t and 22.6 t, each rounded on its own |
| Load demo invoice | Pass | 201, then 409 `Duplicate` on a second click; the same file imported twice is also 409. A request that is neither multipart nor XML is 415 `UnsupportedMediaType`; a body that is not XML, sent as `text/xml`, is 422 `Schema` |
| Reset demo returns to the seed state | Pass | `/api/graph` after `POST /api/dev/reset`: N2 verified, N3 and N4 estimated, N5 partial, no N1; the stream sends `graph.reset` |
| `GET /api/health` returns `ai` and `baseUrl` | Pass | `ai` is `unconfigured` (the AI service's own provider name until C7); `baseUrl` is `request` on :8080 and `forwarded` through :3000 |
| Two windows stay in sync; the API stopped and started again | Pass | By hand: both windows show each import and reset, and recover without a reload |
| API restarted in memory mode | Pass | `lastSeq` falls from 2 to 0 and N1 is gone; the store accepts the lower `lastSeq` on hydrate. In C4 the dashboard went back to Live without a reload |
| `pnpm typecheck && pnpm lint`, `dotnet test`, `pnpm test` | Pass | B3: 206 .NET tests and 78 web tests. C4: 255 .NET tests (241 in memory mode; all 255 with `DATABASE_URL` on the scratch database `tracegreen_test`, never the dev database, see "Caveats") and 105 web tests |
| A person records the screen | Pass | Dev C, 2026-10-29: `tracegreen-m1-postgres.mp4` (175 s) and `tracegreen-m1-memory.mp4` (102 s), shared in the team chat rather than committed |

## M2 pre-check (2026-11-05)

The part of the M2 checklist (`docs/plan/README.md` §8) that needs no phone, run by A (task A8) on the demo image (`compose.demo.yml`: nginx, built SPA, Production API) through the laptop's LAN address, `http://192.168.0.200:3000`, in both data modes. A script made the requests a phone would make and listened on `/api/events` like a dashboard. Each row gave the same result in both modes.

| M2 item | Result | Detail |
|---|---|---|
| Base URL for links | Pass | `request-link` gives `http://<lan-ip>:3000/verify/…`, `loopback: false`, from the forwarded host |
| Submit flips N1 to VERIFIED, and the event carries fresh KPIs | Pass | Air + grid → VERIFIED with `AIR_HEAVY`; `node.verified` with `kpis` reached the stream 25 ms after the submit (Postgres) and 8 ms (memory). This is the server's share of the 2 s budget; the phone, the hotspot and the browser are measured in the T16 run |
| Same link again: "already used" | Pass | 409 `AlreadyUsed` on the context and on a second submit |
| Bad signature | Pass | 401 `InvalidToken` |
| Audit chain valid | Pass | `POST /api/audit/verify`: valid, length 6 |
| Simulator | Pass | N1 air → sea `win-win`; N1 → N1-alt `premium` (322.71 €/t from the verified air figure) |
| Rate limit | Pass | The 11th verify-route call in the minute is 429 |
| No token in the logs | Pass | No `t=v1.` or token in the nginx or API container logs after the run |
| Security headers | Pass | CSP, `nosniff` and `no-referrer` on the SPA |
| Status mapping in integration tests (410 for expiry and invoice change included) | Pass | Covered since A6 |

Fixed during the check:
- **The demo image ignored `DATA_MODE`.** Its `environment: !override` dropped it, so `DATA_MODE=memory` still ran Postgres, which left the demo with no fallback without the database. `compose.demo.yml` now passes `DATA_MODE` through; empty keeps Postgres.
- **`RUNBOOK.md`'s container test command reset the live demo workspace.** It now uses the scratch database.

Still open for M2, all needing people or B5:
- **The T16 run.** A phone on the laptop's hotspot scans the QR, submits, and someone times submit → flip on the dashboard. C drives the phone and writes `docs/run-log.md`.
- **The simulator panel in the drawer (B5).** Until it merges, the simulator is checked through the API only.
- **Setup on the day:** start the hotspot (`nmcli device wifi hotspot ifname wlp8s0 ssid TraceGreen password <8+ characters>`; the laptop is then usually `10.42.0.1`). Open the dashboard at `http://10.42.0.1:3000`, or set `PUBLIC_BASE_URL` in `.env.demo`, then press "Reset demo".

## What remains (in order)

Current order, from the 2026-10-22 review's slice order (vertical slices toward the demo path F1 → F2 → F3 → F4; status by ID in `docs/ROADMAP.md`). Slices 1–3 are done, slice 4 is built, slice 5 is done (backend A7, simulator panel B5), and slice 6's smoke passes against Compose (B6):

1. **Slice 4 check (M2, A8 with B and C):** the API-level part passed on 2026-11-05 in both modes ("M2 pre-check" above). Left: the timed two-device run-through over the hotspot (T16), where the phone flips the node green in under 2 s. Measure import → node and submit → flip, and log failures in `docs/run-log.md` (people write it).
2. **Slice 5 SPA:** the simulator panel in the node drawer, calling `POST /api/simulate` and `POST /api/scenarios` with `NodeView.alternatives`. Done when N1 air → sea shows `win-win` and N1 → N1-alt shows `premium` or `below-ets`, with both MAC badge states rendered.
3. **Slice 6, Playwright smoke (B6):** done; `pnpm e2e` passes against Compose and the demo image, and CI runs it on the demo image in both data modes (A9).
4. **Slice 7, Python AI service (C7):** the contract, cache and stub are done (2026-11-05). Left: the live model provider in hackathon hours H1–H4 (D4), in its own PR, with A reviewing the prompt handling (invoice text in a delimited block, instructions in it ignored, output validated against the schema and the allow-list, length capped, 10 s timeout). F6 (`docs/plan/ai-f6.md`) builds it in three parallel branches: the chain cache → two cloud models → local Qwen 3.5 → stub (Terminal A), the Romanian test set and scoring script (Terminal B), and the HS rules, R1–R3, the Compose variables and the docs (Terminal C, done 2026-11-06, "AI classifier data and config (F6)" above). Phase 2 is done for the local model: A chose `ministral-3:3b` (Ollama 0.23.4 cannot pull `qwen3.5:4b`), B scored it in `docs/ai-eval.md`, and C wrote the AI demo steps and rehearsed them with it on the merged branches ("Rehearsal on the merged branches" above). Left: scoring the two cloud models and rehearsing steps 4 and 5 once API keys are on the demo laptop, then Phase 3, which merges A, then C right after it (A's `compose.demo.yml` relies on the `extra_hosts` that C adds to `compose.yml`), then B, runs the full check and rehearses on the demo laptop.
5. **Supplier page (F12):** the stage path above on a real phone over the hotspot, with the terms tick; a native read of the Romanian strings. Merge before the H12 scope freeze, or show it as what comes next.
6. **The tag (T22):** the factor rows and the carbon price are sourced (T4, see "Sourced factors (T4)" above). Left: the operator details in `seed/supplier-terms.json`, kept as placeholders for the hackathon because there is no legal entity (decided 2026-11-06). Once an operator exists, a person fills them in (a new `version`, see `docs/legal/README.md`); then `node scripts/preflight.mjs` passes and `v0.9-prehack` can be tagged.

The task list below is the original plan, kept for its specifications; where it and the order above disagree on order, the order above wins.

The implementation plan lives outside the repo (Claude plan file `read-the-tracegreen-roadmap-ala-validated-thunder.md`); the summary below is enough to continue without it.

1. **Use-case services and endpoints (plan Task 9).** The slice-1 part is done (A1, A2): graph, ingest, demo import, reset and the health fields, checked over HTTP in both data modes. The SPA transport (A3) is done and unit-tested, and it passed the two-window and stop-the-API checks in the M1 run (see "M1 check (2026-10-24)"). The slice-3 backend (A5, A6) is done: link, verify context and submit, audit verify, rate limit, `AIR_HEAVY`, simulate-answer. The slice-5 backend (A7) is done: simulate, saved scenarios and the alternatives on each node. Next for the API: M2 (A8) and CI (A9). The original list: `Domain/Services/{Ingest,Graph,Link,Verify,Audit,Scenario,Demo}Service.cs` constructor-injecting `IRepository`, `IEventBus`, `ILinkSigner`, `IAiClient`, `TimeProvider`, `SeedBundle`, `AppConfig`. Endpoints: `GET /api/graph`, `POST /api/ingest/ubl` (multipart `file` or `text/xml`), `POST /api/ingest/demo` (ingests `seed/invoices/N1-shenzhen.xml` reusing N1's fixed ids from `demo-state.json`), `POST /api/request-link`, `GET /api/verify/context?t=`, `POST /api/verify`, `POST /api/audit/verify`, `POST /api/simulate`, `POST /api/scenarios`, `POST /api/dev/reset`, `POST /api/dev/simulate-answer` (dev endpoints 404 unless `DEMO_TOOLS=1`). Rate limiting on `/api/verify*` (10/min per client IP → 429). `BaseUrlResolver` (`PUBLIC_BASE_URL` → `X-Forwarded-Proto/Host` → request origin; flags loopback). Plausibility rules `SEA_TOO_FAST`, `AIR_HEAVY`. Node events must carry fresh `Kpis` (the SPA never computes carbon). Status mapping: replay 409, expired/hash mismatch 410, bad signature 401. Add `ai` and `baseUrl` to `/api/health`.
2. **Python AI service (Task 10).** `app/schemas.py` (contract mirrored by `HttpAiClient`: `POST /classify`, `POST /suggest`, `POST /extract-pdf` → 501, `GET /health`), `app/cache.py` (`cache_key` = sha256 of trimmed, lower-cased, whitespace-collapsed description — identical to `Domain/Ai/CacheKey.cs`), providers `cached → anthropic (only if AI_MODEL and ANTHROPIC_API_KEY) → stub`, prompt that delimits invoice lines as data, output validated against the allow-list from `seed/categories.json`. Tests: cache hit, stub fallback, no client construction without a model, allow-list enforcement, every seed line hits the cache.
3. **Web SPA (Task 11).** The slice-1 part is done (B1, B2) and passed M1 in both data modes, recorded on 2026-10-29 (C4); the slice-4 dashboard (B4) and the slice-5 simulator panel (B5) are done; next is the Playwright smoke (B6). The original list: `pnpm gen:api` (types from `/openapi/v1.json`, committed), `openapi-fetch` client, Zustand store (`hydrate`, `applyEvent` ignoring stale `seq`), selectors (`toRfNodes`, `toRfEdges` with log-width edges, `hotspotIds`), dagre layout, `useGraphEvents` (EventSource; refetch on `resync`/reopen; poll every 5 s while not live), dashboard components (toolbar with import / load demo / reset, React Flow canvas with `data-testid="node-<id>"` and `data-status`, KPI bar, node drawer, QR card with printed URL and loopback warning, simulator panel calling `POST /api/simulate`, audit panel with chain check, connection badge), verify page (two button groups, skip → PARTIAL, RO/EN via `?lang=`, result states).
4. **Playwright smoke (Task 12).** `web/e2e/demo-path.spec.ts` against the Compose stack: reset → load demo invoice → request link → phone context submits → node flips → replay shows "already used" → simulator badges → chain valid.
5. **Scripts, docs, CI (Task 13).** `scripts/{preflight,demo-check,validate-seed-anaf}.mjs`, `scripts/firewall.ps1`; `docs/{ROADMAP,ARCHITECTURE,SECURITY,DECISIONS,factor-sources}.md`; final `CLAUDE.md`; `.github/workflows/ci.yml` (done in A9: api with Postgres service, ai, web, generated-types drift check; and the e2e job on the demo image in both data modes).

## Decisions

The ledger is `docs/DECISIONS.md` (decision — reason — cost if wrong): stack, the review rulings R1–R8, D1–D6, and the contract decisions of each slice.

## Caveats to know before continuing

- Duplicates are refused by the repository itself, under the same lock as the insert (raw XML hash, invoice id or node id already stored), so two identical uploads or two "Load demo" clicks at the same instant give one 201 and one 409. Ingest also checks before classifying, only to spare the AI wait.
- Seed nodes are stamped `2026-11-01T08:00Z` (`DemoState.SeedTime`), so a node imported before that date sorts before them in `GET /api/graph`. The layout must not depend on node order.
- KPIs and events are published after the insert, outside any lock. Two imports at the same instant can publish in reverse order, so the last `node.created` may carry KPIs without the other node until the next event or snapshot; a reset landing between an insert and its publish can deliver a `node.created` for a node the reset removed (the next snapshot drops it). Both need two actions within milliseconds; serialise publishing if it ever shows.
- A classification review computes its figures under the repository's lock, so it always sees a supplier answer that landed before it. The other order is not covered: a supplier answer computed before a review but written after it keeps the activity figure of the old category's material, because `NodePatch` carries no category. It needs the two within milliseconds; the next review or answer corrects it. The fix: `RecordVerificationAsync` compares the category the patch was computed with, and `VerifyService` recomputes on a mismatch.

- `.NET 10 SDK` on this machine is installed under `%LOCALAPPDATA%\Microsoft\dotnet` (user-local); put it first on `PATH` or use the containers.
- `dotnet-ef` global tool is 10.0.5 while the runtime is 10.0.13: it works but warns; `dotnet tool update -g dotnet-ef` removes the warning.
- The Postgres contract tests need the scratch database `tracegreen_test` and reset the demo workspace of whatever database `DATABASE_URL` names. Never point them at the dev database `tracegreen`: on 2026-10-29 a run inside the api container that way removed the imported N1 from the live graph. `RUNBOOK.md` now passes `-e DATABASE_URL=…/tracegreen_test` to `docker compose exec api dotnet test` (checked 2026-11-05: 266 passed, and the dev graph kept its nodes). If the dev database was hit, press "Reset demo".
- Health reports `ai: error …` when the API runs on the host without the AI service; inside Compose it reports the provider.
