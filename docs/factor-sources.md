# Factor sources

Where every emission factor in `seed/factors.json`, and the carbon price behind the MAC badge, comes from (roadmap T4). A row that does not name a real source here is a placeholder.

## Rules

- A person reads the source and gives the value. Nobody fills in a number from memory. Claude Code may make the edit once a person has given it the value and the source row.
- Each kind has its own source. A row keeps its placeholder `source` text until a person replaces it with a row that names the table, the year and the row label:
  - transport: DESNZ 2026, table "Freighting goods", or EEA;
  - material: DESNZ 2026, table "Material use";
  - spend: the source chosen in D1 (below).
- The DESNZ electricity factor is never used for Romania (roadmap FB14, FV5).
- Where no row matches exactly, take the nearest row and write which one and why in the "match" column. Expect this for most materials: zinc die-cast, PCB, wire harness, connectors and generic goods.
- Well-to-tank emissions are excluded. Transport rows use DESNZ's freight factors without its separate well-to-tank tables, so "WTT included?" reads No.
- `year` is the year of the source edition (2026 for DESNZ 2026). When the data describe an earlier year, the "data year" column says so. The placeholder rows carry 2026 as well; that year means nothing until the row is sourced, which is why it is marked here.
- The "i" icon next to every number shows the row's `source`, `year` and `rowLabel`, so a placeholder row shows the word PLACEHOLDER on screen. That is intended until T4 lands; do not hide it.
- `scripts/preflight.mjs` (C6) fails while any `source` in `factors.json` contains PLACEHOLDER, while the carbon price below has no source and date, and while this file and `factors.json` disagree. An unset variable cannot be the test for the price, because `AppConfig` falls back to 70 when it is empty. So `v0.9-prehack` cannot be tagged on placeholder data.
- If the real rows change a demo badge (D5), report it in `docs/NEXT.md` and change what the demo script says. Lanes, masses and prices are never adjusted to bring a badge back.
- Once the rows are confirmed, `seed/factors.json` is updated from this file in one PR, together with the tests that pin numbers derived from it.

## Transport

| id | key | value | unit | source | year | data year | row label | WTT included? | notes |
|---|---|---|---|---|---|---|---|---|---|
| `transport.road` | road | 0.1 | kgCO2e/t.km | PLACEHOLDER - replace with a DESNZ 2026 Freighting goods or EEA row at T4 | 2026 (placeholder) |  | HGV road freight, average laden (placeholder) | No |  |
| `transport.rail` | rail | 0.03 | kgCO2e/t.km | PLACEHOLDER - replace with a DESNZ 2026 Freighting goods or EEA row at T4 | 2026 (placeholder) |  | Rail freight (placeholder) | No |  |
| `transport.sea` | sea | 0.016 | kgCO2e/t.km | PLACEHOLDER - replace with a DESNZ 2026 Freighting goods or EEA row at T4 | 2026 (placeholder) |  | Sea container freight (placeholder) | No |  |
| `transport.air` | air | 0.8 | kgCO2e/t.km | PLACEHOLDER - replace with a DESNZ 2026 Freighting goods or EEA row at T4 | 2026 (placeholder) |  | Air freight, long-haul (placeholder) | No | DESNZ publishes air freight with and without radiative forcing: name the variant in the row label. For lanes that do not touch the UK, the "international, to/from non-UK" row is likely a better fit than long-haul. |

## Material

| id | key | value | unit | source | year | data year | row label | match |
|---|---|---|---|---|---|---|---|---|
| `material.zinc_diecast` | zinc_diecast | 3000 | kgCO2e/t | PLACEHOLDER - replace with a DESNZ 2026 Material use row at T4 | 2026 (placeholder) |  | Zinc die-cast, primary production (placeholder) |  |
| `material.pcb` | pcb | 5000 | kgCO2e/t | PLACEHOLDER - replace with a DESNZ 2026 Material use row at T4 | 2026 (placeholder) |  | Electrical items: printed circuit boards (placeholder) |  |
| `material.wire_harness` | wire_harness | 2500 | kgCO2e/t | PLACEHOLDER - replace with a DESNZ 2026 Material use row at T4 | 2026 (placeholder) |  | Copper wire harness (placeholder) |  |
| `material.packaging_paper` | packaging_paper | 1000 | kgCO2e/t | PLACEHOLDER - replace with a DESNZ 2026 Material use row at T4 | 2026 (placeholder) |  | Paper and board packaging (placeholder) |  |
| `material.connectors` | connectors | 4000 | kgCO2e/t | PLACEHOLDER - replace with a DESNZ 2026 Material use row at T4 | 2026 (placeholder) |  | Electrical connectors (placeholder) |  |
| `material.plastics` | plastics | 3100 | kgCO2e/t | PLACEHOLDER - replace with a DESNZ 2026 Material use row at T4 | 2026 (placeholder) |  | Average plastics (placeholder) |  |
| `material.generic` | generic | 2000 | kgCO2e/t | PLACEHOLDER - replace with a DESNZ 2026 Material use row at T4 | 2026 (placeholder) |  | Generic manufactured goods (placeholder) |  |

## Spend

| id | key | value | unit | source | year | currency | price year | row label | match |
|---|---|---|---|---|---|---|---|---|---|
| `spend.metals` | metals | 0.45 | kgCO2e/EUR | PLACEHOLDER - replace with a row from the D1 spend-factor source at T4 | 2026 (placeholder) |  |  | Fabricated metal products (placeholder) |  |
| `spend.electronics` | electronics | 0.35 | kgCO2e/EUR | PLACEHOLDER - replace with a row from the D1 spend-factor source at T4 | 2026 (placeholder) |  |  | Electronic components (placeholder) |  |
| `spend.packaging` | packaging | 0.3 | kgCO2e/EUR | PLACEHOLDER - replace with a row from the D1 spend-factor source at T4 | 2026 (placeholder) |  |  | Paper packaging (placeholder) |  |
| `spend.other` | other | 0.4 | kgCO2e/EUR | PLACEHOLDER - replace with a row from the D1 spend-factor source at T4 | 2026 (placeholder) |  |  | Other manufactured goods (placeholder) |  |

"currency" and "price year" are those of the source's factors (for example, US dollars at 2022 purchaser prices). See D1.

## Carbon price

| name | value | unit | benchmark | source | date set |
|---|---|---|---|---|---|
| `ETS_PRICE_EUR_PER_T` | 70 | EUR/tCO2e | Settlement price of the front-December EUA futures contract on ICE Endex | PLACEHOLDER |  |

The MAC badge compares an option's abatement cost with this price: below it the option is "below the carbon price", above it a "premium option". The 70 is a placeholder. A person sets the price on 5 Nov from the benchmark above and writes the source and the date here. The value lives in four places: `.env.demo`, `.env.example`, `compose.yml` and the default in `api/src/TraceGreen.Api/AppConfig.cs`. All four change in the same commit, and preflight (C6) checks that they agree.

## Decisions

- **D1, spend-factor source** (open; C with A and B, due Fri 23 Oct): US EPA v1.3, Defra, or EXIOBASE (roadmap §6.1.1). The choice fixes a currency and a price year; US EPA v1.3, for example, is in 2022 US dollars at purchaser prices. Whichever source wins, an invoice amount has to be converted into that currency at a stated exchange rate and adjusted to that price year before the factor applies. Today the code multiplies the EUR amount by a factor in kgCO2e/EUR. Separately, invoices in other currencies than EUR are refused with `UnsupportedCurrency`.
- **D6, grid rows** (decided 2026-10-23): the four `grid.*` rows are deleted from `factors.json`. No formula read them, and EEA and ANRE cover Europe and Romania, which left Ember as the single listed option for China and Taiwan. If a formula ever needs grid intensity, the rows come back with an EEA, Ember or ANRE source.
- **Well-to-tank** (decided 2026-10-23): excluded, as stated in the rules.
- **Carbon price benchmark** (decided 2026-10-23): the front-December EUA futures settlement on ICE Endex.
