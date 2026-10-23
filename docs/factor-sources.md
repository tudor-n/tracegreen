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

Source (D1): US EPA, Supply Chain Greenhouse Gas Emission Factors v1.3 by NAICS-6 (published July 2024), file `SupplyChainGHGEmissionFactors_v1.3.0_NAICS_CO2e_USD2022.csv` from https://pasteur.epa.gov/uploads/10.23719/1531143/ (SHA-256 `6025a14c4fe16675735efba1683030e4d36011e3973f3c4261f8231aa69c002f`). Column used: "Supply Chain Emission Factors with Margins", because invoice amounts are purchaser prices. GWP: IPCC AR5, 100 years. Rows chosen and checked against the file by a person on 2026-10-23.

| id | key | value | unit | source | year | currency | price year | row label | match |
|---|---|---|---|---|---|---|---|---|---|
| `spend.diecast_nonferrous` | diecast_nonferrous | 0.462 | kgCO2e/USD | US EPA v1.3, with margins | 2024 | USD | 2022 | 331523 Nonferrous Metal Die-Casting Foundries | Zinc die-cast parts (N1). Zinc is not named; die-casting is grouped by process. 331524 and 331529 share the value (USEEIO 331520). |
| `spend.pcb_bare` | pcb_bare | 0.098 | kgCO2e/USD | US EPA v1.3, with margins | 2024 | USD | 2022 | 334412 Bare Printed Circuit Board Manufacturing | Bare FR4 boards (N2). Shares the value with 334417 (USEEIO 33441A). |
| `spend.vehicle_electrical` | vehicle_electrical | 0.218 | kgCO2e/USD | US EPA v1.3, with margins | 2024 | USD | 2022 | 336320 Motor Vehicle Electrical and Electronic Equipment Manufacturing | Wire harnesses (N3). The NAICS index lists automotive harness sets under 336320. Fallback if that does not hold: 335929 (0.235). |
| `spend.corrugated_boxes` | corrugated_boxes | 0.479 | kgCO2e/USD | US EPA v1.3, with margins | 2024 | USD | 2022 | 322211 Corrugated and Solid Fiber Box Manufacturing | Corrugated shipping boxes (N4). |
| `spend.electronic_connectors` | electronic_connectors | 0.098 | kgCO2e/USD | US EPA v1.3, with margins | 2024 | USD | 2022 | 334417 Electronic Connector Manufacturing | Automotive connectors (N5). |
| `spend.plastics_other` | plastics_other | 0.371 | kgCO2e/USD | US EPA v1.3, with margins | 2024 | USD | 2022 | 326199 All Other Plastics Product Manufacturing | Plastic parts; no seed invoice uses it. |
| `spend.misc_manufacturing` | misc_manufacturing | 0.182 | kgCO2e/USD | US EPA v1.3, with margins | 2024 | USD | 2022 | 339999 All Other Miscellaneous Manufacturing | UNCLASSIFIED lines. |

Each category has its own spend row (`sectorKey` in `seed/categories.json`); the four placeholder buckets are gone.

### Converting an invoice amount

The API converts on every import (`api/src/TraceGreen.Domain/Carbon/SpendConversion.cs`), with the tables in `seed/spend-conversion.json`:

`kg = invoice EUR × USD/EUR on the invoice date × CPI-U(2022) / CPI-U(invoice year) × factor`

- Exchange rate: ECB euro reference rate, US dollar/Euro (`EXR.D.USD.EUR.SP00.A`), daily from 2024-01-02, retrieved 2026-10-23. A weekend, holiday or date after the table uses the last published rate before it; a date before the table uses the first rate. The row label names the date used.
- Price adjustment: US BLS CPI-U, U.S. city average, all items, not seasonally adjusted (`CUUR0000SA0`), annual averages 2022 292.655, 2023 304.702, 2024 313.689, 2025 321.943, retrieved 2026-10-23. A year without an annual average yet (2026) uses the latest full year, and the label says so. The ratio is rounded to 4 decimals (2022/2025 = 0.9090) and used as shown.
- The node's spend figure lists three rows behind the "i" icon: the EPA row, the exchange rate and the price adjustment.
- Refreshing `spend-conversion.json` (for example before tagging `v0.9-prehack`) changes the spend kg of invoices whose date has a new rate, and the pinned N1 number in `IngestEndpointTests`. N1 is dated 2026-10-28, after the last rate in the table, so it uses the 2026-10-23 rate for now.

## Carbon price

| name | value | unit | benchmark | source | date set |
|---|---|---|---|---|---|
| `ETS_PRICE_EUR_PER_T` | 70 | EUR/tCO2e | Settlement price of the front-December EUA futures contract on ICE Endex | PLACEHOLDER |  |

The MAC badge compares an option's abatement cost with this price: below it the option is "below the carbon price", above it a "premium option". The 70 is a placeholder. A person sets the price on 5 Nov from the benchmark above and writes the source and the date here. The value lives in four places: `.env.demo`, `.env.example`, `compose.yml` and the default in `api/src/TraceGreen.Api/AppConfig.cs`. All four change in the same commit, and preflight (C6) checks that they agree.

## Decisions

- **D1, spend-factor source** (decided 2026-10-23): US EPA Supply Chain GHG Emission Factors v1.3, with margins, converted on every import at the ECB rate of the invoice date and CPI-U annual averages (see Spend). EXIOBASE was ruled out: its licence is non-commercial and forbids publishing coefficients derived from it, and it ships no ready multipliers. Defra's spend factors are UK, in GBP and older. v1.4 (2025, CC BY 4.0, 2024 USD) exists but is published by Cornerstone rather than EPA. US factors describe the US economy, so they likely understate imports from more carbon-intensive grids; the node already reads "Spend-based estimate". Invoices in other currencies than EUR are still refused with `UnsupportedCurrency`.
- **D6, grid rows** (decided 2026-10-23): the four `grid.*` rows are deleted from `factors.json`. No formula read them, and EEA and ANRE cover Europe and Romania, which left Ember as the single listed option for China and Taiwan. If a formula ever needs grid intensity, the rows come back with an EEA, Ember or ANRE source.
- **Well-to-tank** (decided 2026-10-23): excluded, as stated in the rules.
- **Carbon price benchmark** (decided 2026-10-23): the front-December EUA futures settlement on ICE Endex.
