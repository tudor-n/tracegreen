# Runbook

How to run TraceGreen for development, for the demo laptop, and what to do when something fails.

## Development (everything in Docker)

1. Start Docker Desktop.
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

The Postgres repository contract runs only when `DATABASE_URL` is set. Create a scratch database once (`docker compose exec db psql -U tracegreen -d tracegreen -c "CREATE DATABASE tracegreen_test"`), then `DATABASE_URL=postgres://tracegreen:tracegreen@localhost:5432/tracegreen_test dotnet test` from `api/`. Never point it at the dev database: the tests reset the demo workspace.

## Demo laptop (production images)

1. Put the laptop and the phones on the same network (a phone hotspot, or the laptop's own Windows Mobile Hotspot, whose IP is usually `192.168.137.1`). Find the laptop IP with `ipconfig`.
2. Edit `.env.demo`: `PUBLIC_BASE_URL=http://<laptop-ip>:3000`.
3. `docker compose -f compose.yml -f compose.demo.yml up --build` — runs the one-shot migrate + seed, then the API, AI and nginx (serving the built SPA on port 3000 and proxying `/api`).
4. Allow inbound TCP 3000 once in Windows Firewall (elevated PowerShell): `scripts/firewall.ps1`.
5. From the laptop, run `node scripts/demo-check.mjs` to confirm the LAN URL answers; then open the LAN URL on a phone.

Reset to the demo start state at any time with the "Reset demo" button (or `POST /api/dev/reset`). Restarting the stack keeps the data in the `pgdata` volume; `down -v` wipes it and the next `up` reseeds.

## Running services on the host instead

- API: .NET 10 SDK on `PATH` (see `api/global.json`), then `cd api && dotnet run --project src/TraceGreen.Api`. Without `DATABASE_URL` it runs in memory mode with the seed loaded.
- AI: `cd ai && uv run uvicorn app.main:app --reload --port 8000`.
- Web: `cd web && pnpm install && pnpm dev` (proxies `/api` to `API_URL`, default `http://localhost:8080`).

Copy `.env.example` to `.env` for the variables each service reads.

## When something fails

| Symptom | Check |
|---|---|
| `web` is up but `/api/health` returns 502/504 | `docker compose logs api` — the first `dotnet watch` build takes up to a minute; wait for `Now listening on`. |
| API exits immediately with `LINK_SECRET` or `DATA_MODE` error | The environment is invalid; the message names the variable. |
| Phone cannot open the QR link | The link must use the laptop LAN IP, not `localhost`: set `PUBLIC_BASE_URL` in `.env.demo`. Then firewall (step 4 above) and `node scripts/demo-check.mjs`. |
| Node does not flip after the supplier submits | The dashboard badge shows `polling` when the event stream is down; it still refreshes every 5 s. Check `docker compose logs api` for the SSE route. |
| Changes on the host are not picked up in a container | Polling is enabled through `DOTNET_USE_POLLING_FILE_WATCHER`, `WATCHFILES_FORCE_POLLING`, `CHOKIDAR_USEPOLLING`; confirm the bind mount in `docker compose config`. |
