# Dev A: API, wire contract and live updates

These are your tasks in order. Each one lists the outcome, the files, what "done" means, and a prompt to start the Claude Code session with. Read [README.md](README.md) first for dates, seams, git rules and the shared Claude Code loop.

Rules that hold across your track:

- Endpoints stay thin: parse → service → status (`CLAUDE.md`). Carbon numbers come only from `TraceGreen.Domain/Carbon`.
- Ruling 1: the activity formula stays as implemented, pinned by `RenewablePowerLowersActivityCarbonVsGrid`.
- Every feature works in memory and Postgres mode. When you touch persistence, also run the repository contract with `DATABASE_URL` against the `tracegreen_test` database (`RUNBOOK.md`).
- The snapshot and the events put the same node shape on the wire.
- Tokens never reach logs, audit payloads or exception messages.

## A0: Get the repo ready for three people (Thu 22 Oct, 30 min)

- Keep one copy of the priority file, `docs/PRIORITY-CHANGES.md`, and delete the untracked duplicate at the repo root (identical content). Don't commit it: it names companies (§4) and mentions a juror, and this repo is public. Add it to `.gitignore` next to the roadmap file, and send it to B and C privately. Every brief refers to it by that path.
- In `CLAUDE.md`:
  - Next to the pointer to `docs/NEXT.md`, add a line that imports the priority file: the at-sign followed by `docs/PRIORITY-CHANGES.md`, written as plain text. Claude Code ignores imports inside backticks, and the rest of `CLAUDE.md` puts paths in backticks. The import loads the file into every session on machines that have it. Remove the line when the file is folded and deleted in A10.
  - Add the line `Team plan and briefs: docs/plan/`.
  - `PRIORITY-CHANGES.md` §6 asks for the Map entries that don't exist yet to be marked "(planned)": `Domain/Services/`, the plausibility rules, and `web/src/{api,store,hooks}`. Remove each marker when its code lands.
- Commit `docs/plan/` and push to `main`.
- On GitHub, protect `main`: require a pull request with one approval, and block force-pushes and deletion. From then on, every change reaches `main` through a PR.
- On Linux, install the .NET 10 SDK on the host (version in `api/global.json`) so that worktree sessions can run `dotnet test`.

Done when: these changes are pushed and B and C have pulled.

## A1: Slice-1 contract (Fri 23 Oct, merged by 13:00)

Land everything B and C will type against as a single contract PR.

- **Where the services live.** `NEXT.md` lists `AppConfig` as a service dependency, but `AppConfig` sits in `TraceGreen.Api` and `TraceGreen.Domain` takes no project or package references. Either give the services a Domain-side options record that `Program.cs` builds from `AppConfig`, or keep the orchestration in the Api project. Pick one and write the reason in the PR.
- **The node read model**, so the SPA can render a node without computing anything:
  - `carbonKg` and `status`;
  - the ruling-4 `uncertainty` label: `High` for ESTIMATED and PARTIAL, `Medium` for VERIFIED with `unknown` energy or unknown mass, `Low` otherwise;
  - the parts of the ruling-3 provenance line: kind, factor source, year and row label;
  - the `FactorLookup` inputs behind each number, for the "i" icon. A PLACEHOLDER source must reach the screen (`PRIORITY-CHANGES.md` §2);
  - the classification source and confidence, for the "AI-mapped" label;
  - the lane's illustrative flags.
- **`GET /api/graph`** returns the buyer, nodes, KPIs, audit entries, `lastSeq` and mode. `primaryShare` keeps its name (ruling 2).
- **`POST /api/ingest/ubl`** (multipart `file` or a `text/xml` body) and **`POST /api/ingest/demo`** return the created node and fresh KPIs. Parser errors come back as ProblemDetails with a stable `code`: `TooLarge` → 413; `DoctypeForbidden` and `NotUblInvoice` → 400; `Schema` → 422. A duplicate import (same raw hash): proposed 409 carrying the existing node id.
- **`POST /api/dev/reset`** returns 204, publishes `graph.reset`, and returns 404 unless `DEMO_TOOLS=1`.
- **`GET /api/health`** adds `ai` (the provider name or the error, behind a short timeout so health never hangs) and `baseUrl` with a loopback flag (ruling 8).
- **Event payload types.** `/api/events` is excluded from the OpenAPI description, so today the SPA would have to hand-type the event records. Put them in the generated contract instead, for example by registering the payload records as OpenAPI components with a schema or document transformer. The events should carry the same node read model as the snapshot.
- Where the logic is not trivial, endpoints return 501 until A2.
- Run the API, then `cd web && pnpm gen:api`, and commit `web/src/api/schema.d.ts` in the same PR.

Done when: `dotnet test` passes; `/openapi/v1.json` shows typed responses for every new route and for the event payloads; `schema.d.ts` is committed; and B and C have been told.

Kickoff prompt (plan mode):

> Read docs/plan/dev-a.md task A1, docs/PRIORITY-CHANGES.md (slice 1, rulings 2–4 and 8), docs/NEXT.md task 9, and api/src/TraceGreen.Domain (Model, Ports, Carbon, Seed). Propose the slice-1 wire contract: records, routes, status codes, where the services live and how they get configuration, and how the SSE event payload types end up in web/src/api/schema.d.ts. List the trade-offs you see. Don't write code yet.

## A2: Slice-1 implementation (Fri–Sat 23–24 Oct)

- **`IngestService`**, in this order:
  1. Parse.
  2. Check for a duplicate (`GetInvoiceByRawHashAsync`).
  3. Classify the lines with `LineClassification.ClassifyLinesAsync` under the configured timeout. The cache is consulted before the AI, and the import never waits longer than the timeout.
  4. Take the dominant line and its category.
  5. Compute the spend estimate and the mass from the unit codes.
  6. Resolve the lane from the supplier's country: CN → `CN-RO`, TW → `TW-RO`, DE → `DE-HH-SB`, RO → `RO-SB-SB`. That last mapping was decided during the scaffold; N3's `RO-OR-SB` is a pin in the demo state only. Decide what an unknown country does, and note it in the PR. `ingest/demo` reuses N1's fixed node id, invoice id and lane from `seed/demo-state.json`.
  7. Call `InsertInvoiceWithNodeAsync` with an `invoice.imported` audit entry.
  8. Publish `node.created` with fresh KPIs, then `audit.appended`.
- **`GraphService`** builds the snapshot and maps nodes to the read model. **`DemoService`** handles the demo import and the reset (`SeedAsync(reset: true)`, then `graph.reset`).
- **`BaseUrlResolver`** tries `PUBLIC_BASE_URL`, then `X-Forwarded-Proto` and `X-Forwarded-Host`, then the request origin, and flags loopback addresses. Health uses it now; request-link uses it in A6.
- **Integration tests** through `TestWebAppFactory`:
  - the snapshot holds the four seeded nodes;
  - importing N1 returns 201 with an ESTIMATED node, the total kg rises, and `node.created` reaches the bus with KPIs;
  - a duplicate returns 409, a DOCTYPE 400, an oversize file 413;
  - the dev routes return 404 without `DEMO_TOOLS`;
  - health has the new fields.

  Then walk the same flow by hand in Compose: once in the default Postgres mode, once with `DATA_MODE=memory`.

Done when: the backend items of slice 1 in `PRIORITY-CHANGES.md` pass in both modes.

Kickoff prompt:

> Implement the slice-1 services and endpoints from the merged contract (docs/plan/dev-a.md task A2). Work test-first through TestWebAppFactory and run `dotnet test` after each service. Keep endpoints thin. Stop and ask me if the contract needs to change.

To go faster, run a second session in a worktree (`claude -w a2-tests`) that writes the integration tests while the main session implements the services. The two meet at the merged contract.

## A3: Client transport in the SPA (Sat 24 Oct)

- **`web/src/api/client.ts`**: an `openapi-fetch` client typed by `schema.d.ts`, on the same origin (`/api` is proxied).
- **`web/src/hooks/useGraphEvents.ts`**, which drives B's store:
  - It opens `EventSource('/api/events')`.
  - On the first `hello`, it fetches `/api/graph` and calls `store.hydrate`.
  - On domain events, it calls `store.applyEvent`.
  - On `resync`, or when the stream reopens after an error, it fetches the snapshot again.
  - While the stream is not live, it polls `/api/graph` every 5 s.
  - It exposes `live`, `polling` or `offline` for B's connection badge.
- Keep the decision logic in a pure function and test reconnect, resync and stale `seq` there. Vitest runs in the `node` environment and only picks up `src/**/*.test.ts`.

Done when: two browser windows stay in sync through an import and a reset, and stopping the API switches the hook to polling, then restarting it recovers without a page reload.

Kickoff prompt:

> Read docs/plan/dev-a.md task A3, api/src/TraceGreen.Api/Endpoints/Events.cs and the store interface in web/src/store. Build web/src/api/client.ts and web/src/hooks/useGraphEvents.ts, with the reconnect logic in a pure function that has unit tests. Run pnpm typecheck, pnpm lint and pnpm test.

## A4: M1 integration (Sun 25 Oct)

With B and C, run the M1 checklist in README §8 in both modes and fix what breaks. C records the screen. Bring a slice-3 contract outline and a proposal for D2 and D3 to the Sunday demo.

## A5: Slice-3 contract (Mon 26 Oct, merged by 13:00)

- **`POST /api/request-link {nodeId}`** returns `{ url, expiresAt, loopback }`. The token exists only inside `url`.
- **`GET /api/verify/context?t=`** returns what the phone page needs and nothing personal: the supplier label, the item description, the lane's available modes with the default pre-selected, energy pre-selected as `grid`, and the expiry. Decide whether a used token returns 409 here too. It makes "scan again → already used" show up immediately.
- **`POST /api/verify {t, transportMode?, energySource?, massT?}`** returns the resulting status (VERIFIED, or PARTIAL when one answer is skipped) and any plausibility warnings. Errors:
  - 401 for a malformed token or a bad signature;
  - 410 for an expired token or a changed invoice, each with its own `code`;
  - 409 for a replay;
  - 429 for the rate limit;
  - 422 for a mode the lane doesn't have.
- **`POST /api/audit/verify`** returns `{ valid, length, brokenAt }`.
- **`POST /api/dev/simulate-answer`** works only with `DEMO_TOOLS=1`. It goes through the same service path as a real answer, so the audit rows and events are identical.
- Warnings have the shape `{ code: "SEA_TOO_FAST" | "AIR_HEAVY", … }` and behave as D2 and D3 decide.
- Regenerate `schema.d.ts`.

Kickoff prompt (plan mode):

> Read docs/plan/dev-a.md task A5, docs/PRIORITY-CHANGES.md slice 3 and ruling 5, docs/NEXT.md task 9, and Domain/Security, Domain/Audit and Ports/IRepository. Propose the slice-3 contract and the status mapping, including exactly what the phone page receives. Point out anything that could leak a token or personal data. No code yet.

## A6: Slice-3 implementation (Mon–Wed 26–28 Oct)

- **`LinkService`** mints a nonce, signs `LinkClaims(nodeId, invoiceHash, nonce, now + LINK_TTL_SECONDS)`, builds the URL with `BaseUrlResolver`, and appends a `link.requested` audit entry without the token.
- **`VerifyService`**:
  1. Look up the node with `FindNodeAsync` (the anonymous path).
  2. Load the invoice and recompute its hash from the stored extract (C2).
  3. Verify the token with `ILinkSigner.Verify` and map the result to a status code.
  4. Derive the new status with `Estimate.DeriveStatus` and compute `ActivityBasedKg`.
  5. Call `RecordVerificationAsync`. The unique nonce turns a replay into 409.
  6. Publish `node.verified` with fresh KPIs, then `audit.appended`.

  Store only `Hashing.IpHash`, never the IP.
- **Rate limit** `/api/verify*` to `RATE_LIMIT_PER_MIN` calls per client IP with ASP.NET Core's built-in rate limiter (part of the shared framework, so no new package). Behind the Vite proxy and nginx, the client IP arrives in `X-Forwarded-For`, so trust forwarded headers only from the proxy.
- **Plausibility rules**, as pure Domain code with unit tests:
  - `AIR_HEAVY` (air and more than 2 t) fires on N1 in the demo, which is 2.3 t. Follow D2.
  - `SEA_TOO_FAST` needs dispatch and delivery dates, which the parser does not extract and no seed invoice carries. Follow D3. If you add BT-72 (`cac:Delivery/cbc:ActualDeliveryDate`), remember that `ExtractedInvoice` feeds the invoice hash: reset the demo data after the change, and re-run C's ANAF script on any XML you touch.
- **Tests**: every status code (use `FakeTimeProvider` from `Microsoft.Extensions.TimeProvider.Testing`, already pinned, for expiry), replay, the 11th call → 429, KPIs on `node.verified`, and a valid audit chain after a verification. Run `/security-review` before asking for review.

Done when: the slice-3 "done when" in `PRIORITY-CHANGES.md` holds in both modes.

Kickoff prompt:

> Implement slice 3 from the merged contract (docs/plan/dev-a.md task A6). One service at a time, test-first, `dotnet test` after each. Show me the rate limiter and forwarded-headers configuration before wiring it in.

## A7: Slice-5 backend (Thu–Fri 29–30 Oct)

- **Contract PR first (Thu, 13:00):**
  - `POST /api/simulate` takes `{nodeId, mode}` or `{nodeId, alternativeId}` and returns the `ScenarioResult`: values, deltas, MAC, badge, inputs and `illustrative`.
  - `POST /api/scenarios` returns the saved scenario and publishes `scenario.saved`.
  - Decide how the SPA gets the alternatives that fit a node (alternatives match on category).
  - Errors: 422 for a mode the lane lacks, 404 for an unknown node or alternative.
- **Implementation:** resolve the lane, category and factors, then call `Simulator.ModeChange` or `Simulator.SupplierSwap` with `ETS_PRICE_EUR_PER_T`. The base node is never touched.

Done when: through the HTTP API and in both modes, N1 air → sea returns `win-win`, and N1 → N1-alt returns `premium` (with today's placeholders) or `below-ets`.

Kickoff prompt:

> Read docs/plan/dev-a.md task A7 and Domain/Carbon/Simulator.cs. In plan mode, propose the slice-5 contract; once I approve, implement it test-first. The carbon math already exists, so the service only resolves inputs.

## A8: M2 (Sat 31 Oct – Sun 1 Nov)

Integrate with B and C, run the first timed two-device run-through (T16), and fix what lands in `docs/run-log.md`. Over the hotspot, measure the time from import to node and from submit to flip.

## A9: CI (Sat 31 Oct – Tue 3 Nov)

Write `.github/workflows/ci.yml` with these jobs:

- **api**: a Postgres service, with the contract run pointed at a scratch database through `DATABASE_URL`;
- **ai**: `uv run pytest` and ruff;
- **web**: `pnpm typecheck`, `pnpm lint`, `pnpm test`, `pnpm build`;
- **types drift**: start the API, run `pnpm gen:api`, then `git diff --exit-code web/src/api/schema.d.ts`;
- **e2e**: Compose plus `pnpm e2e`, once B6 has landed.

Pin every action version.

## A10: Docs and hand-over (Mon 2 – Thu 5 Nov)

- `docs/ARCHITECTURE.md`: services, ports, adapters, the event flow, the read model and both data modes.
- `docs/SECURITY.md`:
  - the token layout and status mapping exactly as implemented (ruling 5);
  - rate limiting and forwarded headers;
  - RLS and the audit chain;
  - the CSP from B7;
  - what is logged and what never is.
- `docs/DECISIONS.md`: the ledger in the "decision — reason — cost if wrong" format, covering rulings 1–8, the outcomes of D1–D6, and the contract decisions from A1, A5 and A7.
- `docs/ROADMAP.md`: status by feature, control and task ID that is safe to keep in the repo (ruling 7: IDs yes, company facts no).
- `CLAUDE.md`: a Map that matches the tree, Linux commands next to the Windows ones, and any new dependency noted. In `docs/NEXT.md`, report test counts as "N in memory mode, M with `DATABASE_URL`".
- Fold `docs/PRIORITY-CHANGES.md` into `NEXT.md` and `DECISIONS.md`, then delete it, its `.gitignore` entry and its import line in `CLAUDE.md`. Those two files are public, so fold the rulings without company names or reasons that mention jurors.

## Reviews you do

You review B's PRs and C's PRs that touch `api/`, `seed/`, `scripts/` or `ai/`. For C7, check that the JSON matches `HttpAiClient` field by field and that invoice text is only ever passed as delimited data.
