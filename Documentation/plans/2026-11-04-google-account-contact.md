# Optional Google access and account contact details

Owner request: optional Google sign-up/sign-in, plus first name, last name and contact number on account creation for booking prefill. Google remains disabled until configured.

1. Add optional persisted contact fields and Google subject through Data → Business → Web; preserve legacy accounts. Add explicit migration 005 for a partial unique Google subject index. No applied migration changes or automatic startup migration.
2. Require validated names/contact on email registration. Prefill bookings from the account, fall back per missing field to the latest owned booking, and retain edits/recovered form values.
3. Use Microsoft's Google OAuth handler with correlation/state and the Identity external cookie. Enable only with explicit flag and both credentials. POST challenge uses antiforgery and local return URL. New Google users complete contact details before creation. Existing matching-email accounts are not automatically linked. Gmail/Workspace verified email can satisfy verification; other Google emails use the existing email-verification flow. Do not save OAuth tokens.
4. Test registration validation/persistence/prefill, legacy fallback, disabled/configured OAuth, callback failure, new/repeated Google login, duplicate email/subject and lockout. Use a fake Google backchannel for local tests, not real credentials.
5. Document configuration and CLI migration; run build, unit/Web smoke, isolated Mongo integration, JS and Tailwind. Inspect account screens at desktop/mobile widths once, fix together, confirm once. No live Google login without owner credentials.

## Verification — 2026-11-04

- Release solution build passed: zero warnings/errors.
- 63 unit, 49 Web smoke/integration and 30 Data/Business integration tests passed (142 .NET total); integration databases were isolated local MongoDB databases.
- 44 JavaScript tests and Tailwind build passed. Impeccable detector reported no findings on changed account views/partials.
- Real Google OAuth handler exercised through a fake local HTTP backchannel: correlation/state, signup completion, provider-subject lookup/repeat sign-in, no auto-linking existing email, lockout, invalid state/missing external cookie, authoritative email classification, required local verification, local return URL, antiforgery, untrusted posted email/roles and duplicate-subject index.
- Contact registration tested through HTTP with required/invalid phone values, persistence and booking prefill; profile fields take precedence over a previous reservation. Legacy contact fallback remains covered by existing booking flows.
- Explicit CLI migration 005 applied successfully under the migration lease to the disposable local booking-preview database only (four earlier migrations unchanged). No shared/Atlas migration applied.
- Separate local preview http://localhost:5290 reported Healthy. Google remained disabled; the button is absent until configured. Desktop/mobile contact form inspected together, spacing compacted once, confirmed once. No horizontal overflow or browser warnings/errors. Mobile capture `.local/account-contact-mobile.png`.
- Live Google consent/login not tested because no OAuth credentials have been configured. Readiness requires migration 005 on each target database; enabling Google requires an exact registered callback URL and credentials as documented in ONBOARDING.md. No account-linking/profile editor added.

- Final independent security review: no blocking OAuth findings. Fixed punctuation-only phone validation (7–15 actual digits) and added real provider-claim mapping, invalid Google contact submission and mixed legacy prefill regressions. Final Web suite: 49 passed; solution rebuild: zero warnings/errors.
