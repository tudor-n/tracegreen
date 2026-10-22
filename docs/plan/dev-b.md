# Dev B: dashboard and demo tooling

These are your tasks in order. Each one lists the outcome, the files, what "done" means, and a prompt to start the Claude Code session with. Read [README.md](README.md) first for dates, seams, git rules and the shared Claude Code loop.

Rules that hold across your track:

- The SPA renders. It never computes carbon or KPIs (`CLAUDE.md`). Formatting (tonnes with one decimal, euros without decimals, days as integers) is presentation, and lives in selectors and helpers.
- React Flow state rules (roadmap §8.4): Zustand owns the domain nodes; React Flow gets derived, memoised arrays; React Flow node objects are never written back into the store.
- Labels come from `web/src/copy/` (C3). `dangerouslySetInnerHTML` is a lint error; render invoice text as text.
- Every component that shows data has a loading state, an empty state and an error state (roadmap §18.3).

## B0: Shared Claude Code setup and Linux bring-up (Thu 22 Oct)

1. **`.claude/settings.json`**, committed, applies to every session in the repo:

   ```json
   {
     "attribution": { "commit": "", "pr": "", "sessionUrl": false },
     "permissions": {
       "allow": [
         "Bash(dotnet build *)",
         "Bash(dotnet test)",
         "Bash(dotnet test *)",
         "Bash(uv run pytest)",
         "Bash(uv run pytest *)",
         "Bash(uv run ruff *)",
         "Bash(pnpm typecheck)",
         "Bash(pnpm lint)",
         "Bash(pnpm test)",
         "Bash(pnpm test *)",
         "Bash(pnpm e2e)",
         "Bash(pnpm e2e *)",
         "Bash(pnpm gen:api)",
         "Bash(docker compose ps)",
         "Bash(docker compose logs *)",
         "Bash(docker compose exec api dotnet test *)",
         "Bash(docker compose exec ai uv run *)",
         "Bash(docker compose exec web pnpm *)",
         "Bash(node scripts/*)"
       ],
       "deny": [
         "Read(./.env)",
         "Read(./.env.*.local)",
         "Bash(git push --force *)",
         "Bash(git push -f *)"
       ]
     },
     "hooks": {
       "PreToolUse": [
         {
           "matcher": "Edit|Write",
           "hooks": [
             {
               "type": "command",
               "command": "node",
               "args": ["${CLAUDE_PROJECT_DIR}/.claude/hooks/copy-guard.mjs"]
             }
           ]
         }
       ]
     }
   }
   ```

   Notes on this file:
   - The `attribution` block removes the commit trailer, the PR line and the session link, which `CLAUDE.md` forbids. Keep this object form: `"attribution": false` needs Claude Code 2.1.281 or later, and older builds skip the whole settings file when they see it.
   - Project `allow` rules take effect after each person accepts the folder-trust prompt.
   - Deny rules are guardrails, not a boundary: `git -C . push --force` slips past them. GitHub branch protection (A0) is what actually stops force-pushes.
   - The hook uses the exec form with `${CLAUDE_PROJECT_DIR}`, so it still runs when Claude works from inside `web/` or `api/`.
   - If someone works on Windows, check `/permissions` there: commands may run through the PowerShell tool, which `Bash(...)` rules don't cover.

2. **`scripts/lib/banned-terms.mjs`** exports the banned terms from `PRIORITY-CHANGES.md` §4, each with what to say instead. C's preflight (C6) imports the same module, so the hook and the preflight never disagree.
   - Generic terms are stored in plain text: "blockchain" (say "tamper-evident audit log"), "verified PCF" and "primary data".
   - The seven company names and the retired product name from `CLAUDE.md` are stored only as SHA-256 hashes of the exact word. The repo is public, and §4 keeps those names out of it, including out of a list of banned names. The checker hashes each word it scans and compares. Matching the exact case keeps ordinary lower-case words that share a spelling with a name from tripping the check. For the names that have no everyday lower-case twin, also store the hash of the lower-case form.
3. **`.claude/hooks/copy-guard.mjs`** is a Node script with no dependencies. It reads the hook's JSON from stdin, takes the text being written (`tool_input.content` for Write, `tool_input.new_string` for Edit), and checks it against the list. When a term matches, it exits with code 2 and writes the term and its replacement to stderr. Claude receives that as the tool's error and rewrites the text instead of writing it. `tool_input.file_path` arrives as an absolute path (with backslashes on Windows), so normalise it against `CLAUDE_PROJECT_DIR` before matching the exempt paths. These paths are exempt because they state the rules: `CLAUDE.md`, `docs/PRIORITY-CHANGES.md`, `docs/plan/`, `.claude/hooks/`, `scripts/lib/banned-terms.mjs`. The module exports this list of exempt paths too, so the preflight skips the same files.
4. **`.gitignore`:** add `.claude/settings.local.json`, and `.claude/worktrees/`, where `claude -w` creates its worktrees.
5. **`compose.yml`:** pass `DATA_MODE: ${DATA_MODE:-}` to the `api` service, so `DATA_MODE=memory docker compose up` switches modes; document it in `RUNBOOK.md`. An empty value keeps today's behaviour, because `AppConfig` treats blank as unset.
6. **Linux bring-up**, with the notes going into `RUNBOOK.md` (`PRIORITY-CHANGES.md` §6). Document these as commands, never as `.sh` files:
   - Compose on Linux;
   - the .NET 10 SDK on the host;
   - finding the LAN IP (`ip -4 addr`);
   - opening port 3000 when a firewall is active;
   - running the laptop's hotspot through NetworkManager.
7. **Optional:** `.claude/rules/web.md` with `paths:` frontmatter (for example `web/**/*.{ts,tsx}`), holding the React Flow state rules and the formatting rules from the top of this brief. Claude Code loads the file only when a session touches matching files, so `CLAUDE.md` stays short.

Done when: A and C have pulled and accepted the folder-trust prompt, and in a session, an edit that adds "blockchain" to a file under `web/src` is blocked by the hook with the replacement text.

Kickoff prompt:

> Read docs/plan/dev-b.md task B0, CLAUDE.md and docs/PRIORITY-CHANGES.md §4. Create .claude/settings.json, scripts/lib/banned-terms.mjs and .claude/hooks/copy-guard.mjs as described there (Node .mjs, no dependencies, no .sh). Store the company names and the retired product name only as SHA-256 hashes; never write them in plain text in any file, commit message or PR description. Then give me a one-line command that pipes a sample hook payload into the hook so I can test it by hand, and the expected output.

## B1: Store, selectors and layout (Thu–Fri 22–23 Oct)

- **`web/src/store/`** (Zustand) holds domain state (buyer, nodes by id, KPIs, audit, `lastSeq`, mode) and UI state (selected node, open panels, scenario preview). Actions:
  - `hydrate(snapshot)`, which replaces everything, `lastSeq` included, even when the new `lastSeq` is lower: the event sequence restarts at 0 when the API restarts;
  - `applyEvent(event)`, which ignores any `seq <= lastSeq` and applies idempotently (upsert nodes by id, skip audit entries whose id is already listed), because an event can repeat what a snapshot already holds. It never receives `graph.reset`: A's sync refetches the snapshot instead (`web/src/hooks/startGraphSync.ts` documents the seam);
  - `select(nodeId)`.

  Merge this interface first, on its own, because A's hook (A3) calls it.
- **Selectors:**
  - `toRfNodes`: the buyer node plus the supplier nodes, at dagre positions;
  - `toRfEdges`: supplier → buyer, with a width taken from a clamped `log(carbonKg)`;
  - `hotspotIds`: the top N by `carbonKg`, a derived overlay and never a status;
  - formatting helpers.
- **Layout** with `@dagrejs/dagre`, memoised on the set of nodes only, so selecting a node doesn't recompute it.
- Until A1 merges, type against `GraphSnapshot` in `api/src/TraceGreen.Domain/Model/Records.cs` with a local fixture, then switch to `web/src/api/schema.d.ts`.
- **Vitest** (node environment, `src/**/*.test.ts`): stale events are ignored, reset works, the hotspot choice is right, edge widths and formatting are right.

Kickoff prompt (plan mode):

> Read docs/plan/dev-b.md task B1, CLAUDE.md, and GraphSnapshot, SupplierNode and Kpis in api/src/TraceGreen.Domain/Model/Records.cs. Plan web/src/store and its selectors so that React Flow never feeds back into Zustand, and list the vitest cases. After I approve, implement it and run pnpm typecheck, pnpm lint and pnpm test.

A second session can write the selector tests while the main session builds the store. Worktrees start from `origin/main` by default, so first set `"worktree": { "baseRef": "head" }` in your `.claude/settings.local.json`, then run `claude -w b1-tests` from your task branch. Run `pnpm install` once inside the new worktree's `web/`, and merge its branch into yours when the tests pass.

## B2: Canvas, KPI bar and toolbar (Fri–Sat 23–24 Oct)

- **The custom supplier node:**
  - status colours from the tokens in `web/src/index.css`: amber, hatched for partial, green;
  - the red hotspot ring;
  - `data-testid="node-<id>"` and `data-status`;
  - the label, and the tCO₂e value with one decimal;
  - an "i" tooltip with the source, year and row label (a PLACEHOLDER source shows on screen, per §2);
  - "AI-mapped · 0.92" when the classification came from the cache or a model;
  - an entrance animation.
- **The KPI bar** shows API values only: total tCO₂e, the "Supplier-confirmed share" with its subtitle and tooltip (ruling 2, from C3), € spend, and the node count.
- **The toolbar** has Import (an `.xml` file sent as multipart to `POST /api/ingest/ubl`), Load demo invoice, and Reset demo (shown only when health reports demo tools). Errors (400, 409, 413, 422) are shown as text.
- **`web/src/pages/Dashboard.tsx`** composes them, with loading, empty and error states.

Done when: importing N1 shows an amber node with the ring in under 2 s, with KPIs from the API.

Kickoff prompt:

> Build the slice-1 dashboard per docs/plan/dev-b.md task B2, using the generated types in web/src/api/schema.d.ts and the labels in web/src/copy. Start with the custom node and its data-testid and data-status attributes, then the KPI bar, then the toolbar. Run the checks after each component and tell me what to look at in the browser.

## B3: M1 integration (Sun 25 Oct)

Integrate with A and run the M1 checklist in README §8 in both modes. C records the screen.

## B4: Slice-4 dashboard (Mon–Thu 26–29 Oct)

- **Node drawer** (the shadcn `sheet` component). It shows:
  - the status;
  - the ruling-3 provenance line: "Supplier-confirmed · <source> <year>" or "Spend-based estimate · <source>";
  - the ruling-4 uncertainty label, never ±%;
  - numbers with their "i" icons;
  - the lane cost and lead time, labelled illustrative;
  - the "Request data" and "Simulate" buttons.
- **QR card** (`qrcode.react`, already a dependency). It shows:
  - the QR code and the printed URL;
  - the expiry countdown (C9, for example "expires in 6d 23h");
  - a loopback warning when `request-link` reports `loopback`, plus ruling 6's note that only phones on the laptop's hotspot can open the link.
- **Audit panel.** It lists entries (actor, action, time) from the snapshot and from `audit.appended`. "Verify chain" calls `POST /api/audit/verify` and shows "Chain valid ✓" or "Broken at entry n ✗". It says "tamper-evident audit log", never "blockchain".
- **Connection badge** showing `connecting` (first load only), `live`, `polling` or `offline`, from A's hook.
- **Flip animation** from amber to green whenever a node's status changes to VERIFIED, whether the change arrives as `node.verified` or in a fresh snapshot (after polling or a reconnect there is no event to hang it on).

Done when: with A6 and C5 merged, a phone on the hotspot turns the node green in under 2 s.

Kickoff prompt:

> Read docs/plan/dev-b.md task B4 and the slice-3 types in web/src/api/schema.d.ts. Build the drawer first, then the QR card, the audit panel and the connection badge, as separate PRs if any grows past about 300 lines. Run the checks after each.

## B5: Simulator panel (Fri–Sat 30–31 Oct)

Inside the drawer:

- **Mode choice:** Air, Sea, Rail or Road, with the modes the lane has no leg for disabled.
- **Supplier swap** from the alternatives that A7 exposes.
- **The result** as a ghost node on the canvas. Scenario state lives apart from the base nodes.
- **Deltas:** tonnes with one decimal, euros with none, days as integers, with cost and lead time labelled illustrative.
- **The MAC badge** in four states: win-win, below the carbon price, premium, not an abatement option. The names come from C3.
- **"Apply"** calls `POST /api/scenarios`. The base node stays unchanged.

Done when: N1 air → sea shows win-win, and N1 → N1-alt shows premium or below-ETS.

Kickoff prompt:

> Read docs/plan/dev-b.md task B5, the slice-5 types in web/src/api/schema.d.ts, and the store. Plan where scenario state lives so the base nodes never change, then build the panel and the ghost node. Run the checks and tell me how to see both badge states with N1.

## B6: Playwright smoke (Sun 1 – Mon 2 Nov)

- **`web/playwright.config.ts`:** base URL `http://localhost:3000`, Chromium, one worker.
- **`web/e2e/demo-path.spec.ts`**, as one scripted run:
  1. Reset, then load the demo invoice; `node-<N1 id>` is `ESTIMATED`.
  2. Open the drawer, request a link, and read the printed URL.
  3. Open the URL in a second page with a phone viewport and submit Air + Grid.
  4. The dashboard node is `VERIFIED` within 2 s.
  5. Open the same URL again: "already used".
  6. Simulator: Sea shows win-win; N1-alt shows premium or below-ETS.
  7. The audit panel shows the chain as valid.
- Install the browser with `pnpm exec playwright install chromium`. It is a browser download, not a new package.
- Document how to run it in both modes (`DATA_MODE=memory docker compose up`).

Done when: `pnpm e2e` passes against Compose and the test ids are frozen for A9's CI job.

Kickoff prompt:

> Read docs/plan/dev-b.md task B6 and README §8 (M2). Write web/playwright.config.ts and web/e2e/demo-path.spec.ts against the running Compose stack, using the data-testid and data-status attributes already in the components. Run `pnpm e2e` and show me the result. If a step is flaky, tell me why before adding any wait.

## B7: Demo tooling (Mon 2 – Wed 4 Nov)

- **`scripts/demo-check.mjs`**, run from the laptop. It checks that `PUBLIC_BASE_URL` (from `.env.demo`) answers `/api/health`, that it is not a loopback address, and that `/api/events` opens, then prints pass or fail for each check.
- **`scripts/firewall.ps1`** for Windows, with the Linux equivalent documented as a command in `RUNBOOK.md`.
- **A CSP header** in `web/nginx.conf` for the demo image. React Flow needs inline styles, and the QR code is inline SVG. Check it by running the Playwright smoke against `compose.demo.yml`.
- **`RUNBOOK.md`** gets ruling 6: links work only for phones on the hotspot, and the second phone is the fallback.

Kickoff prompt:

> Read docs/plan/dev-b.md task B7, docs/RUNBOOK.md and web/nginx.conf. Write scripts/demo-check.mjs (Node, no dependencies), add the CSP header, and update RUNBOOK.md with the Linux commands and ruling 6. Then run the demo stack and the Playwright smoke against it.

## Reviews you do

You review A's PRs and C's PRs under `web/`.
