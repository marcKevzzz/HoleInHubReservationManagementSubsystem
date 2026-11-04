# Sprint 1 Customer Access and Booking Improvements Implementation Plan

> **For agentic workers:** Use `superpowers:executing-plans` to implement this plan task-by-task in the current checkout. Steps use checkbox syntax for tracking. Preserve existing uncommitted work.

**Goal:** Finish a reliable Sprint 1 customer journey from browsing and account access through verified booking submission and secure reservation retrieval.

**Architecture:** Keep ASP.NET Core MVC Web → Business → MongoDB Data. Reuse account services, guest verification, encrypted drafts, the outbox and guarded reservation transactions. Keep the current visual identity and installed dependencies.

**Tech stack:** .NET 10, ASP.NET Core Identity/MVC, MongoDB, Tailwind v4, local GSAP.

**Sources:** Owner directives in this chat; `Documentation/PROJECT.md`; `Documentation/specs/2026-10-18-production-foundation.md`; revised supplied `GROUP2_PROJECT TIMELINE IPT102 (1).pdf`, page 1. The timeline's indefinite Pending holds are superseded by the owner's 30/60-minute policy.

## Global constraints

- This is Sprint 1 completion, not a new feature sprint. Preserve later-sprint features already implemented; do not extend or remove them.
- Retain guest and verified-account booking, single/multi-bay allocation, the existing whole-shop demo gate, UTC storage and Asia/Manila display.
- The owner now permits 30-minute ordinary sessions. Review lead time remains 30 minutes Regular and 60 minutes other services; fixed classes/rentals keep their specified windows.
- Number of guests includes non-players. Do not fabricate per-bay player counts. Account contact defaults come from the latest owned booking; keep them editable.
- No migrations on startup, secrets in source, public role elevation, new libraries or unrelated refactors.
- No additional profile, transactions, reports, reminders, attendance, rescheduling or payment integration work in these batches. Existing actions remain available.

## Review focus

- Booking selection survives account navigation without storing personal contact data in browser storage.
- Missing, stale or invalid schedules do not trigger unnecessary requests or produce hidden-field validation failures.
- Verification never holds capacity; competing bookings during verification produce a recoverable conflict.
- Account records and guest bearer links never expose another customer's booking.
- Database/email failures neither report false success nor create duplicate bookings on retry.

## Batch 1 — Account access and return to booking

**Primary areas:** `AccountController`, account forms/models, and existing reservation-selection restoration in `reservation.js`.

- [x] Trace and test sign-in, sign-up, forgot/reset password, and account email verification while a customer is booking.
- [x] Carry a validated local return URL through account navigation and successful recovery. Reject external/protocol-relative destinations. Preserve existing generic authentication/reset responses and lockout behavior.
- [x] Reuse the current sessionStorage selection key for service, date, time, duration and resource IDs only; include a 30-minute saved timestamp and discard invalid/stale selections. Do not store names, phone, password or email there.
- [x] Restore selection after same-tab navigation, then refresh availability. If an email link opens a different tab with no saved selection, provide a booking link without claiming that a draft was restored.
- [x] Disable duplicate account form submission while pending; restore controls after recoverable errors/back navigation. Reuse existing UI behavior if present.
- [x] Keep first-time account contact fields blank. Verify latest-owned-reservation prefill and editable values for returning customers.

**Acceptance:** A customer can leave booking to authenticate/recover access and return without losing a valid same-tab selection; external redirects remain blocked. Extend existing account HTTP tests and navigation tests, rather than replacing authentication infrastructure.

## Batch 2 — Reservation form and verification reliability

**Primary areas:** reservation controller/create/verify/confirmation views, `reservation.js`, and shared booking validation.

- [x] Test single-tile 30-minute selection, two-tile one-hour dragging, reverse dragging, and switching Morning/Afternoon/Evening tabs. Keep selected range when tabs change; expose keyboard operation and a visible selected-range summary.
- [x] Guard every availability/quote request against incomplete date, start and duration. Abort stale responses. Disable past/short-lead starts and reject them again on the server.
- [x] Validate whole-shop fixed sessions and all-active-bay selection; show a useful unavailable state when one bay conflicts. Never silently reduce exclusivity to free bays.
- [x] Keep contact values after validation failure. When availability changes during guest verification, retain the encrypted draft and route the customer back to edit their schedule.
- [x] Display the existing verification expiry/resend restrictions clearly. Use server restrictions as authority; wrong, expired and reused codes must not create an allocation.
- [x] Confirm Submit → Verify → Request received. The receipt displays current Pending status/reference and links to secure details; whole-shop proceeds to existing demo checkout separately.

**Acceptance:** Every supported session can be selected/submitted without blank-start 400 requests. A conflict during verification keeps the customer's details recoverable and creates no partial/duplicate allocation. Extend existing JS, policy and guest HTTP tests.

## Batch 3 — Useful My Reservations and booking details

**Primary areas:** owned reservation queries/DTOs and customer index/details views.

- [x] Replace the minimal table with a responsive list showing reference, service, local schedule, total guests and clearly labeled status.
- [x] Provide two views: Upcoming (end is in the future and status is active) and Past (ended or terminal: Rejected, Expired, Cancelled, Completed or No-show). Default to Upcoming. Preserve secure account ownership filtering before categorization.
- [x] Give each view an empty state and a booking call to action. Provide a clear View details link.
- [x] Show review/payment deadline and explanatory Pending copy on details; surface existing rejection/cancellation reasons and history. Do not add customer change workflows.
- [x] Process eligible Pending expiry before returning owned lists, reusing the existing idempotent expiry mechanism so displayed status does not depend solely on the background worker.

**Acceptance:** Customers can find their next booking on mobile and understand its actual state. Cross-account IDs return no data. Existing guest status links still work. Add ownership/list classification tests and view smoke checks.

## Batch 4 — Friendly failures and safe retry

**Primary areas:** Business exception translation, Web error rendering and existing notification handling.

- [x] Translate expected MongoDB connectivity/timeouts at Business boundaries into a typed service-unavailable outcome; keep MongoDB types in Data. Leave unexpected exceptions to the normal error pipeline.
- [x] Return a friendly 503 view for affected pages and a safe `{ message }` response for availability/quote requests. Preserve form input/drafts and provide Retry or Edit details as appropriate.
- [x] Distinguish queued verification email from delivered email. Do not claim a message reached an inbox merely because an outbox write succeeded.
- [x] Preserve durable outbox retry/idempotency. Log a safe failure category/event or correlation ID without credentials, email bodies, bearer tokens or full connection strings.
- [x] Reuse the same idempotency key when retrying a possibly committed submission; never generate a new booking just because the previous response was lost.

**Acceptance:** Simulated database failure shows recovery instructions, SMTP failure leaves queued mail retryable, and retry after a committed request returns the original booking. Use injected failures/local test fixtures; never disrupt shared Atlas to test outages.

## Batch 5 — Sprint 1 acceptance and documentation

- [x] Run the complete guest and verified-account happy paths, single/multi-bay conflicts, 30-minute sessions, fixed whole-shop demo, code expiry/reuse, closed/short-lead times, duplicate submit and ownership rejection.
- [ ] Complete remaining manual acceptance (see evidence below). Inspected desktop and mobile together: all booking steps, guest dialog focus, calendar square selection, time tabs/dragging, contact prefill, confirmation, keyboard navigation and reduced motion. Fix findings once, then perform one confirmation pass.
- [x] Run solution build, non-integration tests, isolated local MongoDB integration tests, npm UI tests and Tailwind build. Record actual results and any blocked check.
- [x] Correct stale onboarding scope statements. Document what Sprint 1 demonstrates, which later features already exist, and unresolved client policies. Do not claim a live POS integration or production launch.

**Acceptance:** A short evidence checklist records actual pass/fail results for the full Sprint 1 demo. No raw database exceptions or unresolved critical customer-flow defects remain.

## Execution order and stop points

Complete batches 1 → 2 → 3 → 4 → 5. Each batch is a separate reviewable change with focused verification before proceeding. Do not create commits or a PR unless requested. Do not bundle a profile feature into contact prefill. Guest lost-link recovery and automatic linking of old guest bookings to new accounts remain follow-up work, not blockers for the existing secure-link flow.

**Not included:** new staff dashboard/approval features, attendance, rescheduling, scheduled reminders/SMS, reports, profile management, transaction history and live payment collection. These follow the later sprint scope in the timeline.

## Execution ledger — 2026-11-04

- Scope checked against the revised timeline PDF. Ruling: retain owner-approved 30/60-minute review deadlines despite the timeline's older indefinite-hold wording; leave existing later-sprint features in place without extending them.
- Batch 1 implemented: local return URLs now travel through all account forms and email links; account forms prevent duplicate submission; browser selection snapshots expire after 30 minutes and contain selection fields only.
- Ruling: reuse the suitable current checkout because it contains the owner's ongoing uncommitted implementation. Do not create a separate worktree or discard those edits.

- Batch 2: retry lookup now uses the existing owner/idempotency fingerprint before availability checks; verification displays server-derived expiry/resend timing. Added conflict/edit/retry HTTP coverage.
- Batch 3 implemented: Upcoming/Past list uses active status plus UTC end time, refreshes eligible Pending expiry and keeps account filtering server-side.

- Batch 4 ruling: classify engine-specific connectivity failures in Data, translate to a typed Business failure and handle them once in a global MVC filter. This preserves the tier boundary without duplicating catches in every query. Recoverable booking forms use encrypted ASP.NET TempData with a 30-minute timestamp and retain the original idempotency key. Passwords are never saved.

## Final verification evidence — 2026-11-04

- Solution Release build with isolated output: passed, zero warnings/errors. This avoids replacing the owner's running app binaries.
- Non-integration tests: 63 unit and 28 Web smoke passed. Password-reset outage recovery retains the original reset credentials and local booking destination, never the entered password.
- Isolated local MongoDB replica-set tests: 30 Data/Business integration and 10 Web integration passed. Coverage includes competing allocation, whole-shop exclusion, idempotency, approval/expiry races, duplicate/late payment events, guest verification conflict/edit recovery and repeated database outage recovery.
- JavaScript UI tests: 43 passed, including 30-minute/two-tile/reverse/cross-noon selection logic, local return-selection expiry, account duplicate-submit/back navigation, verification timing and reduced-motion behavior.
- Tailwind build passed. Impeccable detector reported no findings for the changed history, verification, unavailable view and reservation stylesheet.
- Separate local preview at http://localhost:5288 used the existing disposable booking-preview database. Readiness reported Healthy with all required migrations applied. Existing configured app was preserved; no shared/production migration was applied.
- Browser checks: guest modal initially focuses the heading; mobile/desktop calendar/time tabs and square dates; half-hour session; keyboard progression through bays, contact and review; guest submission reaches verification without capacity; countdown/resend state; local test-account sign-in; Upcoming empty state and Past records with secure details links. Desktop main/summary both measured 782px high. Mobile history had no horizontal overflow. Fixed touching status/details links, confirmed once at desktop/mobile widths. Final mobile capture: `.local/sprint1-mobile.png`.
- Remaining manual acceptance: physical pointer drag across time tiles, full verified guest receipt in the browser, all staff screens, OS reduced-motion browser run and real SMTP inbox delivery were not repeated in this pass. Their relevant behavior is covered by automated tests where stated above; this is not a claim of live email or POS verification. SMTP was intentionally unconfigured in the disposable preview.
- Scope aligned to timeline: no new profile, attendance, reporting, reminders, customer rescheduling or live POS work. Existing later-sprint implementation retained. Provisional review deadlines and production whole-shop/payment policy remain client decisions.

## Essential-process acceptance requested by owner — 2026-11-04

Owner narrowed this pass to necessary process checks, excluding mobile/visual review. See [essential process acceptance](2026-11-04-sprint-1-process-acceptance.md) for the final results and receipt-delivery defect/fix. Core functional checks passed; the historical manual visual checkbox above is intentionally not claimed complete. Actual running app readiness was Healthy. Final checks: 208 .NET tests, 51 JS tests, Release build and Tailwind passed. Real customer inbox delivery and live POS are not claimed.
