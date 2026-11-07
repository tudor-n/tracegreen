# Supplier trust (F7)

How TraceGreen judges whether a supplier's answers could be true, what it counts in the meantime, and how a person reviews them. Status by roadmap ID: `ROADMAP.md`. The decisions behind it: `DECISIONS.md`, "Supplier trust (F7)".

## The problem

Most suppliers have no legal duty to answer. A factory in China is outside EU law. An EU firm under 1,000 employees falls under the value-chain cap, and cannot be required to give more than the VSME standard asks. Before F7, two taps through the signed link made a node VERIFIED, and nothing checked the taps. Two answers move the figure most:

- **Power.** "Renewable" cuts the material-stage figure by 40% (`Estimate.RenewableShare` 0.8 × `ProcessElectricityShare` 0.5).
- **Mode.** "Sea" instead of "air" cuts the transport figure about 15× on the Shenzhen lane. The seed's DESNZ rows give 0.5313 against 0.01612 kgCO₂e/t·km (33× per tonne-km). The lane's distances are 7,500 km by air and 16,000 km by sea.

The link proves only that someone opened it. Contract pressure does not help with suppliers that nothing obliges, so the product has to make the true answer the supplier's easiest path, and a false one visible.

## Principles

1. **Ask for facts, not footprints.** A supplier gives a mode, dates, document numbers and certificate ids, and the API computes the carbon. Facts leave traces that can be checked; a typed footprint does not.
2. **Asymmetric trust.** A claim that raises the figure is taken as given, since nobody lies upward. A claim that lowers it counts once something backs it. Until then, the counted figure keeps the assumption the claim would replace:
   - the lane's default mode;
   - grid power;
   - the larger of the invoice's and the confirmed mass.
3. **Evidence ladder, ranked by who loses if the value is false:**
   1. an authority record: the customs declaration, an ANAF-signed e-invoice, the e-Transport UIT;
   2. an accredited third party: an ISO 14064-3 or 14067 statement, a CBAM verifier, a registry's certificate cancellation;
   3. an independent party's document: a carrier's bill of lading or air waybill, a forwarder's invoice;
   4. the supplier's own document;
   5. a bare answer;
   6. nothing.
4. **Review effort follows the kilograms at risk,** the way an auditor samples by materiality, not by node count.
5. **Make silence and lying costly, and truth cheap.**
   - An unbacked reduction does not count, so the supplier looks worse in the buyer's comparison until it backs the claim.
   - The question set stays inside what an SME can already answer.

## Three figures per node

| Figure | What it is |
|---|---|
| **Estimate from spend** | Invoice total × the sector's spend factor. |
| **From the supplier** | The activity figure from the answers, as answered and as counted. The counted figure adds back each reduction nothing backs yet; the gap between the two is the kilograms at risk. |
| **Best possible · illustrative** | The best state the delivery could reach. The default is the lowest-emission mode the lane has, with renewable power. The buyer can choose a mode, a power source or an alternative supplier (`PUT /api/nodes/{id}/target`). |

A VERIFIED node's `carbonKg`, the totals and the KPIs use the counted figure. The status set stays `ESTIMATED | PARTIAL | VERIFIED`, and the supplier-confirmed share keeps ruling 2's definition. `kpis.kgAtRisk` sums the unbacked reductions: what backing them could take off the total.

No seeded or demo answer claims a reduction against its lane's default:

- N1 answers air + grid on CN-RO, whose default is air.
- N2 is sea + grid on TW-RO, whose default is sea.
- N5 is road on DE.

So their figures, the hotspot ring and the badges are unchanged (`TrustAssessorTests.SeedNodesKeepTheirFigures`).

## The model (v1)

Pure C# in `api/src/TraceGreen.Domain/Trust/`. `NodeFactory` stores the counted figure and `NodeViewMapper` explains it, through the same `TrustAssessor.Assess`, so the two always agree.

### Evidence level per claim

A person's decision comes first. After it, a contradiction outranks any backing, and a claim that lowers nothing needs nothing.

| Level | When |
|---|---|
| Contradicted | A person rejected it. Or the customs declaration gives another mode. Or the goods arrived in under half the claimed mode's lead time. Or the document belongs to another mode. Or the shipping mass is below the invoice's net goods mass. |
| Accepted by a person (`reviewed`) | A person accepted it, after looking at a document. |
| Matches your records (`corroborated`) | The customs declaration gives the same mode. Or the transit time fits the claimed mode and not the lane's default. |
| Backed by a document (`documented`) | A shipping document number that passes its check and fits the mode. Or, for renewable power, a certificate type and id. |
| Not backed yet (`unproven`) | A bare answer, or a number that fails its check. |
| Nothing to back (`notNeeded`) | The answer does not lower the figure. |

Documented, corroborated and reviewed claims count in the figure. Unproven and contradicted claims keep their fallback.

### Score, 0 to 100

- **Evidence: 70 points.** Each claim that lowers the figure is weighted by how much it lowers it, and earns 0 (contradicted), 0.25 (unproven), 0.7 (documented) or 1 (corroborated or reviewed) of its weight. With no reduction claimed, all 70.
- **Channel: 30 points.** Signals take points off:

  | Signal | Points |
  |---|---|
  | Answered from the network the link was made on (keyed address hashes, never addresses) | −20 |
  | Answered within 2 minutes of the link | −10 |
  | The seller's tax id fails its check | −10 |
  | Another mode than this supplier's last delivery on the lane | −10 |
  | An earlier answer from this supplier was rejected | −20 |
  | The demo tool answered | −30 |

- **Contradictions:** any contradiction caps the score at 25.
- **Bands:** 80 and above is "well backed"; 50 to 79 is "partly backed"; below 50, or any contradiction, is "needs a check".
- **Review:** a node needs review when its band says so or any kilograms are at risk. The Overview counts these nodes, and the supplier list shows them with the most kilograms at risk first.

Every threshold and weight is in `TrustPolicy.cs`. They are policy for a person to confirm, like `AIR_HEAVY`'s 2 t, not factor data. The transit rule leans on the lanes' illustrative lead times, so it uses a factor-2 margin.

### What is checked, and where

- **Shipping documents** (`DocumentIds.cs`, mirrored in `web/src/lib/evidence.ts`):
  - a container number by its ISO 6346 check digit; about one random number in ten passes;
  - an air waybill by its mod-7 check digit; one random number in seven passes;
  - a bill of lading, CMR, e-Transport UIT and CIM/SMGS note by shape.
- **Seller tax ids** (`TaxIds.cs`):
  - Romania's CUI by its control key;
  - China's unified social credit code by GB 32100-2015's mod-31 check character;
  - Taiwan's business number by its digit sums.

  The seed's fictional N1 and N2 ids fail these checks, so their nodes show the signal.
- **The buyer's records** (`POST /api/nodes/{id}/receipt`): the goods-receipt date, and the customs declaration's mode at the border and its MRN, in its 18-character shape.
- **A person's decision** (`POST /api/nodes/{id}/claims/{kind}`), naming the value the page showed; a stale page gets 409 `ClaimChanged`.
- **The record:** every write is in the tamper-evident audit log (`verification.submitted` with the evidence and the score, `receipt.recorded`, `claim.reviewed`, `target.set`).

## Every way to check an answer

"v1" is built. "S2" comes next. "S3" needs the network, accounts or API keys.

### A. Transport mode

| # | Check | What it proves | Stage |
|---|---|---|---|
| A1 | The shipping document number with its check digit; the document's type must fit the mode | A traceable id is on record under the Supplier Terms | v1 |
| A2 | Transit time: the supplier's dispatch date against the buyer's goods-receipt date, and the lane's lead time for the mode | "Sea" received 9 days after dispatch is impossible. This also evaluates `SEA_TOO_FAST` (D3) without reading BT-72. | v1 |
| A3 | The customs import declaration (the buyer is the importer): the mode at the border (box 25) and the MRN | An authority record, independent of the supplier: the direct answer for a supplier in China | v1 |
| A4 | Incoterms on the invoice (UBL `DeliveryTerms`): under EXW, FCA or FOB the buyer's own forwarder carries the goods | Who arranged the carriage | S2 (changes `ExtractedInvoice`, so every invoice hash; reset the demo) |
| A5 | The delivery place or port on the invoice, against its UN/LOCODE function (port, airport, rail terminal) | Mode plausibility | S2 |
| A6 | The forwarder's freight invoice, imported as a second document for the delivery (F9) | An independent party's record | S2 |
| A7 | The freight charge per kg against the mode | Cost consistency | S2 |
| A8 | Carrier track-and-trace (DCSA Track & Trace, airline or IATA ONE Record tracking) and AIS vessel data | The container or waybill really moved on those dates | S3 |

### B. Power

| # | Check | Stage |
|---|---|---|
| B1 | "Renewable" asks for an instrument: an EU guarantee of origin, an I-REC, a China GEC, a Taiwan T-REC, a PPA or own generation, with its id. Without one it counts as grid, as the GHG Protocol's Scope 2 quality criteria require for market-based claims. | v1 |
| B2 | Registry check: a GO cancellation statement, an I-REC redemption statement, China's green-certificate platform | S3 |
| B3 | The certificate's period and volume, against the delivery and a plausible share of the factory's use (uploaded and read through F9) | S2 |
| B4 | Region plausibility: the seller's province from the USCC region code, against provincial grid mixes from a named source | S2 |

### C. Mass

| # | Check | Stage |
|---|---|---|
| C1 | A shipping mass below the invoice's net goods mass is impossible, so it is a contradiction. More than 2× is flagged. | v1 |
| C2 | The customs gross mass (box 35) | S2 |
| C3 | Quantity × unit weight from the supplier's history | S3 |

### D. Who the supplier is, and who answered

| # | Check | Stage |
|---|---|---|
| D1 | Tax-id check digits (RO, CN, TW) | v1 |
| D2 | Answered from the network the link was made on: the buyer's staff answering for the supplier | v1 |
| D3 | Answered within 2 minutes of the link | v1 |
| D4 | History: the mode changed on the same lane; an earlier answer rejected by a person | v1 |
| D5 | Registries: ANAF company data, EU VIES, China GSXT, Taiwan's company registry. ANAF's activity code tells a manufacturer from a trader. Its published balance sheets give headcount and turnover, so the SME status and the buyer's spend as a share of turnover. | S3 |
| D6 | Two optional taps, with no personal data: "your role" and "how do you know?" (shipping documents, memory, estimate) | S2 |
| D7 | A trader (NACE 46.xx) cannot answer for the factory's power: ask who made the goods | S3 |

### E. When suppliers send their own numbers

| # | Check | Stage |
|---|---|---|
| E1 | Ask within the VSME basic module (B3: Scope 1, Scope 2, energy use, turnover, headcount). That is the most an SME under the value-chain cap may be asked, and what banks already ask for green loans. | S3 |
| E2 | Consistency checks: Scope 2 ≈ kWh × the grid factor; intensity per € against the sector's spend factor; a cradle-to-gate figure not below material factor × mass, a physical floor the seed already has | S3 |
| E3 | Third-party statements: ISO 14064-3 or 14067, an EPD, a CBAM verifier report; the certificate looked up on IAF CertSearch | S3 |
| E4 | Peer comparison: the same category and region in the workspace, later across buyers | S3 |

### F. Legal hooks that do reach these suppliers

Confirm each against the current legal text before it goes into public copy.

- **CBAM.** Its definitive period runs from 1 January 2026. For steel, aluminium, cement, fertiliser and hydrogen goods, the EU importer reports installation-level emissions or pays default values. That gives a Chinese installation a reason to supply actual data, so ask for the CBAM communication data on those CN codes.
- **The value-chain cap.** It comes from the 2025 Omnibus. The question set stays within VSME, which is a selling point to suppliers: "we never ask more than you can be required to give".
- **Measured numbers outside the EU.** China's national ETS (power, steel, cement, aluminium), its product-footprint labelling pilots and Taiwan's carbon fee: suppliers in them already hold measured numbers.
- **Romania's e-Factura and e-Transport.** Authority records for domestic suppliers. An e-Factura download carries the Ministry's signature file, which proves the invoice reached ANAF unchanged (S3).

### G. Incentives

- **v1:** the counted figure. An unbacked reduction does not count, so backing it is in the supplier's interest whenever the buyer compares suppliers (the simulator, the alternatives, the MAC badge).
- **S2:**
  - Random evidence requests: a share of documented claims get a follow-up link asking for the document; a missing reply drops the claim to unproven and costs reputation.
  - A Supplier Terms line saying the buyer may ask for the document. This needs a new terms version (`docs/legal/README.md`).
  - An "I don't know" option for mode.
- **S3:**
  - One supplier profile across buyers (`docs/after.md`): answer once, reuse it.
  - A VSME-shaped summary the supplier can hand to its bank.
