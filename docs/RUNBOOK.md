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
docker compose exec api dotnet test
docker compose exec ai uv run pytest
docker compose exec web pnpm test
```

Wipe the database and reseed from scratch: `docker compose down -v && docker compose up`.

The API picks Postgres because Compose sets `DATABASE_URL`. To run the same stack in memory mode (seed loaded at boot, nothing persisted), start it with `DATA_MODE=memory docker compose up` (PowerShell: `$env:DATA_MODE="memory"; docker compose up`, then `Remove-Item Env:DATA_MODE`). Leaving `DATA_MODE` unset or blank keeps Postgres. Check which mode is running with `curl -s localhost:8080/api/health`. Every feature is checked in both modes.

The Postgres repository contract runs only when `DATABASE_URL` is set. Create a scratch database once (`docker compose exec db psql -U tracegreen -d tracegreen -c "CREATE DATABASE tracegreen_test"`), then `DATABASE_URL=postgres://tracegreen:tracegreen@localhost:5432/tracegreen_test dotnet test` from `api/`. Never point it at the dev database: the tests reset the demo workspace.

## Demo laptop (production images)

1. Put the laptop and the phones on the same network (a phone hotspot, or the laptop's own hotspot: Windows Mobile Hotspot, whose IP is usually `192.168.137.1`, or NetworkManager on Linux, usually `10.42.0.1`, see [Linux](#linux)). Find the laptop IP with `ipconfig` (Windows) or `ip -4 addr` (Linux).
2. Edit `.env.demo`: `PUBLIC_BASE_URL=http://<laptop-ip>:3000`.
3. `docker compose -f compose.yml -f compose.demo.yml up --build` — runs the one-shot migrate + seed, then the API, AI and nginx (serving the built SPA on port 3000 and proxying `/api`).
4. Allow inbound TCP 3000 once in Windows Firewall (elevated PowerShell): `scripts/firewall.ps1`. On Linux, see the firewall notes under [Linux](#linux).
5. From the laptop, run `node scripts/demo-check.mjs` to confirm the LAN URL answers; then open the LAN URL on a phone.

Reset to the demo start state at any time with the "Reset demo" button (or `POST /api/dev/reset`). Restarting the stack keeps the data in the `pgdata` volume; `down -v` wipes it and the next `up` reseeds.

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
