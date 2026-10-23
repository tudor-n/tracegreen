# Factor sources

Where every emission factor in `seed/factors.json`, and the carbon price behind the MAC badge, comes from (roadmap T4). A row that does not name a real source here is a placeholder.

## Rules

- A person reads the source and gives the value. Nobody fills in a number from memory. Claude Code may make the edit once a person has given it the value and the source row.
- A row keeps its placeholder `source` text, unchanged, until a person replaces it with a DESNZ 2026 row (tables "Freighting goods" and "Material use") or an EEA, Ember or ANRE row, naming the table, the year and the row label.
- Where no row matches exactly (zinc die-cast, PCB), write down the nearest row and why it was chosen.
- Grid rows come from EEA, Ember or ANRE. The DESNZ electricity factor is never used for Romania (roadmap FB14, FV5).
- Spend rows come from the source chosen in D1 (below).
- The "i" icon next to every number shows the row's `source`, `year` and `rowLabel`, so a placeholder row shows the word PLACEHOLDER on screen. That is intended until T4 lands; do not hide it.
- `scripts/preflight.mjs` fails while any `source` contains PLACEHOLDER or `ETS_PRICE_EUR_PER_T` is unset, so `v0.9-prehack` cannot be tagged on placeholder data.
- If the real rows change a demo badge (D5), report it in `docs/NEXT.md` and change what the demo script says. Lanes, masses and prices are never adjusted to bring a badge back.
- Once the rows are confirmed, `seed/factors.json` is updated from this table in one PR, together with the tests that pin numbers derived from it.

## Emission factors

Copied from `seed/factors.json` on 2026-10-23: 19 rows, all placeholders. "WTT included?" records whether the source row includes well-to-tank emissions.

| id | kind | key | value | unit | source | year | row label | WTT included? |
|---|---|---|---|---|---|---|---|---|
| `transport.road` | transport | road | 0.1 | kgCO2e/t.km | PLACEHOLDER - replace with DESNZ 2026 / EEA row at T4 | 2026 | HGV road freight, average laden (placeholder) |  |
| `transport.rail` | transport | rail | 0.03 | kgCO2e/t.km | PLACEHOLDER - replace with DESNZ 2026 / EEA row at T4 | 2026 | Rail freight (placeholder) |  |
| `transport.sea` | transport | sea | 0.016 | kgCO2e/t.km | PLACEHOLDER - replace with DESNZ 2026 / EEA row at T4 | 2026 | Sea container freight (placeholder) |  |
| `transport.air` | transport | air | 0.8 | kgCO2e/t.km | PLACEHOLDER - replace with DESNZ 2026 / EEA row at T4 | 2026 | Air freight, long-haul (placeholder) |  |
| `material.zinc_diecast` | material | zinc_diecast | 3000 | kgCO2e/t | PLACEHOLDER - replace with DESNZ 2026 / EEA row at T4 | 2026 | Zinc die-cast, primary production (placeholder) |  |
| `material.pcb` | material | pcb | 5000 | kgCO2e/t | PLACEHOLDER - replace with DESNZ 2026 / EEA row at T4 | 2026 | Electrical items: printed circuit boards (placeholder) |  |
| `material.wire_harness` | material | wire_harness | 2500 | kgCO2e/t | PLACEHOLDER - replace with DESNZ 2026 / EEA row at T4 | 2026 | Copper wire harness (placeholder) |  |
| `material.packaging_paper` | material | packaging_paper | 1000 | kgCO2e/t | PLACEHOLDER - replace with DESNZ 2026 / EEA row at T4 | 2026 | Paper and board packaging (placeholder) |  |
| `material.connectors` | material | connectors | 4000 | kgCO2e/t | PLACEHOLDER - replace with DESNZ 2026 / EEA row at T4 | 2026 | Electrical connectors (placeholder) |  |
| `material.plastics` | material | plastics | 3100 | kgCO2e/t | PLACEHOLDER - replace with DESNZ 2026 / EEA row at T4 | 2026 | Average plastics (placeholder) |  |
| `material.generic` | material | generic | 2000 | kgCO2e/t | PLACEHOLDER - replace with DESNZ 2026 / EEA row at T4 | 2026 | Generic manufactured goods (placeholder) |  |
| `spend.metals` | spend | metals | 0.45 | kgCO2e/EUR | PLACEHOLDER - replace with DESNZ 2026 / EEA row at T4 | 2026 | Fabricated metal products (placeholder) |  |
| `spend.electronics` | spend | electronics | 0.35 | kgCO2e/EUR | PLACEHOLDER - replace with DESNZ 2026 / EEA row at T4 | 2026 | Electronic components (placeholder) |  |
| `spend.packaging` | spend | packaging | 0.3 | kgCO2e/EUR | PLACEHOLDER - replace with DESNZ 2026 / EEA row at T4 | 2026 | Paper packaging (placeholder) |  |
| `spend.other` | spend | other | 0.4 | kgCO2e/EUR | PLACEHOLDER - replace with DESNZ 2026 / EEA row at T4 | 2026 | Other manufactured goods (placeholder) |  |
| `grid.RO` | grid | RO | 0.25 | kgCO2e/kWh | PLACEHOLDER - replace with DESNZ 2026 / EEA row at T4 | 2026 | Romania grid average (placeholder) |  |
| `grid.CN` | grid | CN | 0.55 | kgCO2e/kWh | PLACEHOLDER - replace with DESNZ 2026 / EEA row at T4 | 2026 | China grid average (placeholder) |  |
| `grid.TW` | grid | TW | 0.5 | kgCO2e/kWh | PLACEHOLDER - replace with DESNZ 2026 / EEA row at T4 | 2026 | Taiwan grid average (placeholder) |  |
| `grid.DE` | grid | DE | 0.35 | kgCO2e/kWh | PLACEHOLDER - replace with DESNZ 2026 / EEA row at T4 | 2026 | Germany grid average (placeholder) |  |

## Carbon price

| name | value | unit | source | date set |
|---|---|---|---|---|
| `ETS_PRICE_EUR_PER_T` | 70 | EUR/tCO2e | PLACEHOLDER |  |

The MAC badge compares an option's abatement cost with this price: below it the option is "below the carbon price", above it a "premium option". The 70 is a placeholder, set in `.env.demo`, `.env.example`, `compose.yml` and as the default in `api/src/TraceGreen.Api/AppConfig.cs`. A person sets the day's EUA price on 5 Nov and writes the date next to it.

## Open decisions

- **D1, spend-factor source** (C with A and B, due Fri 23 Oct): US EPA v1.3 with FX conversion, Defra, or EXIOBASE (roadmap §6.1.1). Not decided yet. Until a source with FX conversion is chosen, non-EUR invoices are refused with `UnsupportedCurrency`.
- **D6, grid rows** (C, due Sat 31 Oct): no formula reads the four `grid.*` rows today. Either source them from EEA, Ember or ANRE, or delete them.
