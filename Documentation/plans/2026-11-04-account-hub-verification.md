# Account pages and shared email-code verification — 2026-11-04

## Owner-approved execution
User requested planning followed by implementation without another approval stop. Preserve the current white/forest-green editorial identity, fonts, underlined controls, square selections and local GSAP motion. Prior Sprint1 exclusions are overridden only for the explicitly requested Transactions and My profile pages.

## Design and behavior
1. Shared account navigation: My reservations / Transactions / My profile. Clear page heading, Book a bay action, restrained dividers, no metric-card dashboard or filler paragraphs. Responsive lists stack cleanly on mobile.
2. My reservations: Upcoming/Past views; prominent date and service, bay names, reference, guest count, status and useful pending deadline; secure details links and concise empty states. Reuse ownership queries and expiry processing.
3. Transactions: owned records for existing whole-shop demonstration payments only; distinguish rental total from deposit, payment status and reservation status; show reference/date and link to owned booking. Explicit demo label; no real receipts, settlement, refunds, payment gateway or invented ordinary-session transactions.
4. My profile: account-owned first/last name and contact number editable and validated through existing Identity store. Email read-only, never accepted from posted data. Safe concurrency errors, success feedback and antiforgery. Profile supplies booking defaults; booking-specific edits do not overwrite profile.
5. Shared verification core: guest and account have distinct browser-bound scopes and components, with one shared implementation for code hashing, ten-minute expiry, five attempts, one-minute resend throttle and durable encrypted outbox. Existing guest allocation semantics remain unchanged. Auth signup supplies the email once; no email entry on verification. Account verification uses a separate protected context and code form, and retains legacy email links for compatibility. Verified account booking skips OTP and locks email while allowing contact edits.
6. Remove redundant instructions from contact/review/verification/auth screens; keep necessary deadlines, demo disclosure and error/recovery text. Preserve non-JS paths and keyboard focus.

## Configuration
Google credentials have been saved by the owner in Development user secrets. Inspect presence only, never print secrets. Verify a local challenge uses localhost:5275/signin-google. Authorized Google callback URIs require /signin-google, not just the two site roots. Deployment credentials and Google Cloud allow-list changes cannot be inferred from local settings; do not publish or disclose credentials. Preserve existing configured app launch settings.

## Work and verification
- Account hub implementer owns new account hub controller/models/views/CSS, reservations index view, shared account navigation/menu links, and related tests/payment DTO exposure. Root owns auth verification core/controllers/views and booking copy/email protections.
- Add regression tests for no repeated email input, isolated verification scopes, wrong/reused/expired codes, resend throttling, profile ownership/immutable email/prefill and owned transaction history.
- Run solution build, unit/Web smoke, isolated Mongo integration, JS/Tailwind. Inspect all changed pages together at desktop/mobile widths, fix once and confirm once. No real transactions or deployment. Final independent review.

## Final evidence — 2026-11-04

- Implemented the plan without another approval stop. New account navigation, redesigned reservations, owned demo payment history and current-user-only profile editing are active.
- Shared email-code service now backs separate account and guest components. Account signup/sign-in retains email in a protected context; verified booking skips OTP and displays immutable account email. Legacy verification links remain supported.
- Release solution build: passed, zero warnings/errors. Tests: 63 unit + 56 Web smoke/integration + 30 isolated Data/Business integration = 149 .NET passed; 45 JavaScript tests passed. Tailwind build passed. Final Impeccable detector reported no remaining findings after removing the feedback accent border.
- Independent security review: no blocking security issue found. Fixed authenticated signup creating a second account/context mismatch by redirecting before signup; added coverage. Added forged booking-email persistence rejection, booking contact edits not mutating profile, and legacy expired/valid confirmation-link HTTP regressions.
- Desktop/mobile reviewed in a batched pass: all three account pages, profile save feedback, account verification, guest verification, readonly booking email and concise review/receipt. Fixed shared guest-code label placement, profile validation associations/native phone pattern, irrelevant whole-shop review copy and receipt duplication; confirmed once. No mobile horizontal overflow.
- Local browser + actual SMTP-to-Mailpit verification passed: account code delivered, email confirmed, account automatically signed in, booking contact defaults editable except email, account booking submitted directly to Pending. Its held bay became unavailable in the following guest flow. Guest code delivered separately, no repeated email input, verification created a second Pending request with secure status link. Account/guest purpose-isolation and expiry/reuse/throttle tests also passed.
- Google credentials presence verified privately. Live challenge reaches Google but returns `redirect_uri_mismatch`; required provider callbacks are http://localhost:5275/signin-google and https://holeinhub.runasp.net/signin-google under Authorized redirect URIs. Local secrets do not configure hosting. This external Google Cloud setting remains for the owner; no live Google login claimed.
- Review app is running on http://localhost:5275 using disposable local database hih_booking_preview_31576860433b454cb8508fb68dc9fb3a and Mailpit http://localhost:8025. Readiness Healthy, migrations through 005 already applied there. Actual Atlas and hosted site were not changed. Local restart configuration is recorded in ignored .local/account-hub-start.ps1 and process metadata in .local/account-hub-preview.json.
- Old agent previews with exact verified build paths were stopped after an initial safety review rejected an overly broad PID match; the user's configured app was not stopped. This resolved competing outbox workers and local mail delivery.
- Final desktop capture: .local/account-hub-desktop.png. No real payment collection, receipts, refunds, profile email change or deployment added.

## Owner refinement — 2026-11-04

- Accept the authenticated Google verified-email claim for all valid provider emails, without a second venue code step. Reject unverified Google email claims. Existing linked, unconfirmed accounts become confirmed only after a successful Google login for the matching email; never auto-link unrelated same-email accounts.
- Successful account-code verification signs in and redirects directly home, removing the intermediate confirmation step. Guest verification still submits Pending and shows its booking receipt.
- Centered resend/countdown in the shared code component; removed the sign-in button icon via existing data-no-icon support.
- Temporarily hide standalone /availability and /Reservations/Availability (404) and its menu link. Keep booking CheckAvailability endpoint and scheduling rules active.
- Build zero warnings/errors; 63 unit, 30 Data/Business integration, 56 Web and 45 JS tests passed. Desktop/mobile visual check confirmed centered controls and zero sign-in icon nodes. Temporary isolated preview stopped; owner app not restarted or changed. User must restart their running app to load server/view changes. Live Google login not repeated.

## Google v3 mapping correction — 2026-11-04

Diagnosed live sign-in rejection after requiring verified provider email. Installed Microsoft Google handler uses /oauth2/v3/userinfo, but our added claim action and fake backchannel used the older verified_email name. Changed the action to email_verified and updated the fake provider to the actual v3 sub/email_verified schema. Three positive OAuth integration cases reproduced failure before the fix; all 56 Web cases pass after it, including missing/false-claim rejection. Solution build zero warnings/errors. Owner app is left running with its existing launch environment; restart required to load the changed startup claim mapping. No credentials or provider configuration changed.

## Local booking policy and copy — 2026-11-04

The normal Development launch had no ReservationPolicy approval/coverage/recipient settings; only old agent previews had process-scoped values. Saved the owner-directed provisional policy through Web user-secrets (all review days, full-day coverage with existing venue-hours validation still enforced, existing 30/60-minute deadlines, development payment preview enabled). Google/database secrets preserved; no production settings changed. StaffEmails uses the documented development placeholder staff@example.test and needs a real venue recipient before deployment. The running app must restart to reload IOptions and compiled views.

Removed visible demo labels from customer/staff booking, deposit and Transactions copy; retained concise simulated-checkout/no-money disclosure, production payment gating, stored audit history and identifiers. Shared history formatting presents legacy demo payment events as payment statuses without rewriting records. Removed booking modal sign-in arrow using existing data-no-icon support plus current-compiled-markup CSS.

Verification: solution build zero warnings/errors; 63 unit +57 Web smoke/integration +30 isolated Data/Business integration tests passed, plus45 JS tests and Tailwind. No shared booking submitted or production setting modified during this change. Manual check of the owner's restarted app remains pending.

## Booking receipt, verification and status refinement — 2026-11-04

Restyled the request receipt and secure reservation details using the My Reservations typography, forest-green date column, fine rules, grouped facts, responsive status/actions sidebar and history. Preserved token-bearing status/checkout/cancellation routes, anti-forgery, Pending semantics, coaching/legacy participants, cancellation cutoff and simulation disclosures. Renamed the summary contact row to Guest name.

Guest verification now uses a centered standalone layout without the main navigation/footer. The shared code input, autocomplete, native POST, expiry and resend controls remain intact; only the guest wrapper receives the new visual treatment. General submission errors and restored-request feedback open Review; MVC field errors route to their own step. Native/no-JavaScript markup remains accessible.

Verification: solution build zero warnings/errors; 63 unit, 37 web smoke, 22 web integration and 30 isolated Data/Business integration tests passed (152 total), plus 46 JavaScript tests and Tailwind. Added server feedback-key coverage and verification-shell assertions. Independent scoped review found no actionable regressions. Batched desktop/mobile inspection covered guest verification, receipt and status, followed by one spacing correction and confirmation. Preview used the disposable local MongoDB database on port 5293 with Mailpit; readiness Healthy. Owner's running app and Atlas data were not changed. Restart the owner's Web process to load compiled Razor changes.
