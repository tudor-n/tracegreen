# Security

What TraceGreen protects, how, and what it does not cover yet. Control IDs (`C1`–`C9`) are the roadmap's; the reasons behind each choice are in `docs/DECISIONS.md`.

The demo is single-tenant and runs on a laptop that serves its own Wi-Fi hotspot (`RUNBOOK.md`). The buyer dashboard has no login. The one route an outsider is meant to reach is the supplier page behind a signed link, so most of the controls below protect that link and what it can change.

## Supplier links (C1–C3, C9)

A supplier confirms a node through a link `<baseUrl>/verify/<nodeId>?t=<token>`, shown as a QR code on the buyer's link card. `POST /api/request-link` creates it. The token exists only inside that URL: it is not stored, logged or written to the audit log.

**Layout** (`Domain/Security/HmacLinkSigner.cs`; final per ruling R5):

```
v1.<nodeId>.<expiresUnix>.<nonce>.<invoiceHash>.<sig>

sig        = base64url( HMAC-SHA256( LINK_SECRET, "<nodeId>|<expiresUnix>|<nonce>|<invoiceHash>" ) )
nodeId     = the node's UUID
expiresUnix= expiry in Unix seconds; LINK_TTL_SECONDS after issue (default 604800, 7 days)
nonce      = base64url of 16 random bytes
invoiceHash= SHA-256 (hex) of the canonical JSON of the invoice's extracted fields
```

The signature is compared in constant time (`CryptographicOperations.FixedTimeEquals`) and must be exactly 32 bytes.

**Checks, in this order** (the first that fails decides the answer):

| Check | Fails as | Code |
|---|---|---|
| Format: six parts, `v1` prefix, base64url where expected | 401 | `InvalidToken` |
| Signature | 401 | `InvalidToken` |
| Expiry | 410 | `LinkExpired` |
| Invoice hash, recomputed from the stored extract | 410 | `InvoiceChanged` |
| Single use: the nonce is not yet stored | 409 | `AlreadyUsed` |
| More than `RATE_LIMIT_PER_MIN` calls a minute from one address | 429 | `RateLimited` |

The node id is read from the token before the signature is checked, unauthenticated, only to load the invoice hash to compare. A forged token naming an unknown node still fails its signature (401); a genuine link to a node a reset removed is 410.

**Single use** is enforced by the repository, not the signer: the verification row's nonce is unique (a unique index in Postgres, a check under the lock in memory). Five concurrent submits of one link give one 200 and four 409. A used link also answers 409 on `GET /api/verify/context`, so scanning it again shows "already used" before any tap.

**Binding to the invoice** (C2): the invoice hash in the token is compared with one recomputed from the stored extract, so a link issued for one version of an invoice cannot confirm another.

## Supplier page (C7)

`/verify/:nodeId` asks two questions (transport mode, energy source) and an optional mass. It sets no cookies and has no login, email field, camera or clipboard access. The context it receives holds the supplier and buyer names, the invoice number and date, the dominant line's description, the lane, the answer options and the Supplier Terms' checkbox statement with their version and SHA-256. It holds nothing personal, no nonce and no address.

Before submitting, the supplier ticks the Supplier Terms (`seed/supplier-terms.json`; reasoning in `docs/legal/README.md`). The submit carries the terms' SHA-256 from the context, and anything else is 422 `TermsNotAccepted`. The version and SHA-256 are stored on the verification row and in its audit entry. The printable copy at `/terms/supplier` (`GET /api/terms/supplier`) is public text: no token, no rate limit, nothing personal. The supplier page links to it in a new tab, so a printout never carries the link's token.

All text from an invoice is untrusted. React renders it as text, and `dangerouslySetInnerHTML` is a lint error (`react/no-danger` in oxlint).

## Supplier page links (F12)

The supplier page `/supplier?t=` lists every delivery on the graph from one supplier and lets the supplier confirm each of them. Its link is a second kind of token, signed by `PortalLinkSigner`:

- **Layout:** `p1.<anchorNodeId>.<expiresUnix>.<sig>`, where `sig = base64url(HMAC-SHA256(key, "p1|anchorNodeId|expires"))`. The key is `"portal:" + LINK_SECRET`, derived the way the address-hash key is, so a `v1.` verification token never passes as a `p1.` token or the other way round (tested both ways).
- **Reusable until it expires** (`LINK_TTL_SECONDS`, 7 days by default), because it opens a list rather than one answer. Each answer sent from the page mints a fresh single-use `v1.` link for that delivery and submits it through `VerifyService`. Single use, the invoice-hash check, the verification row and the audit entries (`link.requested` by actor `portal`, then `verification.submitted`) are therefore those of the phone page.
- **No overwrite:** an answer for a delivery that is already VERIFIED is 409 `AlreadyConfirmed`, so a forwarded link cannot change supplier-confirmed data. The page also passes the delivery's status and answers as it read them, and the repository compares them under the same lock or transaction as the write (`NodeAnswerState`, both adapters). Of several answers sent at once, one lands and the others get 409 `DeliveryChanged` with nothing written (tested with 8 concurrent answers over HTTP and 10 against each repository).
- **Answers only accumulate:** a question skipped on this page keeps the answer the delivery already has, so a PARTIAL delivery cannot lose its confirmed half through the reusable link. The phone page is unchanged: each of its submits is a whole answer set.
- **Supplier Terms:** the page asks for them as the phone page does, with one unticked checkbox above the list (the same statement and link to `/terms/supplier`, which opens in a new tab without the token). The supplier ticks it once per page visit; every answer carries the terms' SHA-256 from the view, anything else is 422 `TermsNotAccepted` before a link is minted, and the verification row and its audit entry record the version and SHA-256. The page never ticks the box, and a reloaded page or a list with other terms starts unticked.
- **Scope:** "the supplier" is every node in the anchor node's workspace whose invoice carries the anchor invoice's seller tax id (compared within its country, or by name when an invoice has no tax id). Another supplier's delivery answers 404 `NodeNotFound`, exactly like one that does not exist.
- **The risk it leaves:** whoever holds the link can answer that supplier's open deliveries until it expires, just as whoever holds a phone link can answer its one delivery. The expiry, the 409, the audit trail of every answer and the rate limit below bound it.
- **Where the token travels:** in request bodies (`POST /api/portal/view`, `POST /api/portal/answer`), so no API URL carries it. The page URL carries it the way `/verify` does, and nginx logs `$uri` alone. `portal.requested` names the node and the expiry, never the token. A test checks that the signature reaches no log line, response body or audit payload.
- The page follows C7: no cookies, login, email field, camera, clipboard or personal data.

| Check | Status | `code` |
|---|---|---|
| Format or signature | 401 | `InvalidToken` |
| Expiry | 410 | `LinkExpired` |
| The anchor delivery no longer exists (a reset removed it) | 410 | `InvoiceChanged` |
| A delivery that is not this supplier's | 404 | `NodeNotFound` |
| A delivery that is already VERIFIED | 409 | `AlreadyConfirmed` |
| Another answer to the same delivery landed between the page's read and this write | 409 | `DeliveryChanged` |
| The answer does not carry the current Supplier Terms' SHA-256 | 422 | `TermsNotAccepted` |
| More than 3 × `RATE_LIMIT_PER_MIN` calls a minute from one address | 429 | `RateLimited` |

## Rate limiting and forwarded headers (C5)

- `GET /api/verify/context` and `POST /api/verify` share ASP.NET Core's fixed-window limiter: `RATE_LIMIT_PER_MIN` (default 10) per client address per minute. The 11th call gets 429 `RateLimited`.
- `POST /api/portal/view` and `POST /api/portal/answer` (F12) have a limiter of their own, three times `RATE_LIMIT_PER_MIN` per client address per minute. The page opens with one call and spends one per confirmed delivery (an answer returns the fresh list). It never shares a counter with the verify routes, so a supplier going through a list does not use up the phone page's attempts from the same address.
- The client address is the TCP peer, unless the peer is in `TRUSTED_PROXIES` (default `127.0.0.0/8,::1/128,172.16.0.0/12`: Vite on the host, Vite or nginx in Docker). Only then is `X-Forwarded-For` read, with `ForwardLimit = 1`, so a phone on the hotspot cannot choose its own bucket.
- Only the address is taken from forwarded headers. `BaseUrlResolver` reads `PUBLIC_BASE_URL`, then `X-Forwarded-Proto` and `X-Forwarded-Host` (last value wins), then the request origin, and flags loopback. Its result is a display hint for the link card. A direct caller can choose those headers, so it is never trusted and never written to the audit log.

## Input handling

- **UBL import** (`Domain/Ubl/UblInvoiceParser.cs`): DTDs are prohibited, which rules out entity expansion and external entities. The parser reads at most 2 MB of characters, and nginx caps request bodies at 4 MB. A streaming pass rejects nesting deeper than 64 levels before any tree is built.
- **Numeric bounds**: a total that is not positive or is above 10¹² EUR, a line amount above 10¹², or a quantity that is negative or above 10⁹ is 422 `Schema`. This keeps every later sum inside `decimal`, so one hostile invoice cannot break `/api/graph` for everyone.
- **Duplicates** are refused under the repository lock (raw XML hash, invoice id or node id).
- **Answers**: `POST /api/verify` rejects a mode the lane lacks and a mass outside (0, 10⁶] t (422 `InvalidAnswer`).

## The AI boundary

- The AI service classifies line items and never produces a carbon number.
- Invoice text reaches it as delimited data, never as instructions. `seed/invoices/adversarial.xml` carries a prompt-injection line to prove it.
- A category outside the allow-list (`seed/categories.json`) becomes `UNCLASSIFIED`.
- The API calls the service with a 10 s timeout (`AI_TIMEOUT_MS`) for classification and suggestions, and falls back to the cache and stub, so an import never blocks on it. The chat (F13) and PDF extraction (F9) have their own longer budgets (`AI_CHAT_TIMEOUT_MS`, `AI_EXTRACT_TIMEOUT_MS`) on a separate client; no import waits on them.
- **Suggestions (F11)** pick among the node's curated alternatives only, under short ids, and the API sends no figure. A reason holding a digit or a claim word is dropped on both sides.
- **Prompt guard (F6).** The system prompt lists only the allowed category ids and labels, and states that the text inside the `<invoice_lines>` block is data, never instructions. Each description is capped at 500 characters with `<` and `>` stripped and whitespace runs collapsed, so a line cannot close the block; codes are capped at 64 characters. Quantity, unit code, classification code and its scheme go along as context. The request carries no supplier name, tax id or amount. The model sees short ids of the service's own (`1`, `2`, …), never the invoice's line ids, and the messages are built directly rather than through a prompt template, so braces in invoice text are never read as template fields.
- **The guard lowers the risk; it does not remove it.** In scoring, the local model followed an injected instruction in one of 20 tries (`docs/ai-eval.md`). The allow-list bounds what such a line can do: one wrong category, never a number or text outside the schema.
- **Model output is untrusted.** The structured output limits `categoryId` to the allow-list. Only line ids that were sent count, and only the first answer per id. Confidence is clamped to 0–1, the rationale is capped at 200 characters, and an answer below 0.6 confidence becomes `UNCLASSIFIED`. The C# side checks the allow-list again.
- **The service's own budget is 8 s** (`AI_TIMEOUT_S`): at most 4 s per cloud model and 6 s for Ollama, each call in a worker thread with a hard deadline, so a hanging provider cannot hold an import.
- **Where invoice text goes.** With a cloud model in `AI_MODELS`, the line descriptions, quantities, unit codes and classification codes the API sends are passed on to that provider. With only the cache, Ollama and the stub, nothing leaves the laptop.
- **The scoring route is off by default.** `POST /classify?model=<id>` exists only with `AI_DEV_ROUTES=1` and skips the cache; keep it off for the demo. Under Compose the `ai` service publishes no port, so only the containers on the Compose network reach it, not the hotspot.

## The data chat (F13)

- **The model writes the sentence; the API fills in every value.** `POST /api/chat` builds the facts from the dashboard snapshot (`Domain/Chat/FactPack.cs`). The model may cite them only as `{{id}}` placeholders, and the reader sees the values the API fills in from its own data, so no figure and no name on screen comes from a model.
- **`AnswerCheck` refuses an answer when:**
  - it cites an unknown fact;
  - it has a digit outside a placeholder (the unit's own `CO2e` excepted);
  - it writes out a supplier, buyer or place name of the pack;
  - it has a company suffix (SRL, GmbH, Ltd …: an invented supplier);
  - it has a banned term;
  - it is over 600 characters;
  - it cites no fact.

  The check also tests the claims a sentence makes in its own words. A status word must be the status of every supplier the sentence cites; "largest" must cite S1 and "smallest" the last supplier; "more than" and "less than" must follow the ranks. A refused answer reads "I couldn't answer that from the data"; it is never shown. The AI service mirrors the cheap checks, so a model that fails them hands over to the next one.
- **What it cannot catch.** A sentence can still cite the right kind of fact for an unintended reading, for example a supplier's spend where emissions were asked. Every value is a chip that says what it is and whose, and the answer's Sources list repeats that, so a reader can check.
- **Facts, questions and history are data.** They go in `<facts>`, `<question>` and `<history>` blocks through `clean()`. History holds the last three typed questions, never an answer. Supplier names come from invoices and can carry an injected instruction; the worst such a line can do is make the model answer badly, which the check refuses. Invoice line descriptions are not in the facts.
- **New data flow.** The chat sends supplier names, emissions, spend and statuses to the models in `AI_MODELS`, which `/classify` never does. Set `AI_CHAT_MODELS` to the local model to keep them on the laptop.
- **Read-only and rate limited.** Nothing is stored or audited. The policy `chat` allows 3 × `RATE_LIMIT_PER_MIN` a minute per address, and a question is at most 300 characters. The log names the rule that refused an answer, never the answer's text.

## PDF invoices (F9)

- **A PDF is hostile input.** The API refuses anything over 3 MB or without the `%PDF-` header before the AI service sees it. The AI service reads the text layer with pypdf:
  - at most 10 pages and 20,000 characters;
  - encrypted files refused;
  - parsing in a worker with a 5 s deadline, so a crafted file cannot hold it;
  - a parse error is a 422, never a crash.

  There is no OCR: a scan is refused with "no text layer".
- **The model copies; a person decides.** The model fills a fixed schema with strings: unit codes and code schemes from fixed lists, the text in an `<invoice_text>` block through `clean()`. The draft is held on the server, in memory, under a random id: 30 minutes, at most 32, gone on a restart. The person sees every field labelled "AI-extracted · check fields", with the fields the PDF's text does not show marked, and nothing is imported until they confirm.
- **The confirm is validated like any invoice.** The fields are checked (country, ISO date, currency, positive amounts, units, a code only with its scheme). The customer is the workspace's buyer, never the PDF's. Then the UBL import's own checks run (range, EUR only, lane, duplicates by the PDF's SHA-256). The audit entry records `documentKind: pdf`, the PDF's SHA-256, the model that extracted it, and the fields the person changed, compared on the server with the draft it holds.
- **Known gap.** The check for "same seller tax id and number already imported" (the same invoice as UBL and as PDF) scans outside the repository's lock, so two confirms at the same instant could both pass it.
- **Where the text goes.** With a cloud model in `AI_MODELS`, the PDF's whole text goes to that provider, including the buyer's name and any personal data printed on the invoice. Keep `AI_MODELS` local to keep it on the laptop.

## Data at rest (C4, C8)

- **Row-level security** is enabled on `invoices`, `nodes`, `verifications`, `scenarios` and `audit_log` (migration `Rls`). The application connects as the table owner and bypasses it. Any other role, such as a hosted anon role, sees nothing until policies exist.
- **Append-only audit log**: a trigger rejects every `UPDATE` and `DELETE` on `audit_log`. The demo reset uses `TRUNCATE`, which row triggers do not see; the demo is single-tenant.
- **Hash chain** (`Domain/Audit/AuditChain.cs`): each entry's hash is `SHA-256(prevHash + canonicalJson({ts, actor, action, payload}))`, chained from a genesis hash of 64 zeros, with timestamps normalised to UTC milliseconds. Editing, reordering or deleting an entry breaks every later hash. `POST /api/audit/verify` recomputes the chain and returns `{ valid, length, brokenAt }`, and the audit panel shows the result. Postgres also keeps `(workspace_id, prev_hash)` unique, so two writers cannot fork the chain.
- **Audit payloads** hold ids, the expiry, the answers, the warnings and the accepted terms' version and SHA-256 (with `simulated: true` when the demo tool answered). They hold no token, nonce or address.
- **Client address**: a verification row stores `Hashing.IpHash`, keyed with a key derived from `LINK_SECRET` (`"ip-hash:" + secret`), never the address itself.

## Browser headers (C6)

The demo image's nginx (`web/nginx.conf`) sends on every response:

```
Content-Security-Policy: default-src 'self'; img-src 'self' data:; style-src 'self' 'unsafe-inline'; font-src 'self' data:; connect-src 'self'; frame-ancestors 'none'; base-uri 'self'
X-Content-Type-Options: nosniff
Referrer-Policy: no-referrer
```

`'unsafe-inline'` is for styles only, because React Flow positions nodes with inline styles. Scripts load from the origin alone. `no-referrer` keeps a supplier link's token out of the `Referer` header of anything the page loads. The Vite dev server (`docker compose up`) sends none of these headers; use the demo image for anything shown to others.

## Secrets (C8)

- **`LINK_SECRET`** signs links and keys the address hash. It must be at least 32 characters, and the API refuses to start without it in Production. Outside Production it falls back to a fixed development secret, so links from a development stack are forgeable by anyone who reads the repo.
- **Secrets live only in the server's environment.** No secret reaches the SPA bundle, the API answers or the logs.
- `.env` and `.env.*.local` are never committed. `.env.demo` is committed on purpose and holds demo-only values, including its own `LINK_SECRET`. Anyone with the repository can therefore forge links for the demo image, so it must not be reused anywhere real; a real deployment sets its own secret.
- **Optional AI keys:** `ANTHROPIC_API_KEY`, `OPENAI_API_KEY` and `GOOGLE_API_KEY`, with the model list `AI_MODELS` (or the older `AI_MODEL`), are read only by the AI service. They live in the root `.env`, which is git-ignored and which a git worktree does not get. Compose passes them to the `ai` container alone, never to `api` or `web`. `/health.models` reports a missing key by name (`"OPENAI_API_KEY not set"`), never a value.

## Logging

What is logged: startup (data mode, seed directory and counts, migrations); framework warnings and errors; EF Core's SQL commands, with parameter values hidden (sensitive-data logging is off); and nginx access lines in the `tokenless` format (address, time, method, path, status, size).

What is never logged:
- **Link tokens.** ASP.NET Core's request-line logger (`Microsoft.AspNetCore.Hosting.Diagnostics`) is raised to Warning, because its line carries `?t=`. A test collects every log line of a verify flow and checks that no token appears.
- **Query strings and referrers in nginx.** The access log writes `$uri`, not `$request`. `/api/verify/context` has its own location whose error log keeps only critical lines, because an upstream failure would write the whole request line.
- **Nonces, client addresses and invoice text** are not logged, and not written to the audit log.

## Known gaps

- **The buyer dashboard has no authentication.** Anyone who reaches the laptop's address can import, reset (with `DEMO_TOOLS=1`) and request links. The demo relies on the hotspot being the only network. `workspace_id` and `Ctx` exist so a login can become a policy later.
- **The Vite dev server prints a token on proxy errors.** It writes the full URL (`/api/verify/context?t=…`) when the API is down. The demo image uses nginx instead.
- **Links work only on the hotspot (ruling R6).** A phone that is not on the laptop's hotspot cannot open one. The link card warns when the base URL is a loopback address.
- **The supplier page groups deliveries by the seller tax id on the invoices.** Two sellers whose invoices carried the same tax id would share one page. The tax id comes from the e-invoice, which ANAF validates for Romanian sellers.
- **Row-level security has no policies yet.** It blocks other roles entirely, but it does not separate workspaces for the application itself.
