# Supplier terms

Before a supplier can send its answers on `/verify/:nodeId`, it ticks a box that accepts the Supplier Terms. The text lives in `seed/supplier-terms.json` (Romanian and English, version `2026-11-06`). It is served by the API, shown on the supplier page and printable at `/terms/supplier`. This page explains why the terms say what they say, what they can and cannot do, and how to change them.

**Status: draft.** Nobody with legal training has reviewed the text yet. The operator's legal name, CUI, registered seat, city and contact address are `[PLACEHOLDER: …]`, which shows on screen. `node scripts/preflight.mjs` fails until a person fills them in. This page is the team's research, not legal advice. A Romanian lawyer should read the terms and the checklist below before the service is used with a real supplier.

## What the terms do, and what they cannot do

The terms are an agreement between the operator and the supplier. Under the relativity of contracts (Romanian Civil Code, art. 1280), they bind those two parties and nobody else. Within that relationship they do four things:

1. **The supplier vouches for its answers** (section 3). The person who submits states that they have authority to answer for the supplier, and that the answers are accurate and complete to the best of their knowledge.
2. **The supplier acknowledges what TraceGreen does not do** (section 4). TraceGreen records the answers but does not check, audit or certify them. The figures it shows are estimates, not an externally verified product carbon footprint.
3. **The operator is not liable for false answers, to the extent the law allows** (section 6). The supplier also indemnifies the operator against claims by the buyer or third parties caused by answers it knew, or should have known, were false (section 7).
4. **The acceptance is recorded as evidence** (section 9). Each `verification.submitted` entry in the tamper-evident audit log carries the terms' version and SHA-256 fingerprint. The parties agree that this record may serve as evidence, and either party may prove the contrary.

**What the terms cannot do is stop the buyer from suing the operator.** The buyer is not a party to them. If a supplier's false answer misleads a buyer, the buyer's claim against the operator is governed by the buyer's own contract, which is signed on paper. That contract needs these clauses to close the gap:

- The buyer relies on supplier answers and on TraceGreen's estimates at its own risk. TraceGreen records supplier statements but does not check them.
- The operator gives no warranty that supplier answers, emission factors or estimates are accurate, complete or fit for a reporting purpose. The figures are estimates, not an audit or assurance opinion.
- A cap on the operator's liability, for example the fees paid in the last 12 months, with the same carve-outs as section 6 (intent, gross negligence, harm to life or health).
- The buyer remains responsible for its own sustainability reporting, including what it requests from suppliers and how it uses the answers.
- The buyer indemnifies the operator against claims caused by its misuse of the service, for example sending links to people who are not authorised to answer for a supplier.

Section 7 then lets the operator pass a buyer's claim on to the supplier whose false answer caused it.

## How acceptance works, and why

| Requirement | Source | How the service meets it |
|---|---|---|
| Standard clauses that limit liability, choose the law or choose the court have no effect unless the other party accepts them expressly and in writing | Civil Code art. 1202–1203 (*clauze neuzuale*) | The checkbox statement names section 6 (liability), section 7 (indemnity) and section 10 (law and courts) and says "expressly accept". The box starts unticked and the page never ticks it. |
| A liability exclusion cannot cover intent, gross negligence, or harm to life, physical integrity or health | Civil Code art. 1355 | Section 6 keeps all three, and any liability that cannot be excluded by law, outside the exclusion. Without these carve-outs the clause could be void. |
| In business-to-business contracts, a click-accepted clause counts as "writing" if the text could be printed or saved before acceptance | CJEU C-322/14 *El Majdoub* (2015), on Brussels I art. 23, now art. 25 of Regulation 1215/2012 | The full text opens on the page, and `/terms/supplier` serves a printable copy at a stable address. That address carries no token, so a printout or saved copy never contains a working link. |
| Contract terms concluded electronically must be available in a form the recipient can store and reproduce | e-Commerce Directive 2000/31/EC art. 10(3); Legea 365/2002 | The printable page has a Print or save button and shows the version and the SHA-256 fingerprint. |
| Electronic contracts follow the special law on electronic commerce; an e-signature has legal effect | Civil Code art. 1245; Legea 214/2024 (replaced Legea 455/2001 on 8 October 2024) | A tick is a simple electronic signature with limited probative value, so the service adds evidence. The API refuses a submit unless it carries the SHA-256 of the current terms (`422 TermsNotAccepted`). It stores the version and SHA-256 on the verification row and in the hash-chained audit entry, with the time. |
| A person must have authority to bind the supplier | Civil Code, representation (art. 1295 and following) | The checkbox statement and section 3 include the representation of authority, and section 7 covers a submission made without it. |
| Consumer unfair-terms law protects consumers | Legea 193/2000 | It does not apply: suppliers answer as businesses. |
| An IP-address hash that the operator can recompute with its key is still personal data for the operator | GDPR art. 4(5); CJEU C-413/23 P *EDPS v SRB* (2025) | Section 8 is a short privacy notice covering what is stored, why, the legal basis (legitimate interest, art. 6(1)(f)), how long, the data subject's rights and the authority to complain to (ANSPDCP). |
| Large companies may not demand more from suppliers under 1,000 employees than the voluntary VSME standard covers | Directive (EU) 2026/470 (Omnibus I), the value-chain cap | Section 2 says answering is voluntary and that the page asks for nothing beyond its two questions and the optional mass. |

## Section by section

1. **Parties and scope.** The operator (placeholder), the supplier and the buyer are each named. The buyer is said not to be a party, so nobody reads the terms as a three-way contract.
2. **What is asked, and why.** This states the purpose of the data, as GDPR transparency requires. It also says answering is voluntary and a skip has a known effect: that part stays on the spend-based estimate.
3. **Statements.** "To the best of its knowledge" is the usual standard for supplier sustainability data. Without it, a supplier would be warranting freight details it cannot know, and the clause would read as unfair. The duty to correct goes to the buyer, because each link works once.
4. **What TraceGreen does not do.** This keeps the plausibility hints (`AIR_HEAVY`) from being read as checks, and it matches the product's labelling rules: "Supplier-confirmed", never externally verified.
5. **Relationship with the buyer.** The operator is a conduit, not an agent and not a party to the supply contract.
6. **Responsibility and limitation of liability.** The exclusion covers false answers, the buyer's decisions and the page being unavailable, within the art. 1355 limits.
7. **Indemnity.** The supplier covers the operator for claims caused by answers it knew, or should reasonably have known, were false. The operator has to notify the supplier and let it take part in the defence; that reciprocity makes the clause more likely to hold.
8. **Personal data.** The privacy notice. Retention is at most three years, the general limitation period (Civil Code art. 2517), so the record survives as long as a claim could be brought. *Open:* nothing deletes old verification rows yet (`docs/NEXT.md`).
9. **Records and evidence.** An agreement on evidence. It is rebuttable ("either party may prove the contrary"), which keeps it within what Romanian civil procedure allows parties to agree on evidence.
10. **Governing law and courts.** Romanian law, and the courts at the operator's seat (placeholder). This binds suppliers in the EU through Brussels I bis art. 25. For suppliers outside the EU (in the demo, China and Taiwan), whether a Romanian judgment can be enforced there is a separate question; see the checklist.
11. **Language, versions, severability.** The Romanian text prevails. Each submission is governed by the version shown when it was made. An invalid clause leaves the rest in force (compare Civil Code art. 1255 on partial nullity).

## Checklist for the lawyer

- [ ] Operator details: legal name, CUI, registered seat, the city for section 10, and a contact address for sections 8 and 11.
- [ ] Is one checkbox that names sections 6, 7 and 10 enough for art. 1203, or should each clause have its own box?
- [ ] Is the indemnity in section 7 enforceable as written against a supplier that is a small company? Does "should reasonably have known" need narrowing to intent or gross negligence?
- [ ] Section 10 against suppliers in China and Taiwan: is arbitration (for example the Court of International Commercial Arbitration attached to the CCIR) a better forum than the courts at the operator's seat, given that the New York Convention covers enforcement of arbitral awards?
- [ ] Retention in section 8: three years from the submission, and whether the audit log entry (which holds no address hash) may be kept longer.
- [ ] Is the operator a controller for the IP hash and the answers, and is the buyer a separate controller for what it does with the answers? Is a data processing agreement with buyers needed?
- [ ] Whether the paper buyer contract (above) covers the reliance, warranty, cap and indemnity points.
- [ ] Whether the Romanian and English texts say the same thing.

## Changing the terms

A published version is never edited in place. The audit log records the SHA-256 of the exact text each supplier accepted, so the text behind every fingerprint must stay recoverable from git.

1. Edit `seed/supplier-terms.json` in both languages, keeping the same numbered sections in each. `SeedLoader` refuses a file with a language, the acceptance statement, a heading or a paragraph missing, or with sections numbered differently in the two languages.
2. Set `version` to the new date (`YYYY-MM-DD`).
3. If the clauses that need express acceptance move or change, update the `acceptance` statement so it still names them. `SeedLoaderTests` checks that sections 6, 7 and 10 are named.
4. Restart the API (`docker compose restart api`). It reads `seed/` once at startup. A supplier page opened before the restart gets `422 TermsNotAccepted` on submit and shows "The terms have been updated" with a retry that loads the new text.
5. Commit with a subject such as `F3: supplier terms 2026-12-01`. To find the text behind a fingerprint in the audit log, check out the commit, start the API and compare `sha256` from `GET /api/terms/supplier`.

The fingerprint is the SHA-256 of the file's canonical JSON (keys sorted, no whitespace, as `Domain/Security/CanonicalJson.cs` writes it), so reformatting the file keeps it and changing any word changes it.

## Sources

- Romanian Civil Code (Legea 287/2009): [art. 1202](https://www.codulcivil.ro/art-1202-Clauze-standard/), [art. 1203](https://notari.pro/noul-cod-civil-legea-287-2009/art-1203-clauze-neuzuale-incheierea-contractului-contractul), [art. 1245](https://notari.pro/noul-cod-civil-legea-287-2009/art-1245-forma-contractelor-electronice-incheierea-contractului-contractul), [art. 1355](https://www.codulcivil.ro/art-1355/)
- [Legea 365/2002 on electronic commerce, republished (legislatie.just.ro)](https://legislatie.just.ro/Public/DetaliiDocumentAfis/77218)
- Legea 214/2024 on electronic signatures: [BPV Grigorescu](https://www.bpv-grigorescu.com/latest/publications/new-legal-provisions-on-the-use-of-electronic-signatures), [Lynx Legal](https://lynx.legal/romania-the-impact-of-law-no-214-2024-on-the-use-of-electronic-signatures-and-modernization-of-digital-processes-in-romania/)
- CJEU C-322/14 *El Majdoub*: [A&L Goodbody](https://www.algoodbody.com/insights-publications/cjeu-confirms-that-click-wrapping-meets-the-requirements-of-article-23-of-t), [conflictoflaws.net](https://conflictoflaws.net/2015/the-ecj-on-choice-of-court-agreements-relating-to-contracts-concluded-electronically/?print=pdf)
- CJEU C-413/23 P *EDPS v SRB*: [Jones Day](https://www.jonesday.com/es/insights/2025/09/cjeu-clarifies-scope-of-personal-data-in-edps-v-srb-decision)
- CSRD Omnibus I and the value-chain cap: [EcoVadis](https://ecovadis.com/blog/value-chain-action-reporting-after-omnibus/), [BCLP](https://www.bclplaw.com/en-US/events-insights-news/csrd-requesting-data-from-sme-suppliers.html)
- Industry practice for supplier data terms and product-footprint disclaimers: [Terrascope supplier terms](https://support-v2.terrascope.com/en/articles/16000677-supplier-t-cs), [BASF](https://www.basf.com/dam/jcr:299033b6-8771-40a5-b1c5-370191d683a8/PCF-Disclaimer%20for%20IT-Platform_Dec2024.pdf), [LyondellBasell](https://www.lyondellbasell.com/globalassets/lyb/sustainability/reporting/product-carbon-bootprint-disclaimer-update-july242025-lyb.pdf)
