# After the hackathon

Ideas kept out of the 24 hours (roadmap R12: new ideas go here, not into the build). Nothing here is planned work yet.

## Supplier accounts (after F12)

The supplier page (F12) is a signed link with no account. What would come next, in order:

1. **Invite-only accounts.** An account starts only from a buyer's invite, or from the `/verify` thank-you screen ("Keep one link to all your deliveries"), so the buyer's request vouches for which company the person answers for. There is no public sign-up page.
2. **Sign-in by an emailed code, no password.** Nothing to remember or reset. This needs an email-sending service, which is a new dependency.
3. **A supplier organisation keyed by its tax id (CUI),** with one inbox across every buyer that uses TraceGreen, instead of one page per buyer.
4. **A cookie session scoped to `/supplier`** (`HttpOnly`, `Secure`, `SameSite=Strict`), and row-level security policies per workspace and supplier.
5. **Personal data kept to the email address,** which the supplier can delete.
6. **A buyer setting, "supplier account required".** The link then adds one email-code step inside the same page, never a separate sign-up form.

## More supplier inputs, within the EU value-chain cap

Only what a supplier already knows and a factor row can use:

- Saved company defaults (usual transport per route, power source), pre-filling every request.
- Shipment details: the e-Transport UIT code, dispatch and delivery dates (which also feeds the `SEA_TOO_FAST` rule).
- Per-product weight and material.
- Uploaded documents, such as renewable-energy certificates (guarantees of origin), and PDF invoices (F9).
