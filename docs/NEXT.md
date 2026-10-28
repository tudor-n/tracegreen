# Status and next steps

Last updated: 2026-10-27 (slice 3, the verify loop's backend, tasks A5–A6 of `docs/plan/dev-a.md`; before that, 2026-10-24, the M1 integration run in both data modes, task B3 of `docs/plan/dev-b.md`; 2026-10-23, slice 2, the seed invoices validated at ANAF, task C2 of `docs/plan/dev-c.md`; and 2026-10-22, the slice-1 backend and client transport, tasks A1–A3 of `docs/plan/dev-a.md`). Order of work: `docs/PRIORITY-CHANGES.md` (private) and `docs/plan/`.

## What exists

Monorepo with three services behind one browser origin, brought up by `docker compose build && docker compose up` (see `RUNBOOK.md`).

| Area | State | Tests |
|---|---|---|
| `api/` .NET 10 host | Config from env (fails fast), OpenAPI at `/openapi/v1.json`, `GET /api/health` (mode, db, seed nodes, `ai`, `baseUrl`), `GET /api/graph`, `POST /api/dev/reset`, `POST /api/ingest/{ubl,demo}`, `GET /api/events` (Server-Sent Events with `id:`/`Last-Event-ID` replay, `resync`, `ping`), `POST /api/request-link`, `GET /api/verify/context`, `POST /api/verify` (rate limited), `POST /api/audit/verify`, `POST /api/dev/simulate-answer`, startup flags `--migrate --seed --reset --exit` | integration tests via `TestWebAppFactory` (memory mode) |
| `api/` Domain (pure) | Model records; carbon math (`Carbon/`: factor table with provenance, spend and activity estimates, KPIs, MAC + badge, simulator); UBL 2.1 parser (namespace-agnostic, DTD rejected, 2 MB cap); canonical JSON + hashes; HMAC link tokens (`v1.` layout, hash-bound, constant-time); hash-chained audit log; `IRepository`, `IEventBus`, `IAiClient` ports; `LineClassification` (invoice code → cache → AI with timeout → stub); `DemoState` builder | unit tests, hand-computed expectations |
| `api/` Infrastructure | `MemoryRepository` (lock-atomic); `EfRepository` on Postgres (advisory lock per workspace, unique nonce, unique `(workspace_id, prev_hash)`, TRUNCATE-based reset); migrations `Init` + `Rls` (row-level security + append-only trigger on `audit_log`); `InMemoryEventBus` (ring buffer 500); `HttpAiClient` / `StubAiClient` | repository contract runs against memory always and against Postgres when `DATABASE_URL` is set |
| `ai/` FastAPI | `GET /health` only; uv project with pinned `fastapi`, `pydantic`, `anthropic` (installed, unwired) | pytest + ruff clean |
| `web/` Vite SPA | Dashboard at `/`: Zustand graph store (`hydrate`, `applyEvent`, `select`) with memoised React Flow selectors and dagre layout (B1); supplier nodes (status tone, hotspot ring, "i" factor tooltip, "AI-mapped" label), the KPI bar from API values and the toolbar (Import, Load demo invoice, Reset demo) (B2), kept live by `useGraphEvents` (A3). `/verify/:nodeId` is still a placeholder. Tailwind v4, shadcn (button, card, badge, sheet, select, tooltip, separator), Vite proxy `/api` → API with `X-Forwarded-Host`, oxlint with `react/no-danger` as error | vitest (store, selectors, formatting, sync, toolbar requests), typecheck, lint, build |
| `seed/` | Fixed-UUID demo data: 5 UBL invoices + adversarial file, factors (placeholders flagged `PLACEHOLDER`), lanes (illustrative), alternatives, categories (= AI allow-list), classification map, ai-cache, demo-state | validated by `SeedLoader` tests |
| Compose | `db`, `ai` (reload), `api` (`dotnet run`, migrates + seeds on boot), `web` (Vite); `compose.demo.yml` with one-shot `migrate`, runtime images and nginx | brought up and checked by hand |

Test counts at hand-off: 125 .NET tests (109 before Task 8 plus the event bus, AI client, classification and SSE tests), 1 Python test, web typecheck/lint/build.

Test counts after A3: 30 web unit tests (`pnpm test`: the sync reducer and the driver with a fake EventSource and fake timers); 193 .NET tests; in memory mode 180 run and the 13 Postgres contract tests are skipped, with `DATABASE_URL` all 193 run.

Test counts after C2: 197 .NET tests; in memory mode 184 run and the 13 Postgres contract tests are skipped, with `DATABASE_URL` all 197 run.

Test counts at M1 (B3): 206 .NET tests, all run by `docker compose exec api dotnet test` (the container has `DATABASE_URL`); 78 web unit tests.

Test counts after A6: 255 .NET tests; 241 in memory mode (the 14 Postgres contract tests skipped), 255 with `DATABASE_URL`; 78 web unit tests.

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

Decisions behind it (fold into `docs/DECISIONS.md` when it exists):

- **Services live in `TraceGreen.Domain/Services`** and take a Domain-side `ServiceOptions` record that `Program.cs` builds from `AppConfig` — reason: the Domain keeps no project or package reference and the services are testable without a web host — cost if wrong: moving a few classes.
- **`NodeView` is the one node shape on the wire**, in the snapshot and in every node event (`TraceGreen.Domain/Model/ReadModel.cs`, built by `NodeViewMapper`). It carries `carbonKg` with its `basis` (`spend` or `activity`), the `spend` and `activity` figures each with the `FactorLookup` rows behind them (the "i" icon; a PLACEHOLDER source reaches the screen), the ruling-4 `uncertainty`, `classification` (source, confidence, `aiMapped` for the "AI-mapped" label, the dominant line's description), an `invoice` summary and the `lane` (current and default mode, available `modes`, distance, `illustrative: true`). Ruling-3 provenance line = `basis` + that figure's `factors[].source` and `year`. PARTIAL stays on the spend figure because `carbonKg` switches to activity only when VERIFIED.
- **Event types reach the generated client by describing `/api/events`** as `text/event-stream` of `DomainEvent`, a union discriminated by `type` (`node.created`, `node.updated`, `node.verified`, `audit.appended`, `scenario.saved`, `graph.reset`). The discriminator is written first and equals the SSE event name. `hello`, `resync` and `ping` are control messages without domain data. `node.verified` carries a `VerificationView` without nonce or address hash.
- **One JSON shape for responses and SSE** (`ApiJson`): camelCase, enums as camelCase strings (error codes and node status keep their own spelling), numbers never as strings, constructor parameters required and nullability as annotated, so the generated types have no spurious optional fields.
- **The snapshot reads `lastSeq` before the nodes**, so an event published while it is built is newer than the snapshot and is applied on top; the store ignores events with `seq <= lastSeq`. An event newer than the snapshot can still repeat what the snapshot already shows (a `node.created` for a listed node, an `audit.appended` for a listed entry), so the store applies events idempotently: upsert nodes by id, skip audit entries it already has.
- **Enum components hold only their values.** A schema transformer removes the `null` .NET folds into an enum component when it first meets the enum as a nullable property; nullable properties say so themselves (`oneOf [null, $ref]`). So `Record<EnergySource, string>` typechecks.
- **Optional request fields need a default.** `ApiJson` respects required constructor parameters for requests too: a positional request record field that may be omitted must be declared `= null`, or the framework answers 400 without an `ApiProblem` code (matters for A5's skip → PARTIAL).
- **An event that cannot be serialised becomes `resync`** on the stream (the client refetches the snapshot) instead of breaking the stream on every replay.
- **Factor rows on a node come from today's `factors.json`;** stored kilograms are not recomputed. After changing `factors.json`, reset the demo so every node is rebuilt from the new rows; until then older nodes show the new rows, and a row that no longer exists is left out of the list rather than failing `/api/graph`. Persisting the rows on the node would remove this caveat if it ever matters.
- **Mass unknown means no activity figure.** A node whose mass is unknown keeps `activity: null`, so it stays on the spend figure; a VERIFIED one shows `uncertainty: medium` (pinned by `NodeViewMapperTests`).
- **`baseUrl` is a display hint, not a trusted value:** for forwarded headers the last value wins (an appending proxy adds its own after the client's), but a direct caller can still choose them. Never write it into audit payloads.
- **Errors are `ApiProblem`** (RFC 9457 fields plus a typed `code`); clients switch on `code`, never on titles.
- **Client transport (A3).** `web/src/api/client.ts` is the `openapi-fetch` client typed by `schema.d.ts` (same origin) and re-exports aliases of generated types (`NodeView`, `DomainEvent`, …). `web/src/hooks/useGraphEvents.ts` runs `startGraphSync` (`web/src/hooks/startGraphSync.ts`) for the component's lifetime, driving the store through `{ hydrate(snapshot), applyEvent(event) }`, and returns `connecting | live | polling | offline` for the badge (`connecting` only until the first snapshot; `offline` while snapshot requests fail, e.g. the API is down). The rules live in the pure reducer `web/src/hooks/graphSync.ts`: fetch the snapshot on every stream (re)open, on `resync`, on `graph.reset` (not forwarded to the store) and on a sequence gap; every new request drops the event buffer, because a snapshot requested after an event already contains it; events arriving while a request is in flight are applied afterwards if newer than its `lastSeq`; a `hello` or `resync` during an in-flight request queues one more; stale `seq` is dropped; the first stream error checks the API at once, then `/api/graph` is polled every 5 s while the stream is down; a request hanging past 8 s counts as failed; a new `EventSource` is opened when the browser gives up (a proxy 502). After an API restart the sequence starts again at 0 and the snapshot's lower `lastSeq` wins. The store seam (hydrate overwrites `lastSeq`, events are applied idempotently, no `graph.reset`) is written into `docs/plan/dev-b.md` B1.
- **Spend factors are US EPA v1.3 (D1, decided 2026-10-23), converted on every import.** `SpendConversion` turns the EUR total into 2022 USD with the ECB rate of the invoice date and CPI-U annual averages from `seed/spend-conversion.json`; the spend figure's `factors` are the EPA row, the rate and the price adjustment. Each category has its own spend row. The demo badges do not move: the simulator starts from verified N1's activity figure, and from the spend estimate N1 → N1-alt is still `premium` (about 284 €/t). Details and the refresh caveat: `docs/factor-sources.md`.
- **Non-EUR invoices are rejected with `UnsupportedCurrency`**: the conversion table holds USD/EUR only, and **a supplier country without a lane is rejected with `NoLane`** instead of inventing a lane (CN → `CN-RO`, TW → `TW-RO`, DE → `DE-HH-SB`, RO → `RO-SB-SB`).
- **Ingest refuses input it cannot estimate safely, with 422 `Schema` and a detail:** elements nested deeper than 64 levels (checked by a streaming pass before any tree is built), a total that is not positive or above 10¹² EUR, a line amount above 10¹², a negative quantity (returns are not supported yet) or one above 10⁹. The bounds keep every later sum inside `decimal`'s range, so one hostile invoice cannot make `/api/graph` fail for everyone.

## Wire contract (slice 3)

| Route | Success | Errors |
|---|---|---|
| `POST /api/request-link {nodeId}` | 201 `RequestLinkResponse { nodeId, url, expiresAt, loopback }`; the token exists only inside `url` (`<baseUrl>/verify/<nodeId>?t=<token>`); appends `link.requested` | `NodeNotFound` → 404 |
| `GET /api/verify/context?t=` | 200 `VerifyContext`: supplier label, buyer name, invoice number and date, the dominant line's description (invoice text: render as text), lane origin and destination, `modes` the lane has with `defaultMode`, `energySources` with `defaultEnergy: grid`, `massT`, `expiresAt`. Nothing personal, no nonce, no address | `InvalidToken` → 401; `AlreadyUsed` → 409; `LinkExpired`, `InvoiceChanged` → 410; `RateLimited` → 429 |
| `POST /api/verify {t, transportMode?, energySource?, massT?}` | 200 `VerifyOutcome { nodeId, status, warnings }`: both answered → VERIFIED, one skipped (null or left out) → PARTIAL; then `node.verified` (fresh `NodeView` and `Kpis`) and `audit.appended` | as above, plus `InvalidAnswer` → 422 (nothing answered, a mode the lane lacks, a mass not in (0, 10⁶] t) |
| `POST /api/audit/verify` | 200 `ChainVerification { valid, length, brokenAt }` (1-based position of the first bad entry) | — |
| `POST /api/dev/simulate-answer {nodeId, transportMode?, energySource?, massT?}` | 200 `VerifyOutcome`, through the same link and verify path (same audit rows and events) | 404 unless `DEMO_TOOLS=1`; `NodeNotFound` → 404; `InvalidAnswer` → 422 |

`NodeView` gained `warnings: PlausibilityWarning[]` (`{ code: AIR_HEAVY | SEA_TOO_FAST, detail }`), derived on every read from the confirmed mode and mass, so the snapshot and every node event carry the dashboard marker.

Decisions behind it (fold into `docs/DECISIONS.md` when it exists):

- **D2 and D3 took the plan's proposed defaults** (nobody recorded an outcome by 25 Oct) — `AIR_HEAVY` (confirmed air, more than 2 t) is returned with the answer and on the node, and never blocks or asks for an extra tap; `SEA_TOO_FAST` is in the contract and in `web/src/copy/verify.ts` but is not evaluated, because no invoice carries delivery dates — cost if wrong: one rule and its input (BT-72 extraction, which changes `ExtractedInvoice` and so every invoice hash; reset the demo after it).
- **A used link answers 409 on the context route too**, so scanning it again shows "already used" before any tap. A submit also checks for a used nonce before it validates the answers. The repository's unique nonce stays the guarantee: five concurrent submits of one link give one 200 and four 409.
- **Check order follows the signer:** format → signature (401) → expiry (410 `LinkExpired`) → invoice hash recomputed from the stored extract (410 `InvoiceChanged`) → single use (409). The node id is read from the token first, unauthenticated, only to load that hash. A genuine link for a node a reset removed is 410 `InvoiceChanged`; a forged token naming an unknown node still fails its signature (401).
- **A verification rebuilds the node through `NodeFactory`** with the answers, so the activity figure, cost and lead time follow the confirmed mode (N1 confirmed by sea: 40 days, sea freight cost) and a confirmed `massT` replaces the invoice mass. `NodePatch` gained `costEur` and `leadDays` for this. A VERIFIED node can be answered again with a new link; each answer is a new verification row.
- **Audit payloads:** `link.requested {nodeId, expiresAt}` and `verification.submitted {nodeId, verificationId, status, transportMode, energySource, massTConfirmed, warnings}`, as strings. No token, no nonce, no address. The verification row stores `Hashing.IpHash` keyed with a key derived from `LINK_SECRET` (`"ip-hash:" + secret`), never the address.
- **Rate limit:** ASP.NET Core's limiter (shared framework, no package), fixed one-minute window of `RATE_LIMIT_PER_MIN` per client address on the context and submit routes; the 11th call is 429 `RateLimited`. The address is `X-Forwarded-For` only when the peer is in `TRUSTED_PROXIES` (new variable, default `127.0.0.0/8,::1/128,172.16.0.0/12`: Vite on the host, Vite or nginx in Docker), with `ForwardLimit = 1`, so a phone on the hotspot cannot choose its bucket. Only the address is taken from forwarded headers; `BaseUrlResolver` still reads the host headers itself.
- **Tokens stay out of logs:** `Microsoft.AspNetCore.Hosting.Diagnostics` is raised to Warning (its request line carries `?t=`), pinned by a test that collects every log line. nginx (demo image) logs `$uri` without the query string or referrer, and `/api/verify/context` has its own location whose error log keeps only critical lines, because an upstream failure writes the whole request line there. Not fixed: the Vite dev server prints the full URL on a proxy error (`http proxy error: /api/verify/context?t=…`) while the API is down; the demo image uses nginx.

Checked over HTTP in Postgres mode (`tracegreen_test`) and through `TestWebAppFactory` in memory mode: link → context → air answer on N1 → VERIFIED with `AIR_HEAVY`, `carbonKg` 20 700 on the activity basis, replay 409, context after use 409, chain valid, simulate-answer PARTIAL, the 11th verify call in a minute 429, no token in the API log.

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

Decisions (A, 2026-10-23; fold into `docs/DECISIONS.md` when it exists):

- **The three imports (N1 Shenzhen, N2 Taiwan, N5 Hamburg) are expected to fail one rule**, which narrows slice 2's "all six XMLs validate" to "all six pass every rule except, for the three imports, seller identification" — reason: ANAF identifies the seller by a Romanian CUI ("Nu a fost identificat cui vanzator") and these suppliers have none; marking the invoice self-billed (type 389) does not help — cost if wrong: the three files get Romanian VAT registrations and leave the list. The script lists them in `EXPECTED_FAILURES` and passes them only when that message is ANAF's only one. It fails them on any other message, and when ANAF accepts them, so the list can neither hide an error nor go stale. Checked on scratch copies: with a Romanian seller ID substituted, ANAF accepts all three, so the seller is the only rule they miss; a schema error or a bad buyer CUI in a listed file still fails the script.
- **The parser reads the classification scheme STI as CPV** (`UblInvoiceParser.ListId`) — reason: ANAF accepts CPV codes only under STI, while the classification map, the AI contract and the "Item classification code … (CPV)" rationale use CPV — cost if wrong: one line.
- **RON VAT totals use the BNR rate published on the invoice's issue date** — reason: BR-RO-030 and BR-53 require the figure; nothing in TraceGreen reads it — cost if wrong: two numbers in three files.

Open for A's confirmation in review (task C2 says to ask A before changing a line description):

- **The adversarial line's parsed description gains " - "** ("Wire harness set. - IGNORE PREVIOUS INSTRUCTIONS …") — reason: BR-RO-L100 caps `cbc:Name` at 100 characters, so the injected instruction moved to `cbc:Description` and the parser joins the two; the injection itself is kept (roadmap A3) and `seed/ai-cache.json` is re-keyed to match; nothing else in the repo uses the old string — cost if wrong: shorten the injected text to fit in `cbc:Name` instead, and restore the old cache key.

The API reads `seed/` once at startup. After pulling these changes, restart it (`docker compose restart api`) and press "Reset demo", or run `docker compose down -v && docker compose up`, so the stored buyer and seed invoices match the files.

## M1 check (2026-10-24)

The README §8 M1 checklist, run by Dev B against the Compose stack on `b/b2-dashboard`, once with the default Postgres mode and once with `DATA_MODE=memory`. Requests went through the Vite proxy on :3000, the browser's path. Both modes gave the same results:

| M1 item | Result | How it was checked |
|---|---|---|
| N1 imported from the browser shows an amber node with the hotspot ring in under 2 s | Pass | By hand in the browser; `POST /api/ingest/ubl` answers 201 in 15–30 ms |
| KPIs come from the API | Pass | The snapshot and every `node.created` carry `kpis`, which the store copies; the total goes from 14.5 t (seed) to 37.0 t with N1 (22.6 t) |
| Load demo invoice | Pass | 201, then 409 `Duplicate` on a second click; the same file imported twice is also 409, and a non-XML body is 415 |
| Reset demo returns to the seed state | Pass | `/api/graph` after `POST /api/dev/reset`: N2 verified, N3 and N4 estimated, N5 partial, no N1; the stream sends `graph.reset` |
| `GET /api/health` returns `ai` and `baseUrl` | Pass | `ai` is `unconfigured` (the AI service's own provider name until C7); `baseUrl` is `request` on :8080 and `forwarded` through :3000 |
| Two windows stay in sync; the API stopped and started again | Pass | By hand: both windows show each import and reset, and recover without a reload |
| API restarted in memory mode | Pass | `lastSeq` falls from 2 to 0 and N1 is gone; the store accepts the lower `lastSeq` on hydrate |
| `pnpm typecheck && pnpm lint`, `dotnet test`, `pnpm test` | Pass | 206 .NET tests (in the API container, against Postgres) and 78 web tests |
| A person records the screen | Open | C4 |

## What remains (in order)

The implementation plan lives outside the repo (Claude plan file `read-the-tracegreen-roadmap-ala-validated-thunder.md`); the summary below is enough to continue without it.

1. **Use-case services and endpoints (plan Task 9).** The slice-1 part is done (A1, A2): graph, ingest, demo import, reset and the health fields, checked over HTTP in both data modes. The SPA transport (A3) is done and unit-tested, and it passed the two-window and stop-the-API checks in the M1 run (see "M1 check (2026-10-24)"). The slice-3 backend (A5, A6) is done: link, verify context and submit, audit verify, rate limit, `AIR_HEAVY`, simulate-answer. Next for the API: the slice-5 contract (A7, simulate and scenarios). The original list: `Domain/Services/{Ingest,Graph,Link,Verify,Audit,Scenario,Demo}Service.cs` constructor-injecting `IRepository`, `IEventBus`, `ILinkSigner`, `IAiClient`, `TimeProvider`, `SeedBundle`, `AppConfig`. Endpoints: `GET /api/graph`, `POST /api/ingest/ubl` (multipart `file` or `text/xml`), `POST /api/ingest/demo` (ingests `seed/invoices/N1-shenzhen.xml` reusing N1's fixed ids from `demo-state.json`), `POST /api/request-link`, `GET /api/verify/context?t=`, `POST /api/verify`, `POST /api/audit/verify`, `POST /api/simulate`, `POST /api/scenarios`, `POST /api/dev/reset`, `POST /api/dev/simulate-answer` (dev endpoints 404 unless `DEMO_TOOLS=1`). Rate limiting on `/api/verify*` (10/min per client IP → 429). `BaseUrlResolver` (`PUBLIC_BASE_URL` → `X-Forwarded-Proto/Host` → request origin; flags loopback). Plausibility rules `SEA_TOO_FAST`, `AIR_HEAVY`. Node events must carry fresh `Kpis` (the SPA never computes carbon). Status mapping: replay 409, expired/hash mismatch 410, bad signature 401. Add `ai` and `baseUrl` to `/api/health`.
2. **Python AI service (Task 10).** `app/schemas.py` (contract mirrored by `HttpAiClient`: `POST /classify`, `POST /suggest`, `POST /extract-pdf` → 501, `GET /health`), `app/cache.py` (`cache_key` = sha256 of trimmed, lower-cased, whitespace-collapsed description — identical to `Domain/Ai/CacheKey.cs`), providers `cached → anthropic (only if AI_MODEL and ANTHROPIC_API_KEY) → stub`, prompt that delimits invoice lines as data, output validated against the allow-list from `seed/categories.json`. Tests: cache hit, stub fallback, no client construction without a model, allow-list enforcement, every seed line hits the cache.
3. **Web SPA (Task 11).** The slice-1 part is done (B1, B2) and passed M1 in both data modes; next is the slice-4 dashboard (B4) once the slice-3 contract (A5) lands. The original list: `pnpm gen:api` (types from `/openapi/v1.json`, committed), `openapi-fetch` client, Zustand store (`hydrate`, `applyEvent` ignoring stale `seq`), selectors (`toRfNodes`, `toRfEdges` with log-width edges, `hotspotIds`), dagre layout, `useGraphEvents` (EventSource; refetch on `resync`/reopen; poll every 5 s while not live), dashboard components (toolbar with import / load demo / reset, React Flow canvas with `data-testid="node-<id>"` and `data-status`, KPI bar, node drawer, QR card with printed URL and loopback warning, simulator panel calling `POST /api/simulate`, audit panel with chain check, connection badge), verify page (two button groups, skip → PARTIAL, RO/EN via `?lang=`, result states).
4. **Playwright smoke (Task 12).** `web/e2e/demo-path.spec.ts` against the Compose stack: reset → load demo invoice → request link → phone context submits → node flips → replay shows "already used" → simulator badges → chain valid.
5. **Scripts, docs, CI (Task 13).** `scripts/{preflight,demo-check,validate-seed-anaf}.mjs`, `scripts/firewall.ps1`; `docs/{ROADMAP,ARCHITECTURE,SECURITY,DECISIONS,factor-sources}.md`; final `CLAUDE.md`; `.github/workflows/ci.yml` (api with Postgres service, ai, web, compose + e2e + generated-types drift check).

## Decisions taken so far (the ones you would not guess from the code)

- Backend C#/.NET 10, AI service Python/FastAPI, frontend Vite + React SPA; carbon math exists only in C# and the simulator is a stateless endpoint.
- Postgres in Docker via EF Core, not a local Supabase stack; migrations are plain SQL-compatible and include RLS so hosted Supabase stays an option.
- The activity-based formula is `transport + material × (1 − renewable_share × process_electricity_share)` (the source document's version inverted the renewable effect; it is pinned by `RenewablePowerLowersActivityCarbonVsGrid`).
- Verification tokens carry the full invoice hash so a tampered token (401) and a changed invoice (410) are distinguishable; single use is enforced by the repository's unique nonce, not the signer.
- The audit hash covers `{ts, actor, action, payload}` with timestamps normalised to UTC milliseconds; `audit_log` has an append-only trigger, so the demo reset uses TRUNCATE (single-tenant for now).
- `dotnet watch` is not used in Docker (its polling watcher crashes on Windows bind mounts); the API container runs `dotnet run` and C# edits need `docker compose restart api`. Web and AI services hot-reload.
- A `workspace_id` column and a `Ctx(WorkspaceId, Actor)` argument exist from day one so authentication later is a policy, not a refactor.
- Seed factor values are placeholders (`source` says so); replace them with sourced rows and update `docs/factor-sources.md` when it exists. The seed XMLs pass ANAF's validator, three of them with one expected exception (see "Seed XML validation (2026-10-23)").

## Caveats to know before continuing

- Duplicates are refused by the repository itself, under the same lock as the insert (raw XML hash, invoice id or node id already stored), so two identical uploads or two "Load demo" clicks at the same instant give one 201 and one 409. Ingest also checks before classifying, only to spare the AI wait.
- Seed nodes are stamped `2026-11-01T08:00Z` (`DemoState.SeedTime`), so a node imported before that date sorts before them in `GET /api/graph`. The layout must not depend on node order.
- KPIs and events are published after the insert, outside any lock. Two imports at the same instant can publish in reverse order, so the last `node.created` may carry KPIs without the other node until the next event or snapshot; a reset landing between an insert and its publish can deliver a `node.created` for a node the reset removed (the next snapshot drops it). Both need two actions within milliseconds; serialise publishing if it ever shows.

- `.NET 10 SDK` on this machine is installed under `%LOCALAPPDATA%\Microsoft\dotnet` (user-local); put it first on `PATH` or use the containers.
- `dotnet-ef` global tool is 10.0.5 while the runtime is 10.0.13: it works but warns; `dotnet tool update -g dotnet-ef` removes the warning.
- The Postgres contract tests need the scratch database `tracegreen_test` (see `RUNBOOK.md`).
- Health reports `ai: error …` when the API runs on the host without the AI service; inside Compose it reports the provider.
