# Sprint 1 essential process acceptance — 2026-11-04

Scope: owner requested the necessary functional process only. Mobile, responsive styling, visual polishing and browser gesture reviews are excluded from this acceptance pass. Results below are automated HTTP/domain/MongoDB checks, not a claim of full production or live payment certification.

| Essential process | Evidence | Result |
| --- | --- | --- |
| Running application readiness | Read-only GET localhost:5275/health/ready returned Healthy at 2026-11-04T13:37:44Z | Pass |
| Registration, email verification, login, reset/lockout and ownership | Existing account verification/identity and owner-access HTTP tests | Pass |
| Guest submission | Email proof, no hold before verification, revalidation, Pending receipt, secure link and idempotent retry | Pass |
| Capacity correctness | Competing submissions, multi-bay atomicity, whole-shop exclusion, deadlines, expiry/approval/payment races | Pass |
| Staff approval and rejection | Added actual guest-submit → separate persisted Staff login → queue → decision → guest updated status; verifies staff actor and notification records | Pass |
| Cancellation | Added confirmed guest request retains allocation → Staff approves → Cancelled shown and bay becomes available | Pass |
| Guest confirmed access and lost-link recovery | Existing secure-link and recovery HTTP tests, account/other-email isolation, tampered/expired sessions and code limits | Pass |
| Coaching and whole-shop demo | Coach plus venue approval, simulated success/failed/late/duplicate payment and environment restrictions | Pass |
| Notification processing | Eligibility regression, retry/lease/stale-alert tests and real SMTP acceptance of encrypted/decrypted submission receipt by local Mailpit | Pass |

## Defect found and corrected

Submission receipts deliberately store RequiredStatus=null so they remain useful after a staff decision. OutboxPersistence.IsEligibleAsync previously compared the reservation status to null, causing the worker to skip the receipt and mark it processed without sending. A new integration test reproduced the failure, then passed after allowing an absent status restriction only while the linked reservation exists. Status-bound Pending alerts still require their expected state and valid deadline; missing reservations remain ineligible.

The local SMTP test sends only to a unique synthetic @example.test address through Mailpit at 127.0.0.1:1025, using the actual SmtpEmailSender and encrypted outbox payload. Successful SendAsync proves local SMTP acceptance. It does not prove delivery to an external customer's inbox or exercise the background worker loop end-to-end; eligibility, SMTP and leasing/retry are checked separately.

## Final checks

Release solution build: zero warnings/errors. 77 unit +42 Web smoke +34 Web integration +55 Data/Business integration tests passed (208 total). 51 JavaScript tests and Tailwind passed. All database-writing acceptance tests use isolated local replica-set databases and clean them up. Scoped review found no actionable issue in the fix. No real reservation or configured-database data was changed.

No new migration is required for the receipt fix. Restart/rebuild the normal Web process to load it. Receipts previously skipped and already marked complete are not automatically resent because this pass does not authorize bulk customer messaging. External SMTP/provider delivery and client approval of provisional venue policies remain outside this check.
