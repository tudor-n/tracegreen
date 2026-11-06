# Priority changes — 2026-10-22

For coding agents continuing the TraceGreen plan. Read after `CLAUDE.md` and before `docs/NEXT.md`. Where this file and `NEXT.md` disagree on *order*, this file wins; where they disagree on *specification*, `NEXT.md` wins. Fold each item into `NEXT.md` / `docs/DECISIONS.md` once it is done, then delete this file.

Why this exists: a review on 2026-10-22 compared the strategy roadmap (now v1.2, outside the repo) with the code. The code is ahead of the roadmap on the backend, behind it on everything a juror can see, and diverges from it on five points that nobody had written down. Nothing in the repo has changed since the 2026-10-20 hand-off (8 commits, 126 .NET tests). Milestone M1 is **Sunday 2026-10-25**; roadmap task T4 was due today.

## 1. Order of work (changed)

Build vertical slices toward the demo path, not horizontal layers. The plan's Tasks 9–13 stay as specified in `NEXT.md`, but they are now executed in this order:

| # | Slice | Plan task(s) | Done when |
|---|---|---|---|
| 1 | **Ingest + graph (M1)** — `GET /api/graph`, `POST /api/ingest/ubl`, `POST /api/ingest/demo`, `POST /api/dev/reset`; SPA store, dagre layout, React Flow canvas, KPI bar, toolbar (import / load demo / reset), SSE hook with poll fallback | Task 9 (ingest/graph/reset parts only), Task 11 (canvas/KPI/toolbar/events only) | `seed/invoices/N1-shenzhen.xml` imported from the browser → amber node with hotspot ring in < 2 s, KPIs from the API, in both `DATA_MODE` values; `pnpm typecheck && pnpm lint` clean; recorded by a human |
| 2 | **ANAF validation script** — `scripts/validate-seed-anaf.mjs` against `https://webservicesp.anaf.ro/prod/FCTEL/rest/validare/FACT1` for every file in `seed/invoices/` | pulled forward from Task 13 | All six XMLs validate (or the script prints the exact ANAF message and the XML is fixed); result pasted into `docs/NEXT.md` |
| 3 | **Verify loop (M2 backend)** — request-link, verify context/submit, audit verify, rate limit, `BaseUrlResolver`, plausibility rules `SEA_TOO_FAST` / `AIR_HEAVY`, `POST /api/dev/simulate-answer` | rest of Task 9 | Replay → 409, expired or hash mismatch → 410, bad signature → 401, 11th call in a minute → 429; node event carries fresh `Kpis` |
| 4 | **Verify loop (M2 SPA)** — node drawer, QR card with printed URL and loopback warning, `/verify/:nodeId` page (two button groups, skip → PARTIAL, `?lang=` RO/EN), audit panel with chain check, connection badge | rest of Task 11 | Phone on the hotspot flips the node green in < 2 s |
| 5 | **Simulator** — `POST /api/simulate`, `POST /api/scenarios`, simulator panel with both MAC badge states | Task 9 + 11 remainder | N1 air → sea shows `win-win`; N1 → N1-alt shows `premium` or `below-ets` |
| 6 | **Playwright smoke** | Task 12 | `pnpm e2e` passes against Compose |
| 7 | **Python AI service** | Task 10 | Only after 1–6. Every seed line is served from `seed/ai-cache.json` by the C# side already, so the live service is not on the demo path (roadmap A4, R4). Keep `/health` honest and make `/classify` return the stub until there is time. |
| 8 | **Scripts, docs, CI** | Task 13 | `scripts/preflight.mjs` also implements §4 below |

Do not start a cloud deployment, a tunnel, or any new dependency before slice 5 is done.

## 2. Data work that is not an agent's to invent (T4, due today)

All 19 rows in `seed/factors.json` carry `source: "PLACEHOLDER …"`. `ETS_PRICE_EUR_PER_T=70` and the four grid rows are placeholders too. Agents **must not** fill these with numbers from memory. What agents do:

- Keep the placeholder text in `source` exactly as it is until a human replaces the row with a DESNZ 2026 ("Freighting goods", "Material use") or EEA/Ember/ANRE row that names the table, year and row label. The DESNZ electricity factor is never used for Romania (roadmap FB14, FV5).
- Create `docs/factor-sources.md` as a table skeleton (id, kind, key, value, unit, source, year, row label, WTT included?) pre-filled from `factors.json` so the human only edits values and sources.
- The UI "i" icon on every number shows `source`, `year`, `rowLabel` from `FactorLookup`; a placeholder row therefore shows the word PLACEHOLDER on screen. That is intended until T4 lands — do not hide it.
- `scripts/preflight.mjs` (slice 8) fails when any factor `source` contains `PLACEHOLDER` or when `ETS_PRICE_EUR_PER_T` is unset, so the `v0.9-prehack` tag (roadmap T22, 2026-11-05) cannot be cut on placeholder data.

## 3. Rulings that change code or copy

Each ruling follows the ledger format: decision — reason — cost if wrong. Override by editing the line and noting it in `docs/DECISIONS.md`.

1. **Activity formula.** Keep `Estimate.ActivityBasedKg` as implemented: `transport + material × (1 − renewable_share × 0.5)`. The roadmap's §6.1.2 version is inverted (material vanishes on grid power and rises with renewables); the roadmap will be corrected, the code will not be "fixed back". Pinned by `RenewablePowerLowersActivityCarbonVsGrid`. Cost if wrong: none.
2. **KPI wording on screen.** The API field stays `primaryShare` (matches the roadmap's `primary_share` and Q2). The UI label is **"Supplier-confirmed share"** with the subtitle "by emissions, not by node count" and a tooltip: "share of total kgCO₂e from nodes the supplier confirmed — activity data from official factors, not externally verified PCFs". Reason: AUMOVIO counts only verified PCFs as primary data and reported 0 % for 2025; a juror from there reads the KPI bar. Never render the bare phrase "primary data" in the UI. Cost if wrong: one string.
3. **Green-node provenance label.** Every VERIFIED node shows "Supplier-confirmed · <factor source> <year>" in the drawer; every ESTIMATED node shows "Spend-based estimate · <factor source>"; lane cost and lead time stay labelled "illustrative". This is roadmap Q18's "we record provenance and label what each green node is", made literal.
4. **"Don't know" energy.** `EnergySource.Unknown` keeps producing VERIFIED (both questions were answered) and is computed like grid. Add a derived `Uncertainty` label per roadmap rule M4: `High` for ESTIMATED and PARTIAL, `Medium` for VERIFIED with `Unknown` energy or `MassUnknown`, `Low` otherwise. Shown as a label, never as ±%. Cost if wrong: one enum.
5. **Token layout.** The implemented layout `v1.<nodeId>.<expiresUnix>.<nonce>.<invoiceHash>.<sig>` and the status mapping 401 / 409 / 410 / 429 are final. `docs/SECURITY.md` (Task 13) documents this layout, not the roadmap's shorter one; the roadmap and backup slide 8 will be aligned to the code.
6. **Demo hosting.** The laptop-hosted Compose stack over the laptop's hotspot (`RUNBOOK.md`) is the primary and only path before M2. Consequence to document in `RUNBOOK.md` and in the QR card's loopback warning: only phones on that hotspot can open a link; a juror's own phone cannot. The second phone (A) joins the hotspot as the fallback. Cost if wrong: a tunnel added after M2.
7. **Roadmap copy in the repo.** `TRACEGREEN_ROADMAP ala bun.md` is v1.1 and git-ignored; the current strategy document is v1.2 (2026-10-20) and lives outside the repo. Feature, control, task, question and risk IDs are identical in both versions, so citing `F1–F11`, `C1–C9`, `T#`, `Q#`, `R#` from the repo copy is safe; citing company facts from it is not. Roadmap §19 (the CLAUDE.md starter) is superseded by the repo's `CLAUDE.md`; `SEED_MODE` is only an alias for `DATA_MODE=memory`.
8. **Health endpoint.** `NEXT.md` describes `ai` and `baseUrl` fields on `GET /api/health`; they do not exist yet. Add them in slice 1 so `CLAUDE.md`'s description becomes true.

## 4. Copy rules for anything a juror can read

`CLAUDE.md` invariants still apply (AI labels, "illustrative" on lane data, no `dangerouslySetInnerHTML`, no PII on `/verify/*`). In addition, in UI strings, seed data, README text, demo copy and OpenAPI descriptions:

- Never "primary data" without the definition from ruling 2; never "verified PCF"; never "blockchain" (say "tamper-evident audit log"); never "first" or "only".
- Never a real company name (the seed stays fictional: Transilvania Electro-Assembly SRL and its five suppliers). In particular never "Continental", "Marquardt", "AUMOVIO", "Bosch", "Schaeffler", "TimeLine", "SmartBill" anywhere in code, seed or generated docs — those belong to the pitch, and the pitch has its own banned-claims list.
- Do not hand-tune demo numbers to make a badge appear (roadmap §9). With the current placeholders N1 air → sea is `win-win` and N1 → N1-alt is `premium`; if real factors change that, report it in `NEXT.md` rather than adjusting lanes or masses.

## 5. Roadmap timeline mapped to the code (for status reporting)

| Roadmap item | Due | State on 2026-10-22 |
|---|---|---|
| T2 repo, CI | 20 Oct | Repo done on the .NET / FastAPI / Vite stack; CI is Task 13 |
| T3 database, tables, RLS, offline mode | 21 Oct | Done: Postgres + EF Core migrations `Init` and `Rls`, `DATA_MODE=memory` |
| T4 factors and spend-factor source | **22 Oct** | Open — see §2 |
| T5 parser, 2 XMLs validated at ANAF | 23 Oct | Parser done (BT-158, BT-159, unit codes, DTD rejected, 2 MB cap); ANAF validation open — slice 2 |
| T6 carbon math + MAC guard tests | 23 Oct | Done in `TraceGreen.Domain/Carbon` |
| T7 graph, status colours, KPI bar | 24 Oct | Open — slice 1 (`web/src/pages/Dashboard.tsx` is a placeholder) |
| T8 seed scenario loaded | 25 Oct | Data done and seeded on boot; nothing renders it — slice 1 |
| T9 name, tagline, logo, tokens | 25 Oct | Name, favicon and status tokens done; tagline is a human decision |
| **M1** import XML → amber node | **25 Oct** | Needs slice 1 |
| T10, T13, T14 (links, audit log, simulator math) | 27–31 Oct | Domain code exists ahead of schedule; endpoints and UI are slices 3–5 |
| T22 tag `v0.9-prehack` | 5 Nov | Blocked by §2 preflight until placeholders are gone |

## 6. Documentation drift to fix while passing by

- `CLAUDE.md` "Map" lists `Domain/Services/`, plausibility rules and `web/src/{api,store,hooks,components}` as existing; they are targets. Either create them in slice 1/3 or mark them "(planned)" — do not leave the map describing files that are not there.
- `RUNBOOK.md` and `CLAUDE.md` assume Windows (user-local .NET SDK path, `scripts/firewall.ps1`, Windows Mobile Hotspot IP, polling watchers). A working copy now also lives on Linux. When touching these files, add the Linux equivalents as documented commands (no `.sh` scripts, per `CLAUDE.md`), and keep every script a Node `.mjs`.
- `NEXT.md` test count (125) and the ledger (126) differ by the Postgres contract run; report counts as "N in memory mode, M with `DATABASE_URL`".
- `docs/field-notes.md`, `docs/run-log.md`, `docs/after.md`, `docs/vs-annex.md` are human artefacts from the roadmap; agents do not create them.

## 7. What does not change

Demo-critical path F1 → F2 → F3 → F4; pinned versions, no upgrades before 2026-11-08; every feature works in both `DATA_MODE` values; carbon numbers exist only in C#; the AI never computes carbon and the import never blocks on it; append-only hash-chained `audit_log`; the seed scenario of roadmap §9 with fixed UUIDs; commit subjects that reference IDs (`F1: …`, `C3: …`), no attribution trailers; scope freeze at hackathon hour 12.
