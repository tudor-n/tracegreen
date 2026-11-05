# Status and next steps

Last updated: 2026-11-05 (the demo tooling, task B7 of `docs/plan/dev-b.md`; slice 6, the Playwright smoke, task B6; slice 5, the simulator panel, task B5; the hand-over docs, task A10 of `docs/plan/dev-a.md`; CI, task A9; and slice 5, the simulator backend, task A7; before that, 2026-10-29, the M1 acceptance run in both data modes and its recording, task C4 of `docs/plan/dev-c.md`; 2026-10-27, slice 3, the verify loop's backend, tasks A5–A6 of `docs/plan/dev-a.md`; 2026-10-24, the M1 integration run in both data modes, task B3 of `docs/plan/dev-b.md`; 2026-10-23, slice 2, the seed invoices validated at ANAF, task C2 of `docs/plan/dev-c.md`; and 2026-10-22, the slice-1 backend and client transport, tasks A1–A3 of `docs/plan/dev-a.md`). Order of work: "What remains (in order)" below and `docs/plan/`. Decisions: `docs/DECISIONS.md`.

## What exists

Monorepo with three services behind one browser origin, brought up by `docker compose build && docker compose up` (see `RUNBOOK.md`).

| Area | State | Tests |
|---|---|---|
| `api/` .NET 10 host | Config from env (fails fast), OpenAPI at `/openapi/v1.json`, `GET /api/health` (mode, db, seed nodes, `ai`, `baseUrl`), `GET /api/graph`, `POST /api/dev/reset`, `POST /api/ingest/{ubl,demo}`, `GET /api/events` (Server-Sent Events with `id:`/`Last-Event-ID` replay, `resync`, `ping`), `POST /api/request-link`, `GET /api/verify/context`, `POST /api/verify` (rate limited), `POST /api/audit/verify`, `POST /api/simulate`, `POST /api/scenarios`, `POST /api/dev/simulate-answer`, startup flags `--migrate --seed --reset --exit` | integration tests via `TestWebAppFactory` (memory mode) |
| `api/` Domain (pure) | Model records; carbon math (`Carbon/`: factor table with provenance, spend and activity estimates, KPIs, MAC + badge, simulator); UBL 2.1 parser (namespace-agnostic, DTD rejected, 2 MB cap); canonical JSON + hashes; HMAC link tokens (`v1.` layout, hash-bound, constant-time); hash-chained audit log; `IRepository`, `IEventBus`, `IAiClient` ports; `LineClassification` (invoice code → cache → AI with timeout → stub); `DemoState` builder | unit tests, hand-computed expectations |
| `api/` Infrastructure | `MemoryRepository` (lock-atomic); `EfRepository` on Postgres (advisory lock per workspace, unique nonce, unique `(workspace_id, prev_hash)`, TRUNCATE-based reset); migrations `Init` + `Rls` (row-level security + append-only trigger on `audit_log`); `InMemoryEventBus` (ring buffer 500); `HttpAiClient` / `StubAiClient` | repository contract runs against memory always and against Postgres when `DATABASE_URL` is set |
| `ai/` FastAPI | `GET /health` only; uv project with pinned `fastapi`, `pydantic`, `anthropic` (installed, unwired) | pytest + ruff clean |
| `web/` Vite SPA | Dashboard at `/`: Zustand graph store (`hydrate`, `applyEvent`, `select`) with memoised React Flow selectors and dagre layout (B1); supplier nodes (status tone, hotspot ring, "i" factor tooltip, "AI-mapped" label), the KPI bar from API values and the toolbar (Import, Load demo invoice, Reset demo) (B2), kept live by `useGraphEvents` (A3). The node drawer with the QR card, the audit panel and the connection badge (B4). The simulator in the drawer (B5): mode buttons with the lane's missing legs disabled, the node's `alternatives` as swap buttons, the API's result with deltas (tonnes, euros, days; cost, lead time and the MAC labelled illustrative) and the MAC badge, a ghost node left of the supplier, and "Apply" (`POST /api/scenarios`); the base node never changes (see "Simulator panel (B5)" below). The supplier page `/verify/:nodeId` (C5): RO by default and EN through `?lang=`, the link's context, two pre-selected answer groups with skip (→ PARTIAL), and every result state (thanks with the non-blocking reconfirm banner, already used, expired, invoice changed, not valid, too many attempts, no connection with retry); its rules live in the pure `web/src/lib/verify-state.ts` and its requests in `verify-api.ts` (a request hanging past 10 s counts as no connection). In development, React's StrictMode sends the context request twice on load, so each page load counts twice against the verify rate limit; the production build sends it once. Tailwind v4, shadcn (button, card, badge, sheet, select, tooltip, separator), Vite proxy `/api` → API with `X-Forwarded-Host`, oxlint with `react/no-danger` as error | vitest (store, selectors, formatting, sync, toolbar and simulator requests, scenario display, verify page states and requests), typecheck, lint, build; the Playwright smoke `web/e2e/demo-path.spec.ts` against Compose (B6) |
| CI | `.github/workflows/ci.yml` on every pull request and on pushes to `main`: **api** (`dotnet build` with warnings as errors, then `dotnet test` with a Postgres 17 service, so the contract tests run against `tracegreen_test`), **ai** (`uv sync --locked`, pytest, `ruff check`, `ruff format --check`), **web** (`pnpm typecheck`, `lint`, `test`, `build`), **types drift** (API in memory mode, `pnpm gen:api`, `git diff --exit-code web/src/api/schema.d.ts`). Every action is pinned to a release commit. **e2e** (the demo image built and started, `pnpm e2e` against it on Postgres, then again with the API recreated in memory mode; container logs printed on failure) | the first four jobs green on GitHub since the A9 pull request (#25); the e2e job's steps run locally on 2026-11-05 and pass in both modes, its first GitHub run is its own pull request |
| `seed/` | Fixed-UUID demo data: 5 UBL invoices + adversarial file, factors (placeholders flagged `PLACEHOLDER`), lanes (illustrative), alternatives, categories (= AI allow-list), classification map, ai-cache, demo-state | validated by `SeedLoader` tests |
| Compose | `db`, `ai` (reload), `api` (`dotnet run`, migrates + seeds on boot), `web` (Vite); `compose.demo.yml` with one-shot `migrate`, runtime images and nginx | brought up and checked by hand |

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

To see both badge states: Reset demo → Load demo invoice → click Shenzhen Diecast Co. → Simulate → **Sea** shows `Win-win` (−15.1 t, −€3,910, +33 days); **Cluj Metal Works SRL** shows `Premium option` (€284 per tCO₂e). After a verification by air the badges are the same (deltas −13.2 t and −13.8 t).

Decisions behind it: `docs/DECISIONS.md`, "Simulator panel, slice 5 (B5)".

Checked on 2026-11-05 in headless Chromium (a throwaway Playwright script, not a committed spec) against Compose in Postgres mode with N1 ESTIMATED, and against a memory-mode API with N1 ESTIMATED and after an air answer: Air (current) and Road (no leg on `CN-RO`) disabled; Sea `win-win` and N1-alt `premium` in the panel and on the ghost; Apply → "Saved" on the panel and the ghost, 201 from the API with the `INSERT INTO scenarios` in the Postgres log; N1's status, carbon, cost and lead time unchanged; closing the simulator removes the ghost; no console errors. The Playwright smoke (B6) should assert the same `data-badge` values on `sim-result` and `ghost-<nodeId>`.

## Playwright smoke (B6)

`web/e2e/demo-path.spec.ts` is one scripted run of the demo path against the running Compose stack, with the dashboard in a desktop page and the supplier page in a phone-sized one (Pixel 7): reset and load the demo invoice (N1 ESTIMATED) → request a link and read `qr-url` → the phone answers Air + Grid → N1 is VERIFIED on the dashboard within 2 s of the submit → the same link opened again says "already used" → simulator: Sea `win-win`, N1-alt `premium` or `below-ets` on the panel and the ghost, Apply saved, N1 still VERIFIED → audit chain valid. Every check reads a `data-testid` or another `data-*` attribute, never copy. How to run it, the rate-limit note and the frozen hooks: `docs/RUNBOOK.md`, "Playwright smoke". The supplier page (C5) gained test hooks only (`verify-page` with `data-phase` and `data-result`, the answer buttons, the submit button); its behaviour is unchanged.

Checked on 2026-11-05 against Compose: passes in Postgres mode and in memory mode (`DATA_MODE=memory docker compose up -d api`), about 5–6 s per run, twice in a row in each mode. A third run within the same minute fails at step 3 with `rateLimited`, which is the 10-a-minute limit on `/api/verify*` doing its job.

## Demo tooling (B7)

- `scripts/demo-check.mjs` (Node, no dependencies; logic in `scripts/lib/demo-check.mjs`, tested): the URL (`PUBLIC_BASE_URL` from `.env.demo`, or the argument when it is left empty, RUNBOOK step 2) is set, not loopback, answers `/api/health`, is where the API builds QR links (with `PUBLIC_BASE_URL` set, `baseUrl.source` must be `configured`, which catches a stack not restarted after editing `.env.demo`), and `/api/events` sends its `hello` event. Checked on 2026-11-05: all five pass against the dev stack started with `PUBLIC_BASE_URL=http://<lan-ip>:3000`, and with it empty and the LAN URL as the argument (the check then says to open the dashboard at that address); health and the event stream pass through nginx on the demo image.
- `scripts/firewall.ps1`: one inbound rule for TCP 3000 from the local subnet, idempotent, `-Remove` to delete it. It parses and its help loads; it has not been run elevated on a machine yet.
- CSP: the header in `web/nginx.conf` is unchanged. The smoke passes against `compose.demo.yml` with no CSP violation on the dashboard or the phone page, and the header reaches the pages, the `/verify` route and the assets. The Linux firewall commands were already in `RUNBOOK.md`; ruling R6's fallback phone and the checks are now there too.

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
4. **Slice 7, Python AI service (C7):** after 1–3. Every seed line is served from `seed/ai-cache.json` by the C# side, so the live service is not on the demo path; keep `/health` honest and `/classify` on the stub until there is time (D4: live provider in hackathon hours H1–H4).
5. **Data (T4) and the tag (T22):** a person replaces the PLACEHOLDER factor rows and gives `ETS_PRICE_EUR_PER_T` a source and date (`docs/factor-sources.md`); then `node scripts/preflight.mjs` passes and `v0.9-prehack` can be tagged. If the real rows move a demo badge, report it here (D5).

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

- `.NET 10 SDK` on this machine is installed under `%LOCALAPPDATA%\Microsoft\dotnet` (user-local); put it first on `PATH` or use the containers.
- `dotnet-ef` global tool is 10.0.5 while the runtime is 10.0.13: it works but warns; `dotnet tool update -g dotnet-ef` removes the warning.
- The Postgres contract tests need the scratch database `tracegreen_test` and reset the demo workspace of whatever database `DATABASE_URL` names. Never point them at the dev database `tracegreen`: on 2026-10-29 a run inside the api container that way removed the imported N1 from the live graph. `RUNBOOK.md` now passes `-e DATABASE_URL=…/tracegreen_test` to `docker compose exec api dotnet test` (checked 2026-11-05: 266 passed, and the dev graph kept its nodes). If the dev database was hit, press "Reset demo".
- Health reports `ai: error …` when the API runs on the host without the AI service; inside Compose it reports the provider.
