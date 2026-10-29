# TraceGreen team plan: 22 Oct to 7 Nov 2026

This plan covers the work in `docs/PRIORITY-CHANGES.md`, split across three developers who each work with Claude Code. Each developer has a brief with their tasks in order, what "done" means for each one, and prompts to start each task with:

- [dev-a.md](dev-a.md): the API, the wire contract and live updates
- [dev-b.md](dev-b.md): the dashboard and the demo tooling
- [dev-c.md](dev-c.md): the supplier page, data quality and the copy a juror reads

Read order, for people and for Claude Code sessions: `CLAUDE.md` → `docs/PRIORITY-CHANGES.md` → `docs/NEXT.md` → this file → your brief. When this plan disagrees with `PRIORITY-CHANGES.md` on order, or with `NEXT.md` on specification, those files win, and this plan gets fixed.

`docs/PRIORITY-CHANGES.md` is shared privately and git-ignored: it names companies and mentions a juror, and this repository is public. Everything committed, including this plan, follows the copy rules in its §4.

## 1. Dates

| Date | What | Gate |
|---|---|---|
| Thu 22 Oct | T4 (factor sources) due | `docs/factor-sources.md` skeleton merged; spend-factor source being chosen (D1) |
| Fri 23 Oct | T5: seed XMLs validated at ANAF | Slice 2 merged |
| **Sun 25 Oct** | **M1** | Slice 1 "done when" met in both `DATA_MODE` values and recorded by a person |
| **Sun 1 Nov** | **M2** | Slices 3–5 done; first timed two-device run-through (T16) written up in `docs/run-log.md` |
| Mon–Tue 2–3 Nov | Tech Show (all three attend, so less coding time) | — |
| Wed 4 Nov | Startup Hub (evening) | — |
| **Thu 5 Nov** | **M3** | Playwright smoke green, `scripts/preflight.mjs` green, tag `v0.9-prehack`, backup video, three rehearsals |
| Fri–Sat 6–7 Nov | Hackathon | Scope freeze at H12 |
| Sun 8 Nov | Version pins may move again | — |

The schedule assumes the roadmap's capacity of 12–15 productive hours per person per week around classes (roadmap §10), with more on weekends. Claude Code does most of the typing. People do the design, the review and the checks.

## 2. Tracks

| | Dev A | Dev B | Dev C |
|---|---|---|---|
| Area | API, wire contract, live updates | Dashboard, demo tooling | Supplier page, data quality, juror-facing copy |
| Day 0 | A0 repo ready for three people | B0 shared Claude Code config, Linux bring-up | C0 setup, C1 factor-sources skeleton |
| Slice 1 (M1) | A1 contract · A2 ingest/graph/demo/reset, health `ai` + `baseUrl` · A3 generated client and SSE hook · A4 integration | B1 store, selectors, layout · B2 canvas, KPI bar, toolbar · B3 integration | C3 copy module · C4 M1 acceptance run and recording |
| Slice 2 | review | — | C2 ANAF validation script, seed fixes |
| Slice 3 | A5 contract · A6 link, verify, audit, rate limit, plausibility, simulate-answer | — | — |
| Slice 4 | review | B4 drawer, QR card, audit panel, connection badge, flip | C5 `/verify/:nodeId` page |
| Slice 5 | A7 simulate and scenarios endpoints | B5 simulator panel | — |
| Slice 6 | — | B6 Playwright smoke | — |
| Slice 7 | review of prompt handling | — | C7 AI service: contract, cache, stub |
| Slice 8 | A9 CI · A10 ARCHITECTURE, SECURITY, DECISIONS, ROADMAP, CLAUDE.md map | B7 demo-check, firewall and Linux runbook, CSP | C6 preflight script · C1 real factor rows with the team |
| Reviews | B's PRs; C's PRs under `api/`, `seed/`, `scripts/`, `ai/` | A's PRs; C's PRs under `web/` | Juror-facing copy on every PR that changes it |

Priority follows `PRIORITY-CHANGES.md` §1. With three people the slices overlap, but nobody starts a lower slice while a higher one needs their hands, and the owner of a late higher slice can pull anyone off a lower one. Two rules from that file stay absolute: no cloud deployment, tunnel or new dependency before slice 5 is done, and the AI service (slice 7) starts after slices 1–6.

## 3. How the tracks connect

There are three seams. Each has one person who changes it and tells the others when it changes.

1. **HTTP contract (Dev A).** For slices 1, 3 and 5, A first opens a small contract PR: request and response records, routes, status codes and `Produces<>` metadata, with endpoints returning 501 until they are implemented. The same PR runs `pnpm gen:api` and commits `web/src/api/schema.d.ts`. From then on, B and C build against the generated types, and nobody hand-writes API types. A change after merge needs a new contract PR and a message in the team chat. The author of a contract PR settles questions about it. Anything that changes another track's work goes to the daily sync.
2. **Store interface (Dev B).** `hydrate(snapshot)` and `applyEvent(event)` live in `web/src/store/`. A's `useGraphEvents` hook calls them. The store ignores events whose `seq` is not newer than its `lastSeq` and applies the rest idempotently; `hydrate` replaces `lastSeq` even when it goes down (the sequence restarts with the API); `graph.reset` never reaches the store, because the hook refetches the snapshot.
3. **Copy (Dev C).** `web/src/copy/` holds every label a juror reads: rulings 2–4, the AI labels, the MAC badge names and the RO/EN strings for `/verify`. B and C import from it instead of writing strings inline.

| When | From → to | Handoff |
|---|---|---|
| Fri 23 Oct, 13:00 | A → B, C | Slice-1 contract merged, `schema.d.ts` committed |
| Fri 23 Oct | B → A | Store interface (`hydrate`, `applyEvent`) merged |
| Sat 24 Oct | C → B | `web/src/copy/labels.ts` merged. If it is late, B inlines the ruling-2 strings and switches when it lands. |
| Sun 25 Oct | all | D2 and D3 decided before the slice-3 contract |
| Mon 26 Oct, 13:00 | A → B, C | Slice-3 contract merged |
| Thu 29 Oct, 13:00 | A → B | Slice-5 contract merged |
| Sat 31 Oct | B → A | `data-testid` names frozen for Playwright and CI |

## 4. Schedule

| Day | Dev A | Dev B | Dev C |
|---|---|---|---|
| Thu 22 Oct | A0; A1 design in plan mode | B0 | C0; C1 skeleton; D1 research starts |
| Fri 23 Oct | A1 merged by 13:00; A2 starts | B1 (store interface merged first) | C2 ANAF script and seed fixes (T5) |
| Sat 24 Oct | A2 services and tests; A3 | B2 | C2 finished; C3 |
| **Sun 25 Oct (M1)** | A4 integration in both modes | B3 integration in both modes | C4 acceptance run and recording; C1 research |
| Mon 26 Oct | A5 merged by 13:00; A6 starts | B4 node drawer | C5 page and states against a fixture |
| Tue 27 Oct | A6 request-link, verify context and submit | B4 QR card | C5 states, RO/EN, helper tests |
| Wed 28 Oct | A6 rate limit, plausibility, audit verify, simulate-answer; `/security-review` | B4 audit panel, connection badge | C5 real endpoints; first phone test |
| Thu 29 Oct | A7 contract by 13:00, then implementation | B4 flip animation; slice-4 PRs merged | C5 phone over the hotspot; PRs merged |
| Fri 30 Oct | A7 done; helps B5 integrate | B5 simulator panel | C6 preflight; C1 factor rows |
| Sat 31 Oct | A9 CI (api, ai, web, types drift) | B5 done; test ids frozen | C6 merged; C1 factor PR (D5 check) |
| **Sun 1 Nov (M2)** | A8 T16 run-through, fixes | T16; B6 starts | T16 on the phone; writes `docs/run-log.md` |
| Mon 2 Nov (Tech Show) | Run-log fixes; A10 | B6 Playwright | Run-log fixes on `/verify`; C1 leftovers |
| Tue 3 Nov (Tech Show) | A9 e2e job; A10 | B7 demo-check, firewall, runbook | C7 AI service (once B6 is green) |
| Wed 4 Nov (Startup Hub) | A10; fold `PRIORITY-CHANGES.md` | B7 CSP; demo image run | C7 done |
| **Thu 5 Nov (M3)** | All: preflight green → tag `v0.9-prehack`, backup video, three rehearsals | | |

## 5. Git and pull requests

- `main` is protected (A0), so every change reaches it through a PR with one approval. Branch per task off `main`, named `<dev>/<task>-<slug>`: `a/a1-graph-contract`, `b/b1-store`, `c/c2-anaf`.
- Keep PRs small and single-purpose: a contract PR, an implementation PR, a UI PR. Regenerated `schema.d.ts` goes in the contract PR that caused it.
- The PR title becomes the commit subject, so it references IDs (`F1: graph snapshot and ingest contract`, `C3: single-use token check`). Squash-merge. No attribution trailers (`CLAUDE.md`); the shared settings turn Claude Code's off.
- Before asking for review, run the checks for what you touched (the table in `CLAUDE.md`): `dotnet test`; `uv run pytest` plus ruff; `pnpm typecheck && pnpm lint && pnpm test`. Every feature is checked in both `DATA_MODE` values. Once slice 6 has landed, run `pnpm e2e` against Compose after touching services, events, carbon math or the store.
- Never hand-merge `schema.d.ts`. Rebase, start the API from the rebased tree, and run `pnpm gen:api` again.
- Rebase on `main` at least once a day, and run the checks again after rebasing.
- No new dependency before slice 5 is done. After that, a new dependency needs team agreement, an exact pin and a note in `CLAUDE.md`.
- Never commit `.env` or `.env.*.local`. Tokens never appear in logs, audit payloads, PR descriptions or screenshots.

## 6. Working with Claude Code

### 6.1 Team setup (B0 on Thu 22 Oct, then everyone pulls)

- `.claude/settings.json` (committed) turns commit and PR attribution off, allows the check commands without prompts, denies reading `.env` and force-pushing, and runs a copy-guard hook that blocks banned terms (`PRIORITY-CHANGES.md` §4) before they reach a file.
- `CLAUDE.md` imports the private `docs/PRIORITY-CHANGES.md`, so every session on a machine that has the file knows the slice order and the rulings (A0).
- Personal preferences go in `.claude/settings.local.json`, which is git-ignored.

### 6.2 The loop for every task

1. Create the branch, then start a named session in it: `claude -n c2-anaf`.
2. Plan first. Switch to plan mode with Shift+Tab, or start with `claude --permission-mode plan`, and point the session at the task: *"Read docs/plan/dev-c.md task C2 and docs/PRIORITY-CHANGES.md slice 2. Propose a plan; don't edit files."*
3. Read the plan, correct it in plain words, and approve it.
4. Let it implement in small steps and run the real checks itself (the commands in `CLAUDE.md`). Press Esc to stop it when it drifts. `/rewind` (or Esc twice on an empty prompt) restores Claude's own file edits and the conversation, but not changes made by shell commands, so commit each time the checks pass.
5. Read the diff yourself (`git diff` or the IDE diff view). For UI changes, run the app and look at it.
6. Commit with an ID subject, run `gh pr create`, and post the link in the team chat.
7. Run `/clear` before the next task. To come back to a session later, use `claude --continue`, `claude --resume`, or `claude --from-pr <number>` for a session tied to a PR.

### 6.3 Parallel sessions and Compose

- `claude -w <name>` starts a session in its own git worktree under `.claude/worktrees/<name>`, on a branch named `worktree-<name>`, so two sessions never edit the same files. Use it for work you can check with host tools: `dotnet test`, `pnpm test`, `uv run pytest`.
- A worktree branches from `origin/main` by default. To branch from your current task branch instead, set `"worktree": { "baseRef": "head" }` in your `.claude/settings.local.json`. Rename the branch with `git branch -m` before pushing it. A new worktree has no `web/node_modules`, so run `pnpm install` there once (pnpm reuses its store, so it's quick). The private `docs/PRIORITY-CHANGES.md` is git-ignored, so `.worktreeinclude` copies it into each new worktree; without it, `CLAUDE.md`'s import finds nothing and the session runs without the slice order and rulings. Reopen a worktree you kept with `claude --worktree <name> --resume`.
- Run one Compose stack per machine. `compose.yml` fixes the project name (`tracegreen`) and the ports 3000, 8080 and 5432, so run Compose from your main checkout and switch that checkout to whichever branch you want to see in the browser.
- On Linux, install the .NET 10 SDK on the host so worktree sessions can run `dotnet test`. The container only sees the main checkout.

### 6.4 What a session must not decide

Stop and ask the team when a session wants to:

- put a number into `seed/factors.json`, a lane, a mass or `ETS_PRICE_EUR_PER_T` that a person did not give it from a source (`PRIORITY-CHANGES.md` §2 and §4);
- change a test expectation so that a demo badge comes out a particular way;
- add or upgrade a dependency;
- change a merged contract, the token layout or status mapping (ruling 5), or how the audit chain works;
- write a banned term, or the phrase "primary data" without ruling 2's definition;
- change the activity formula back to the roadmap version (ruling 1);
- create `docs/field-notes.md`, `docs/run-log.md`, `docs/after.md` or `docs/vs-annex.md`, which people write themselves.

### 6.5 Reviews

The reviewer reads the diff and runs the branch. As a first pass the reviewer can run `/code-review <PR number>`, and should run `/security-review` on the checked-out branch of every slice-3 PR. As `CLAUDE.md` says, authors check their own work with the real commands, not with subagents.

## 7. Decisions for people

Record each outcome in `docs/DECISIONS.md` (A10 creates it; until then, use the PR description).

| ID | Decision | Proposed default | Who | By |
|---|---|---|---|---|
| D1 | Spend-factor source: US EPA v1.3 with FX conversion, Defra, or EXIOBASE (roadmap §6.1.1) | — | C with A and B | Fri 23 Oct |
| D2 | `AIR_HEAVY` (air and more than 2 t) fires during the live demo verification, because N1 is 2.3 t by air | Show the warning without blocking: no extra tap on the phone, and a marker on the dashboard node | A, C | Sun 25 Oct |
| D3 | `SEA_TOO_FAST` has no input data: the parser extracts no delivery date and no seed invoice has `cac:Delivery` | Ship `AIR_HEAVY` now. Add BT-72 extraction only if there is time, after slice 2 | A | Sun 25 Oct |
| D4 | Build the live AI classifier before 5 Nov, or during hackathon hours H1–H4 (roadmap F6, §10.4, R5) | Contract, cache and stub before 5 Nov; live provider at H1–H4 | all | Sun 1 Nov |
| D5 | Real factors change the demo badges: N1 air → sea is no longer win-win, or N1 → N1-alt is neither premium nor below-ETS | Report it in `NEXT.md` and change what the demo script says, never the data (§4) | all | When T4 lands |
| D6 | The four `grid.*` rows: no formula reads them today, and preflight fails on any PLACEHOLDER | Source them from EEA, Ember or ANRE, or delete them | C | Sat 31 Oct |

## 8. Milestone checklists

**M1, Sun 25 Oct**

- [x] `seed/invoices/N1-shenzhen.xml`, imported from the browser, shows an amber node with the hotspot ring in under 2 s.
- [x] KPIs come from the API (snapshot and event payloads). The browser computes none of them.
- [x] The same result with `DATA_MODE=postgres` (the Compose default) and `DATA_MODE=memory`.
- [x] "Load demo invoice" and "Reset demo" work. Reset returns to the seed state: N2 green, N3 and N4 amber, N5 partial, no N1.
- [x] `GET /api/health` returns `ai` and `baseUrl`.
- [x] `pnpm typecheck && pnpm lint` is clean; `dotnet test` and `pnpm test` pass.
- [x] A person records the screen.

**M2, Sun 1 Nov**

- [ ] Integration tests cover the status mapping: replay 409, expired or hash mismatch 410, bad signature 401, the 11th verify call in a minute 429.
- [ ] A phone on the hotspot flips the node green in under 2 s, and the KPIs update from the event.
- [ ] Opening the same link again shows "already used".
- [ ] The audit panel shows the chain as valid.
- [ ] Simulator: N1 air → sea shows win-win; N1 → N1-alt shows premium or below-ETS.
- [ ] All of the above in both `DATA_MODE` values.
- [ ] The T16 run-through is timed, and every failure is in `docs/run-log.md`.

**M3, Thu 5 Nov**

- [ ] `pnpm e2e` passes against Compose, and CI is green on `main`.
- [ ] `node scripts/preflight.mjs` passes: no PLACEHOLDER rows, and the ETS price is set.
- [ ] `docs/PRIORITY-CHANGES.md` is folded into `NEXT.md` and `DECISIONS.md`, then deleted.
- [ ] Tag `v0.9-prehack` exists, the backup video is recorded, and the pitch has been rehearsed three times.

## 9. If we fall behind

Cut from the bottom of each list, never from the top (roadmap §0, rule 4).

- M1 at risk: ship polling only (every 5 s), add SSE right after M1, and skip the entrance animation.
- Slice 4 at risk: English only on `/verify` (Romanian after M2), and a plain expiry date instead of a countdown.
- Slice 5 at risk: a table of deltas instead of the ghost node.
- After M2: slice 7 shrinks to an honest `/health` and the stub; scripts come before docs; F8–F11 stay out.
- Anyone blocked for more than half a day says so in the team chat.

## 10. Rhythm

- A daily 10-minute sync at a fixed time (the roadmap suggests 21:00) covers what merged, what is blocked, contract changes and decisions coming due.
- Every Sunday, a 30-minute demo to each other (roadmap §11).
- During the hackathon, the hours follow roadmap §10.4. In the H1–H4 slots: C builds the live classifier if D4 placed it there, with A reviewing the prompt handling; B polishes the entrance and flip animations, the KPI count-up and the hotspot ring; A is on call for the verify loop and shows plausibility warnings on the dashboard. Scope freezes at H12.
