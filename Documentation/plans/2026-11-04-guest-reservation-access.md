# Guest Reservation Access Implementation Plan

> **For agentic workers:** Use superpowers:executing-plans to implement this plan task by task. Steps use checkboxes for tracking. Owner authorized implementation with “proceed now”. Preserve unrelated uncommitted work and use the current checkout.

**Goal:** Let returning guests find their reservations through verified email without creating an account or keeping the original booking link.

**Architecture:** Preserve per-reservation secure email links. Add email-code recovery that grants a short-lived guest access session, restricted to guest reservations made using that email. Reuse MVC, the shared email-code component, outbox, email verification service and existing reservation detail presentation. Do not turn guest access into account sign-in.

**Design authority:** Current My Reservations, the centered guest verification page and the recently updated receipt/details. Preserve white surfaces, forest green accents, Oswald headings, Inter body text, square fields, thin rules and existing local motion conventions.

**Spec:** The experience and security decisions below form the design brief for this plan. The owner accepted the proposed session duration and entry points by directing implementation.

## Experience

1. Existing reservation emails and bookmarked status links continue to open that one reservation directly.
2. Add **Find my booking** beneath Sign in/Create account in the anonymous account menu, beneath the sign-in form, and as a quiet link on the booking receipt. Avoid adding another main navigation item or modal.
3. `/guest/reservations/find` opens a centered page: logo, back link, **FIND YOUR BOOKING**, one floating-label email input and **Send code**. Supporting copy: “Use the email from your reservation.” No account password or booking reference is required.
4. `/guest/reservations/verify` shows the email already entered, the same underlined floating-label code input as sign-in/guest booking, **View reservations**, centered resend/countdown and **Change email**. No navigation/footer on either recovery screen. Separate wrapper and purpose; shared code-entry functionality.
5. Successful verification goes directly to `/guest/reservations`. There is no extra success/Continue screen and no account is created.
6. Guest history uses the My Reservations layout with **YOUR RESERVATIONS**, Upcoming/Past tabs, large date column, service, times, bays, guest count, reference, status and **View details**. Show the verified email and a quiet **Leave guest access** action. Do not show account-only Transactions/Profile navigation.
7. Each detail link uses session authorization, with no raw access token in the history URL. Reuse the current reservation details view and all its actions. Back link returns to guest history. Existing cancellation rules and payment restrictions remain unchanged.

### Layout and states

- Desktop recovery forms remain centered and narrow, using the existing verification layout. History/detail pages use the normal site shell and existing account-hub spacing.
- Mobile uses the existing date/service row layout, stacked status/actions, comfortable tap targets and no horizontal overflow. Do not introduce a new card system, rounded inputs, gradients or dashboard widgets.
- Keep error messages beside the relevant form, preserving email and code context. Do not send guests back to booking Contact details.
- Invalid code: “This code is incorrect or expired.” Explain the resend recovery when locked out. Expired challenge retains its server-bound email and permits resend after the cooldown.
- After verified access, an empty result says “No guest reservations found for this email.” Offer **Use another email** and **Book a bay**. Do not expose this result before verification.
- When access expires, request a fresh code and preserve a safe local return path to the requested guest detail. Never render cached private details after expiry.
- Upcoming/Past classification matches My Reservations, including cancelled, rejected and expired requests. Show all supported service types and existing legacy guest totals.
- Use server-rendered POST forms, antiforgery, meaningful labels, visible keyboard focus and reduced-motion behavior. Email paste and one-time-code autofill must work without JavaScript.

## Access rules

- Email ownership grants access only to guest reservations using that email. A matching email alone never grants access to account-owned bookings, staff screens or another customer's records.
- Use the exact reservation-level predicate: `AccountId` starts with `guest:`, stored `EmailVerificationId` is non-empty (BSON `GuestSessionHash`), and the stored email matches the verified recovery email. The persisted marker records the original verified submission; never join to the short-lived verification record, which TTL cleanup removes. Do not rely on missing `CustomerAccountId`, because older account records may also lack that field. Historical records without this durable guest marker remain accessible by their existing secure link; email recovery must not infer ownership for them.
- Keep existing token access as a separate one-reservation capability. A booking status link must never unlock the email's full reservation history.
- Recovery challenge namespace: `guest-recovery:` plus a cryptographically random browser challenge ID. Persist explicit `GuestRecovery` purpose. Booking, account verification and recovery codes cannot be interchanged or reused.
- Reuse the existing ten-minute code expiry and five-attempt cap. Proposed recovery issuance limits: one code per normalized email per 60 seconds and five per rolling hour, across browser sessions; ten issuance requests per IP per ten minutes. Apply these to both initial Find POST and resend POST. Limit Verify POST to 30 attempts per IP per ten minutes as well as the five-attempt challenge cap. Return a generic retry response when throttled, independent of booking existence. Persist email counters in MongoDB and atomically reserve the email quota, replace the challenge and enqueue the outbox message in one transaction. Failed issuance must not consume quota; parallel browser challenges must not exceed it. Use the existing request limiter for IP protection and document its single-instance scope if hosting expands.
- Send the same code email and neutral response for every syntactically valid email, even when it has no guest bookings. Never reveal booking counts, references or existence before verification. Do not log codes, cookies, tokens or complete email contents.
- Successful verification must atomically consume its proof before issuing access. Verify/consume/issue is purpose- and email-bound; simultaneous verify attempts cannot create multiple successful redemptions. Issuing a replacement code invalidates the previous code.
- Recommended guest session: a Data Protection-protected, HttpOnly, SameSite=Lax cookie containing normalized email, issue time and an absolute 30-minute expiry, with a dedicated protection purpose. Secure in production; the existing local HTTP exception applies only in Development. Validate expiry and scope on every request and POST. This does not create an Identity account session or require a new session collection.
- Leave guest access clears both recovery/access cookies. It does not sign out an existing account, cancel reservations or revoke original reservation email links. Do not promise revocation of a stolen cookie before its absolute expiry.
- Signed-in customers keep normal account ownership access. Guest recovery remains an explicitly separate flow and never silently merges bookings into an account.
- Private responses use no-store and no-referrer. All detail, cancellation and checkout handlers must use the same authorization decision on the server; hiding links is insufficient.

## Implementation sequence

### 1. Add scoped guest queries and an explicit migration

Files: `HoleInHub.Data/Reservations/ReservationPersistence.cs`, `HoleInHub.Data/Reservations/BookingPersistence.cs`, new next-numbered migration (currently `007_GuestReservationAccess.cs`), migration registration/readiness and `tests/HoleInHub.Tests.Integration/Reservations/GuestAccessTests.cs`.

- [x] Write integration tests for exact guest-email ownership, another email, account-owned records with the same email, durable guest provenance after verification TTL cleanup, legacy records without provenance and paging boundaries. Confirm failures before implementation.
- [x] Add narrow guest list/detail operations with normalized-email matching and verified guest provenance. Use indexed email matching with a documented case-insensitive collation for historical casing; preserve the stored contact email. Stable ordering is start timestamp then ID, with 25 rows per page. Check Upcoming/Past counts and paging independently.
- [x] Add an email/date/ID index using the same collation, recovery email-throttling persistence/index, and extend the verification schema's allowed Purpose values with GuestRecovery. Inspect the current migration inventory before choosing a number; do not edit applied 006 or earlier migrations.
- [x] Apply only through the Migrator CLI against isolated local MongoDB, and verify migration reruns and schema compatibility. Do not modify resource IDs, allocation guards, booking ownership or existing deadlines.

### 2. Implement recovery verification and temporary access

Files: `HoleInHub.Business/Accounts/EmailVerificationService.cs`, `HoleInHub.Data/Reservations/BookingPersistence.cs`, new `HoleInHub.Business/Reservations/GuestReservationAccessService.cs`, new `HoleInHub.Web/Controllers/GuestReservationsController.cs`, recovery ViewModels and existing service registration.

- [x] Write failing tests for recovery/account/booking purpose isolation, expired/wrong/used codes, concurrent redemption, resend invalidation and browser-independent email throttling. Cover concurrent issuance from separate challenge IDs, the exact quota/window boundaries, transaction rollback without quota consumption and all limited endpoints.
- [x] Make verification purpose explicit at issuance rather than extending the current account-prefix/otherwise-guest inference. Update both existing callers and compatibility tests.
- [x] Reuse the existing encrypted outbox and shared verification operations. Add atomic recovery-proof consumption and email throttling. Keep booking-draft submission semantics intact.
- [x] Implement Find GET/POST, Verify GET/POST, Change email and antiforgery-protected Leave POST. Bind email to the protected challenge context; the verification form must not accept a replacement email.
- [x] Issue and validate the separate 30-minute protected guest-access cookie. Expired/tampered/missing access returns to recovery. Constrain return URLs to local guest history/details routes.
- [x] Verify successful recovery neither signs into Identity nor changes any reservation status, hold, deadline, owner or payment state.

### 3. Add history and authorize reused details/actions

Files: new guest list ViewModel, `HoleInHub.Business/Reservations/ReservationService.cs`, guest controller, `HoleInHub.Web/Controllers/ReservationsController.cs`, existing details view and new `HoleInHub.Web/Views/GuestReservations/Index.cshtml`.

- [x] Write failing web tests for list/detail access with and without the verified guest session, forged IDs, account records sharing the email, cross-email access, expired sessions, cancellation authorization and antiforgery.
- [x] Expose narrow Business list/detail operations; Web must not query MongoDB directly. Filter before mapping private data, not after returning all reservations to Web.
- [x] Reuse one authorization path for status/details, cancellation and eligible checkout. Existing token URLs and account ownership checks remain supported. A guest session never bypasses cancellation cutoffs or production simulated-payment restrictions.
- [x] Reuse reservation row markup/labels and the details view, passing the correct history back-link context. Keep Pending unmistakable and preserve coaching, legacy counts and history.
- [x] Add 25-row paging, Upcoming/Past counts and the verified empty state. Ensure browser-back behavior after Leave does not expose a cached private page.

### 4. Build recovery screens and connect entry points

Files: new `Views/GuestReservations/Find.cshtml`, `Verify.cshtml`, existing `_VerificationLayout.cshtml`, `_EmailCodeForm.cshtml`, `_AccountMenu.cshtml`, SignIn/Confirmation views and scoped styling in the existing verification/account-hub styles.

- [x] Add the three quiet Find my booking links without moving the primary sign-in/book actions.
- [x] Reuse the guest verification shell and the sign-in-style input. Parameterize only copy/actions needed by recovery; do not duplicate code handling or countdown scripts.
- [x] Provide a visible field label, accessible errors, disabled/loading states, resend/expired behavior and Change email. Verification shows the previously entered email as text.
- [x] Complete native form submission paths and keyboard focus handling. Reuse existing motion utilities; no new animation dependency.

### 5. Verify, document and review

- [x] Run solution build, non-integration tests, isolated local MongoDB integration tests, JavaScript tests and Tailwind build. Preserve the owner's launch environment and running preview.
- [ ] In one batched desktop/mobile pass inspect Find, Verify empty/focused/invalid/expired, guest history upcoming/past/empty, details/back-link and Leave/expired session. Fix findings together and confirm once.
- [x] Confirm secure emailed booking links still open only their reservation; verified account bookings still skip booking-code verification; existing guest booking submission still creates Pending only after verification.
- [x] Update onboarding, security/testing documentation and the project flow notes. Report actual checks and limitations. No automatic shared/Atlas migration or deployment.

## Deferred

Automatic guest-to-account linking, permanent guest profiles, SMS recovery, reference-only lookup, account password recovery changes, changing an existing reservation's email, new refund/rescheduling behavior and a staff recovery console. No live payment integration is included.

## Execution ledger

Ruling: reused the shared Business verification service and added a ReservationService partial for guest queries rather than a separate orchestration service — avoids duplicating existing issuance/consumption logic and keeps Web free of MongoDB dependencies.

Ruling: guest action routes carry an explicit non-authorizing guest-context flag for recovery after browser cookie expiry; shared details uses explicit Reservations controller routes and the canonical guest detail refresh URL. The flag never grants access.

Ruling: keep original account reservation row presentation in one shared partial used by both account and recovered guest history. Upcoming is chronological; Past is reverse chronological.

Migration 007 applied successfully through the explicit CLI to a fresh disposable local preview database. No shared/Atlas migration or owner app restart was performed. Scoped review found shared-view action routing and expired-cookie recovery issues; both corrected and covered by generated-route/native-form tests. The final browser pass is unverified because CUA returned “Local browser use is disabled in Work settings”; do not claim visual confirmation.

Final verification: Release solution build zero warnings/errors. 63 unit + 37 Web smoke + 27 Web integration + 52 Data/Business integration tests passed (179 total); 46 JavaScript tests passed; Tailwind build passed. Preview readiness was Healthy after all seven migrations. Temporary preview process stopped using its verified PID/command path. Browser checks remain outstanding because local browser use is disabled in Work settings. No live/shared migration, deployment or owner process restart performed.
