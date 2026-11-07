# Supplier terms

Before a supplier can send its answers on `/verify/:nodeId`, or on the supplier page `/supplier` (F12, where one box above the list covers the answers sent from that page visit), it ticks a box that accepts the Supplier Terms. The text lives in `seed/supplier-terms.json` (Romanian and English, version `2026-11-06`). It is served by the API, shown on the supplier page and printable at `/terms/supplier`. This page explains why the terms say what they say, what they can and cannot do, and how to change them.

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
| Standard clauses that limit liability, choose the law or choose the court have no effect unless the other party accepts them expressly and in writing | Civil Code art. 1202–1203 (*clauze neuzuale*) | The checkbox statement names §6 (liability), §7 (indemnity) and §10 (law and courts) and says "expressly accept". The box starts unticked and the page never ticks it. |
| A liability exclusion cannot cover intent, gross negligence, or harm to life, physical integrity or health | Civil Code art. 1355 | Section 6 keeps all three, and any liability that cannot be excluded by law, outside the exclusion. Without these carve-outs the clause could be void. |
| In business-to-business contracts, a click-accepted clause counts as "writing" if the text could be printed or saved before acceptance | CJEU C-322/14 *El Majdoub* (2015), on Brussels I art. 23, now art. 25 of Regulation 1215/2012 | The words "Supplier Terms" in the checkbox statement link to the full text at `/terms/supplier`, which opens in a new tab before anything is ticked or sent. That address carries no token, so a printout or saved copy never contains a working link. |
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
- [ ] On the supplier page (`/supplier`), one tick covers every answer sent during that page visit, and each answer is recorded with the terms' version and SHA-256. Is that enough, or should each delivery have its own tick?
- [ ] Is the indemnity in section 7 enforceable as written against a supplier that is a small company? Does "should reasonably have known" need narrowing to intent or gross negligence?
- [ ] Section 10 against suppliers in China and Taiwan: is arbitration (for example the Court of International Commercial Arbitration attached to the CCIR) a better forum than the courts at the operator's seat, given that the New York Convention covers enforcement of arbitral awards?
- [ ] Retention in section 8: three years from the submission, and whether the audit log entry (which holds no address hash) may be kept longer.
- [ ] Is the operator a controller for the IP hash and the answers, and is the buyer a separate controller for what it does with the answers? Is a data processing agreement with buyers needed?
- [ ] Whether the paper buyer contract (above) covers the reliance, warranty, cap and indemnity points.
- [ ] Whether the Romanian and English texts say the same thing.

## Changing the terms

A published version is never edited in place. The audit log records the SHA-256 of the exact text each supplier accepted, so the text behind every fingerprint must stay recoverable. `seed/supplier-terms-versions.json` pins every published version to its fingerprint. The API refuses to start, and the tests fail, when a pinned version's text no longer matches.

1. Edit `seed/supplier-terms.json` in both languages, keeping the same numbered sections in each. `SeedLoader` refuses a file with a language, the acceptance statement, a heading or a paragraph missing, an empty section, two sections with the same number, or sections numbered differently in the two languages.
2. Set `version` to the new date (`YYYY-MM-DD`).
3. If the clauses that need express acceptance move or change, update the `acceptance` statement so it still names them. The statement is `before` + `link` + `after`; the page makes `link` the link to the full text. `SeedLoaderTests` checks that §6, §7 and §10 are named and that the link text is not empty.
4. Run `node scripts/terms-hash.mjs` and add the line it prints to `seed/supplier-terms-versions.json` (`"<version>": "<sha256>"`). Never change an existing line.
5. Restart the API (`docker compose restart api`). It reads `seed/` once at startup. A supplier page opened before the restart gets `422 TermsNotAccepted` on submit and shows "The terms have been updated" with a retry that loads the new statement and keeps the supplier's answers.
6. Commit with a subject such as `F3: supplier terms 2026-12-01`.

**The fingerprint** is the SHA-256 of the file's canonical JSON:

- object keys are sorted by UTF-16 code unit, with no whitespace;
- strings are escaped as .NET's `System.Text.Json` does by default: `\uXXXX` in upper-case hex for every character outside printable ASCII and for `" & ' + < >` and the backtick;
- the hash covers the whole file, both languages and the version.

Reformatting the file keeps the fingerprint; changing any word changes it. `node scripts/terms-hash.mjs <file>` computes it without .NET, and a test on each side pins the C# and Node implementations to the same result. To check a fingerprint from the audit log, find the version in the history of `seed/supplier-terms-versions.json` (`git log -p`), take `seed/supplier-terms.json` from that commit and run the script on it. A printed or saved web page cannot be hashed this way, because the fingerprint covers the JSON file; the operator provides that file on request.

## Sources

These are the primary texts. The industry practice the terms follow (supplier terms of carbon-accounting platforms and product-footprint disclaimers published by chemical producers, read on 2026-11-06) is not cited by name, under the project's copy rules.

- Romanian Civil Code (Legea 287/2009, republished): art. 1202, 1203, 1245, 1255, 1280, 1295 and following, 1355, 2517. Legea 214/2024 on electronic signatures and trust services, and Legea 193/2000 on unfair terms. Official texts on the Ministry of Justice's portal, [legislatie.just.ro](https://legislatie.just.ro).
- [Legea 365/2002 on electronic commerce, republished](https://legislatie.just.ro/Public/DetaliiDocumentAfis/77218)
- [Regulation (EU) 2016/679 (GDPR)](https://eur-lex.europa.eu/eli/reg/2016/679/oj), [Directive 2000/31/EC on electronic commerce](https://eur-lex.europa.eu/eli/dir/2000/31/oj), [Regulation (EU) 1215/2012 (Brussels I bis)](https://eur-lex.europa.eu/eli/reg/2012/1215/oj)
- Directive (EU) 2026/470 (Omnibus I, the value-chain cap), in force since 18 March 2026. The number comes from secondary reports; confirm it on EUR-Lex before citing it outside the team.
- CJEU, [C-322/14 *El Majdoub*](https://curia.europa.eu/juris/liste.jsf?num=C-322/14), judgment of 21 May 2015 (click-wrap acceptance of a jurisdiction clause)
- CJEU, [C-413/23 P *EDPS v SRB*](https://curia.europa.eu/juris/liste.jsf?num=C-413/23), judgment of 4 September 2025 (when pseudonymised data is personal data)
