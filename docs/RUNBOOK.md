# Runbook

How to run TraceGreen for development, for the demo laptop, and what to do when something fails.

## Development (everything in Docker)

1. Start Docker Desktop (Windows, macOS) or make sure the Docker Engine service runs (Linux, see [Linux](#linux)).
2. `docker compose build` (first time: several minutes, pulls the .NET SDK, Python and Node images).
3. `docker compose up` — brings up Postgres, the AI service, the API (applies migrations and the seed on boot) and the Vite dev server.
4. Open `http://localhost:3000`. The API is also reachable directly at `http://localhost:8080/api/health`; its OpenAPI document is at `http://localhost:8080/openapi/v1.json`.

The web and AI services reload on save (their watchers poll, so expect a second or two of delay). The API does not hot-reload inside Docker (`dotnet watch` is unreliable on Windows bind mounts): after editing C# run `docker compose restart api`; the rebuild is incremental and takes about 15 s. After changing dependencies (`Directory.Packages.props`, `pyproject.toml`, `package.json`) run `docker compose build` again and `docker compose up -V` to refresh the dependency volumes.

Tests inside the containers:

```
docker compose exec -e DATABASE_URL=postgres://tracegreen:tracegreen@db:5432/tracegreen_test api dotnet test
docker compose exec ai uv run pytest
docker compose exec web pnpm test
```

The `-e DATABASE_URL=…` matters. The api container's own `DATABASE_URL` is the dev database `tracegreen`, and the Postgres contract tests reset the demo workspace, so a plain `docker compose exec api dotnet test` wipes the live graph. Create the scratch database once first (next section).

Wipe the database and reseed from scratch: `docker compose down -v && docker compose up`.

The API picks Postgres because Compose sets `DATABASE_URL`. To run the same stack in memory mode (seed loaded at boot, nothing persisted), start it with `DATA_MODE=memory docker compose up` (PowerShell: `$env:DATA_MODE="memory"; docker compose up`, then `Remove-Item Env:DATA_MODE`). Leaving `DATA_MODE` unset or blank keeps Postgres. Check which mode is running with `curl -s localhost:8080/api/health`. Every feature is checked in both modes.

The Postgres repository contract runs only when `DATABASE_URL` is set. Create a scratch database once (`docker compose exec db psql -U tracegreen -d tracegreen -c "CREATE DATABASE tracegreen_test"`), then `DATABASE_URL=postgres://tracegreen:tracegreen@localhost:5432/tracegreen_test dotnet test` from `api/`. Never point it at the dev database: the tests reset the demo workspace.

## Playwright smoke (`pnpm e2e`)

`web/e2e/demo-path.spec.ts` walks the demo path against the running stack: reset → load the demo invoice (N1 ESTIMATED) → request a link → a phone-sized page answers Air + Grid → N1 is VERIFIED on the dashboard within 2 s of the submit → the same link says "already used" → simulator: Sea is `win-win`, N1-alt is `premium` or `below-ets`, Apply leaves N1 as it is → the audit chain is valid → no Content Security Policy violation on either page. It starts no server, and it resets the demo workspace first, so do not run it while someone is using the stack.

1. Bring the stack up (`docker compose up`, see above) and wait until `curl -s localhost:8080/api/health` answers.
2. Once per machine: `cd web && pnpm exec playwright install chromium` (a browser download, not a package).
3. `cd web && pnpm e2e`. A run takes about 7 s.

Both data modes: the stack above runs Postgres. For memory mode, recreate only the API with `DATA_MODE=memory docker compose up -d api` (PowerShell: `$env:DATA_MODE="memory"; docker compose up -d api`, then `Remove-Item Env:DATA_MODE`), run `pnpm e2e`, then `docker compose up -d api` to go back to Postgres.

Another origin, such as the demo image: `E2E_BASE_URL=http://<host>:3000 pnpm e2e`.

A failed run leaves a screenshot and a trace in `web/test-results/` (git-ignored); open the trace with `pnpm exec playwright show-trace <path>/trace.zip`.

`web/e2e/supplier-portal.spec.ts` (F12) runs after it with the same setup: reset → import `seed/invoices/adversarial.xml`, a second invoice from N3's seller → N3's drawer gives the supplier page link → a phone-sized page lists both deliveries → N3 confirmed with Road + Mostly renewable is VERIFIED on the dashboard within 2 s → the second delivery comes with Mostly renewable pre-selected and flips too → the same link opened again shows both confirmed → the audit chain is valid → no CSP violation. Its calls count against the supplier page's own allowance (3 × `RATE_LIMIT_PER_MIN`), so it never eats into the smoke's.

Rate limit: a run makes about four calls to `/api/verify*` (under the Vite dev server, React's StrictMode loads the supplier page's context twice), and the API allows `RATE_LIMIT_PER_MIN` (10) a minute per client address. Two runs a minute pass; a third within the same minute fails at step 3 with `data-result="rateLimited"`. Wait a minute, or `docker compose restart api` (the limiter lives in memory).

Frozen test hooks: the smoke and the CI job depend on these, so rename one only together with the spec.

| Where | Hooks |
|---|---|
| Sidebar (every screen) | `toolbar-reset`, `toolbar-demo`; `toolbar-audit` is the Audit log link; `import-file` (the supplier page spec) |
| Supplier tree (`/suppliers`) | `node-<id>` with `data-status` on each row; `ghost-<id>` with `data-badge` on the What if row; `ghost-<id>-saved` |
| Side panel | `node-drawer`, `drawer-request`, `qr-url`; `drawer-simulate` is the What if tab; `drawer-portal`, `portal-url` (the supplier page spec) |
| Simulator (What if tab) | `sim-mode-<mode>`, `sim-alt-<alternativeId>` with `aria-pressed`, `sim-result` with `data-badge`, `sim-apply` with `data-saved` |
| Audit log screen (`/audit`) | `audit-panel`, `audit-verify`, `audit-chain` with `data-valid` |
| Supplier page | `verify-page` with `data-phase` and `data-result`; `verify-transport-<mode>`, `verify-energy-<source>`, `verify-terms` (the terms checkbox), `verify-terms-link` (its link to `/terms/supplier`), `verify-submit` |
| Terms page | `terms-page` with `data-phase` and `data-version` |
| Supplier page list (F12) | `supplier-page` with `data-phase` and `data-result`; `delivery-<nodeId>` with `data-status`; `delivery-<nodeId>-open`, `delivery-<nodeId>-transport-<mode>` and `delivery-<nodeId>-energy-<source>` with `aria-pressed`, `delivery-<nodeId>-confirm`; `supplier-terms` (the page's one terms checkbox, shown while a delivery is open for answers) and `supplier-terms-link` |

## Demo laptop (production images)

1. Put the laptop and the phones on the same network (a phone hotspot, or the laptop's own hotspot: Windows Mobile Hotspot, whose IP is usually `192.168.137.1`, or NetworkManager on Linux, usually `10.42.0.1`, see [Linux](#linux)). Find the laptop IP with `ipconfig` (Windows) or `ip -4 addr` (Linux).
2. Edit `.env.demo`: `PUBLIC_BASE_URL=http://<laptop-ip>:3000`. Or leave it empty: links then take their base from the dashboard's own request, so open the dashboard at `http://<laptop-ip>:3000`, never `localhost` (the link card warns when the base is a loopback address).
3. `docker compose -f compose.yml -f compose.demo.yml up --build` — runs the one-shot migrate + seed, then the API, AI and nginx (serving the built SPA on port 3000 and proxying `/api`). Fallback without the database: `DATA_MODE=memory docker compose -f compose.yml -f compose.demo.yml up -d` (the seed is loaded at boot, nothing persists).
4. Allow inbound TCP 3000 once in Windows Firewall (elevated PowerShell): `scripts/firewall.ps1`. On Linux, see the firewall notes under [Linux](#linux).
5. From the laptop, run `node scripts/demo-check.mjs` to confirm the LAN URL answers; then open the LAN URL on a phone. (`demo-check.mjs` and `firewall.ps1` are task B7 and not written yet. Until then: `curl -s http://<laptop-ip>:3000/api/health` must show `"loopback":false`, and the same URL must open on the phone.)

Only phones on the laptop's network can open a link (ruling R6 in `docs/DECISIONS.md`): a phone on mobile data or another Wi-Fi cannot. Keep the team's second phone on the hotspot as the fallback.

Reset to the demo start state at any time with the "Reset demo" button (or `POST /api/dev/reset`). Restarting the stack keeps the data in the `pgdata` volume; `down -v` wipes it and the next `up` reseeds.

**Handing over to the second phone.** A link works once: if the first phone could not open it, scan the same QR code with the second; if the first phone already submitted, request a new link.

**The demo checks (B7).**

- `node scripts/demo-check.mjs` prints PASS or FAIL for five checks and exits 1 unless all pass: the URL is set (`PUBLIC_BASE_URL` from `.env.demo`; when it is left empty, pass the address the dashboard will be opened at, `node scripts/demo-check.mjs http://<laptop-ip>:3000`), it is not a loopback address, `/api/health` answers on it, the API builds its QR links on it (with `PUBLIC_BASE_URL` set this fails until the stack is restarted after editing `.env.demo`; without it, the check reminds you to open the dashboard at that address), and `/api/events` delivers its first event through nginx. It runs from the laptop, so it proves the stack answers on that address; a phone also needs the hotspot and the firewall rule.
- `scripts/firewall.ps1` adds one inbound rule, "TraceGreen demo (TCP 3000)", for TCP 3000 from the local subnet on every network profile (Windows often files a Mobile Hotspot network as Public). It needs an elevated PowerShell; if the execution policy blocks it, run `powershell -ExecutionPolicy Bypass -File scripts/firewall.ps1`. Running it twice changes nothing, and `-Remove` deletes the rule after the demo.
- The Playwright smoke runs against the demo image too: `docker compose -f compose.yml -f compose.demo.yml up --build -d`, then `cd web && pnpm e2e`. Its last step fails on any Content Security Policy violation the browser reports, on the dashboard or on the phone page. To go back to development afterwards, run `docker compose build` before `docker compose up -d --remove-orphans`, because the demo build reuses the image names.

## Running services on the host instead

- API: .NET 10 SDK on `PATH` (see `api/global.json`), then `cd api && dotnet run --project src/TraceGreen.Api`. Without `DATABASE_URL` it runs in memory mode with the seed loaded.
- AI: `cd ai && uv run uvicorn app.main:app --reload --port 8000`.
- Web: `cd web && pnpm install && pnpm dev` (proxies `/api` to `API_URL`, default `http://localhost:8080`).

Copy `.env.example` to `.env` for the variables each service reads.

## Linux

Everything above works on Linux; these are the Linux-specific steps, run by hand (the repo has no `.sh` scripts). Commands are for Ubuntu; other distributions differ only in the package manager.

**Docker and Compose.** Install Docker Engine and the Compose plugin from Docker's apt repository (docs.docker.com, "Install Docker Engine on Ubuntu"), then let your user run it without `sudo` and start it at boot:

```
sudo apt install docker-ce docker-ce-cli containerd.io docker-compose-plugin
sudo usermod -aG docker $USER        # log out and back in once
sudo systemctl enable --now docker
docker compose version
```

Linux bind mounts deliver inotify events, so the polling watchers in `compose.yml` are not needed there; they still work, just with a second of delay.

**.NET 10 SDK on the host.** Needed for `dotnet test` in worktree sessions, because the `api` container only sees the main checkout. `api/global.json` asks for 10.0.100 or later:

```
sudo apt install dotnet-sdk-10.0
dotnet --list-sdks
```

**LAN IP.** `ip -4 addr` lists every interface; use the `inet` address of the Wi-Fi interface (for example `wlp8s0`) or of the hotspot. `ip -4 -br addr` prints one line per interface. Docker's own bridges (`docker0`, `br-…`, `172.x`) are not reachable from a phone.

**Firewall.** Ubuntu ships `ufw`; check it with `sudo ufw status`. Ports published by Docker (3000, 8080, 5432) bypass `ufw`, because Docker writes its own iptables rules, so the containers are reachable from the network even when `ufw` is active. Port 3000 needs a rule only when the web server runs on the host (`pnpm dev` or `vite preview`):

```
sudo ufw allow 3000/tcp
sudo ufw delete allow 3000/tcp       # after the demo
```

With `firewalld` instead: `sudo firewall-cmd --add-port=3000/tcp` (lasts until reboot).

**Hotspot through NetworkManager.** Phones join the laptop's own Wi-Fi network:

```
nmcli device wifi hotspot ifname wlp8s0 ssid TraceGreen password <8+ characters>
ip -4 addr show wlp8s0               # laptop IP on the hotspot, usually 10.42.0.1
nmcli connection down Hotspot        # stop it; reconnect Wi-Fi with: nmcli device wifi connect <ssid>
```

The interface name comes from `nmcli device` (type `wifi`). Most laptop Wi-Fi cards cannot join a network and run a hotspot at the same time, so starting the hotspot drops the laptop's Wi-Fi; that is fine, the demo needs no internet. Set `PUBLIC_BASE_URL=http://10.42.0.1:3000` in `.env.demo` (or whatever `ip -4 addr` shows). Only phones on this hotspot can open a verification link.

## When something fails

| Symptom | Check |
|---|---|
| `web` is up but `/api/health` returns 502/504 | `docker compose logs api` — the first `dotnet watch` build takes up to a minute; wait for `Now listening on`. |
| API exits immediately with `LINK_SECRET` or `DATA_MODE` error | The environment is invalid; the message names the variable. |
| Phone cannot open the QR link | The link must use the laptop LAN IP, not `localhost`: set `PUBLIC_BASE_URL` in `.env.demo`. Then firewall (step 4 above) and `node scripts/demo-check.mjs`. |
| Phone gets "Too many attempts" (429) | `/api/verify*` allows `RATE_LIMIT_PER_MIN` (default 10) calls a minute per client address. Behind Vite or nginx the address comes from `X-Forwarded-For`, which the API believes only from `TRUSTED_PROXIES` (default `127.0.0.0/8,::1/128,172.16.0.0/12`: the host and Docker's address pool). If Docker on this machine uses another range (`docker network inspect tracegreen_default`), add it there; if every phone shows the same address in the bucket, Docker is hiding the phone behind its gateway and they share one allowance. |
| Node does not flip after the supplier submits | The dashboard badge shows `polling` when the event stream is down; it still refreshes every 5 s. Check `docker compose logs api` for the SSE route. |
| Changes on the host are not picked up in a container | Polling is enabled through `DOTNET_USE_POLLING_FILE_WATCHER`, `WATCHFILES_FORCE_POLLING`, `CHOKIDAR_USEPOLLING`; confirm the bind mount in `docker compose config`. |
