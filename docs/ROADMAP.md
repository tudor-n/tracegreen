# Roadmap status

Status of TraceGreen by roadmap ID, as of 2026-11-06. The strategy roadmap itself is kept outside the repository. Only its IDs and the parts that describe the product live here (ruling R7 in `DECISIONS.md`). Detail and the order of remaining work: `NEXT.md`. Team tasks (`A#`, `B#`, `C#`): `docs/plan/`.

Legend: **done**; **built** (the code exists, but the milestone check that proves it has not been recorded); **partial**; **open**; **outside** (work that happens outside the repository, such as pitch, events and logistics).

## Milestones

| ID | Due | What | Status |
|---|---|---|---|
| M1 | 25 Oct | Import an XML from the browser, and an amber node appears with its KPIs | **done**: recorded 2026-10-24 (B3) and re-run 2026-10-29 (C4), in both data modes (`NEXT.md`, "M1 check") |
| M2 | 1 Nov | Two-device verify loop: the phone flips the node green in under 2 s, then the simulator runs | **open**: the API-level checks passed on the demo image in both modes on 2026-11-05 (`NEXT.md`, "M2 pre-check"); the timed two-device run (T16) and the simulator panel (B5) remain |

## Features

| ID | Feature | Status | Where |
|---|---|---|---|
| F1 | e-Factura import (UBL 2.1, BT-158 classification code and BT-159 origin country, unit codes) | **done** | `Domain/Ubl`, `POST /api/ingest/ubl`; the seed XMLs pass ANAF's validator (`NEXT.md`) |
| F2 | Supply-chain graph: status colours, hotspot ring, log-width edges, KPI bar | **done** | `web/src/store`, `components/graph`, `KpiBar`; KPIs from the API, labelled per ruling R2 |
| F3 | Supplier verification loop: signed link and QR, `/verify/:id`, live flip | **built** | API (A5, A6), node drawer and link card (B4), supplier page (C5, phone-tested), flip animation; the Supplier Terms ticked before a submit and recorded in the audit log, printable at `/terms/supplier` (2026-11-06, operator details still placeholders, `docs/legal/README.md`); M2's timed run is still open |
| F4 | What-if simulator: mode or supplier swap, deltas, MAC badge, saved scenario | **done** | Backend (A7: `POST /api/simulate`, `POST /api/scenarios`, `NodeView.alternatives`); the simulator panel and ghost node in the drawer (B5), checked in the browser in both data modes (`NEXT.md`) |
| F5 | Audit log panel with chain check | **done** | `AuditPanel`, `POST /api/audit/verify` |
| F6 | AI line-item classifier with the "AI-mapped" label | **built** | The C# side classifies from the invoice code (CPV, and HS since F6), then `seed/ai-cache.json`, then the AI client, then the stub, and the label is shown. The Python service answers `/classify` from the cache, then the models in `AI_MODELS` (two cloud models and a local `ministral-3:3b` through Ollama), then the stub, within 8 s, with the allow-list and a 0.6 confidence threshold enforced (C7, F6); the Romanian invoices R1–R3 exercise the AI and HS-rule paths. The local model is scored (`docs/ai-eval.md`: 94.3% in invoice-sized calls of 4 lines) and the AI demo steps are rehearsed with it; the cloud models' scores and the failover steps wait for API keys (`NEXT.md`). Since 2026-11-06 the side panel shows the AI's reason ("Why this category"), and the buyer can correct a category: the node is recomputed on the server, labelled "Buyer-corrected", and the correction is reused for the same line text on later imports |
| F7 | Plausibility check | **partial** | `AIR_HEAVY` on the answer and the node (D2); `SEA_TOO_FAST` is in the contract but not evaluated (D3) |
| F8 | Optional mass confirmation | **partial** | `POST /api/verify` accepts `massT` and a confirmed mass replaces the invoice's; the supplier page does not ask for it yet |
| F9 | PDF invoice drop | **open** | Hackathon, if time |
| F10 | Scenario export | **open** | Only if everything else is done |
| F11 | AI alternative suggester | **built** | `GET /api/nodes/{id}/suggestions` (`SuggestionService`) and the AI service's `/suggest` (`ai/app/suggest.py`): the AI picks among the node's curated alternatives from `seed/alternatives.json` only, under short ids, and never sends a figure; the simulator shows "AI-suggested · illustrative" and the AI's reason under the alternative it picked. A reason with a digit or a claim word (lower, closer, cheaper, carbon, …) is dropped on both sides. The seed has one alternative for `zinc_diecast` and one for `pcb`, so the AI can endorse at most one per node (`NEXT.md`) |
| F12 | Supplier page: one link per supplier, every delivery confirmed from one list | **built** | `PortalService`, `POST /api/portal-link`, `/api/portal/{view,answer}`, `/supplier`, the drawer's "Supplier page link"; the Supplier Terms ticked once per page visit and recorded with each answer; `web/e2e/supplier-portal.spec.ts` passes in both data modes, on the Vite dev stack and in CI on the demo image; not yet run on a real phone (`NEXT.md`) |

## Controls

| ID | Control | Status | Where |
|---|---|---|---|
| C1 | HMAC-signed link token | **done** | `HmacLinkSigner`; the layout is R5's, which differs from the roadmap's shorter form (`SECURITY.md`) |
| C2 | Token bound to the invoice hash; a mismatch is 410 | **done** | `VerifyService`, `Hashing.InvoiceHash` |
| C3 | Single use through a unique nonce; a second use is 409 | **done** | Both repositories; the context route answers 409 too |
| C4 | Hash-chained audit log with an on-screen check | **done** | `AuditChain`, append-only trigger, `AuditPanel` |
| C5 | Rate limit on `/api/verify`: 10 a minute per address | **done** | ASP.NET Core limiter, `TRUSTED_PROXIES`; the supplier page's routes count apart, at 3 × the limit (F12) |
| C6 | Untrusted text rendered as text; CSP header | **done** | `react/no-danger` lint; CSP in `web/nginx.conf` (demo image) |
| C7 | Supplier page without cookies, login or email | **done** | `/verify/:nodeId` and `/supplier` (F12) |
| C8 | RLS; secrets only on the server | **partial** | RLS is enabled with no policies yet (the app connects as owner); secrets are server-only; see "Known gaps" in `SECURITY.md` |
| C9 | 7-day expiry with a countdown on the QR card | **done** | `LINK_TTL_SECONDS` default 604800; "expires in …" on the link card |

## Tasks

| ID | Due | Task | Status |
|---|---|---|---|
| T1 | 19 Oct | Team registration and dates | **outside** |
| T2 | 20 Oct | Repository and CI | **done**: repo on the .NET / FastAPI / Vite stack; CI added in A9 (`.github/workflows/ci.yml`) |
| T3 | 21 Oct | Database, tables, RLS, offline mode | **done**: Postgres with migrations `Init` and `Rls`, plus `DATA_MODE=memory` |
| T4 | 22 Oct | Factor rows with source, year and row label, and a spend-factor source | **partial**: the spend source is decided (D1, US EPA v1.3) and `docs/factor-sources.md` exists. The freight and material rows and `ETS_PRICE_EUR_PER_T` are still placeholders; a person replaces them |
| T5 | 23 Oct | UBL parser and seed XMLs validated at ANAF | **done** (`NEXT.md`, "Seed XML validation") |
| T6 | 23 Oct | Carbon math with MAC guard tests | **done**: `Domain/Carbon` |
| T7 | 24 Oct | Graph, status colours, KPI bar | **done** (M1) |
| T8 | 25 Oct | Seed scenario loaded | **done** |
| T9 | 25 Oct | Name, tagline, logo, colour tokens | **partial**: name, favicon and status tokens are done; the tagline is a team decision |
| T10 | 27 Oct | Signed links and the QR card | **done** |
| T11 | 28 Oct | Mobile supplier page with RO/EN and the rate limit | **done**: phone-tested in C5 |
| T12 | 29 Oct | Live update to the node flip and KPIs; PARTIAL state | **built**: event stream, store and flip animation exist; the flip under 2 s over the hotspot is not measured yet (A8) |
| T13 | 30 Oct | Audit log and panel | **done** |
| T14 | 31 Oct | Simulator drawer with MAC badge states | **done**: the math and the endpoints (A7), the simulator panel with all four badge states and the ghost node (B5) |
| T15 | 1 Nov | Error, loading and empty states; CSP; environment hygiene | **done**: CSP and environment hygiene; the states on the demo path are checked by the Playwright smoke (B6, `web/e2e/demo-path.spec.ts`), which passes against Compose in both data modes |
| T16 | 1 Nov | First timed two-device run-through | **open** (A8) |
| T17–T21, T23 | 3–5 Nov | Field visits, pitch deck, backup video, rehearsals, pack list | **outside** |
| T22 | 5 Nov | Freeze scope and tag `v0.9-prehack` | **open**: `scripts/preflight.mjs` blocks the tag while any factor source says PLACEHOLDER or the ETS price has no source (T4) |
| T24 | H12 | Scope freeze during the hackathon | **outside** (a team rule) |
