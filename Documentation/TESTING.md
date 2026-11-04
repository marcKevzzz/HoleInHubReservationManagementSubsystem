# Testing and verification

Synchronized with current test files and commands on 2026-11-04. This is coverage inventory, not a claim that these tests passed in this revision. Historical counts in the foundation progress ledger apply only to that earlier run.

## Suites

| Suite | Current coverage |
| --- | --- |
| Unit | Booking windows, service/participant rules, reservation policy, MongoDB options, migration ordering/checksums/registration and CLI behavior |
| Web | Public pages, availability, account identity, Google callbacks, account codes, account hub/navigation, guest booking and database-unavailable handling through WebApplicationFactory |
| Integration | Real replica-set allocation contention, guest booking process, outbox recovery, migration leasing/reruns, typed validator rejection, legacy-data preflight, account/guest ownership, structured history and schema-reference parity |
| JavaScript | Booking verification, reservation behavior, shared/button motion, account UI/menu and page transitions |

Mocked Web services test HTTP behavior without proving real MongoDB transactions, SMTP delivery or Google authentication. Integration tests use isolated local databases; never point them at Atlas or a shared preview. The fixture reads MongoDb__ConnectionString from the launching process, so explicitly override persistent Atlas settings before running integration tests. See the fixture and test attributes for actual category membership.

## Required commands

Run from the repository root:

```powershell
dotnet build HoleInHubReservationManagementSubsystem.slnx
dotnet test HoleInHubReservationManagementSubsystem.slnx --filter "Category!=Integration"
docker compose -f docker-compose.mongo.yml up -d
$env:MongoDb__ConnectionString = 'mongodb://localhost:27017/?replicaSet=rs0'
dotnet test HoleInHubReservationManagementSubsystem.slnx --filter "Category=Integration"
npm --prefix HoleInHub.Web run test:ui
npm --prefix HoleInHub.Web run build:css
```

`test:ui` is the current package script. Additional legacy standalone menu/motion tests can be run with `node --test tests/HoleInHub.Tests.Web/Smoke/menu.test.cjs tests/HoleInHub.Tests.Web/Smoke/motion.test.cjs`. Do not infer their execution from `test:ui`.

## Manual checks

Require a healthy `/health/ready` (MongoDB and migrations 001–007) before end-to-end testing. Verify actual email delivery with Mailpit or configured SMTP, guest draft/code/receipt, repeated submission, owned/foreign account access, staff review and explicitly enabled demo checkout. Google requires its configured OAuth client and callback. Check keyboard focus, reduced motion, mobile layouts and non-JavaScript forms. A rendered sign-in page does not prove sign-in works.

## CI and evidence

The current CI workflow installs local assets, checks dotnet formatting, builds/tests/publishes, and runs replica-set integration tests in a separate job. It does not currently run `npm run test:ui`; run that explicitly for local verification. Record command exit codes, actual failures and environmental blockers rather than copying earlier test totals. See [ONBOARDING.md](ONBOARDING.md) for configuration and [CURRENT-SYSTEM.md](CURRENT-SYSTEM.md) for implemented boundaries.

## Guest reservation recovery coverage

`GuestAccessTests` exercise case-insensitive guest ownership, confirmed/history access after challenge cleanup, account/other-email isolation, 25-row paging, migration 007 schema compatibility/idempotency, persisted cross-browser delivery quotas, concurrent issuance/redemption, replacement codes, five-attempt lockout and outbox-failure transaction rollback.

`GuestRecoveryTests` exercise native forms, CSRF, generated detail/cancellation routes, confirmed booking access, account-profile isolation, original bearer links, unknown-email empty history, code expiry/resend with server-bound email, safe return destinations, separate guest cookies, tampered/expired sessions, normal cookie removal, Leave and endpoint IP limits. Factories always configure isolated local replica-set databases.

Run the required solution build, non-integration/integration suites, JS tests and Tailwind build. A browser preview is additional evidence: inspect recovery forms, invalid/expired states, guest upcoming/past/empty history, details/back-link and Leave on desktop/mobile. Do not treat rendered pages alone as proof of database readiness.

## Contact rules and public catalog

`ContactRulesTests` and `contact-inputs.test.cjs` cover Unicode names without numbers and ASCII-digit phone input. Signup/profile/booking HTTP tests reject forged invalid fields with JavaScript bypassed. Existing booking-flow tests verify receipt content and the secure status link while retaining idempotency checks. `ServicesCatalogTests` exercise schema seeding/rerun safety, active ordering, edited DB content, safe local imagery/actions and full/partial fresh fetches. `services.test.cjs` covers successful and failed refresh, hidden tabs and focus acquired during an in-flight request. Browser layout checks remain additional evidence, not replaced by these tests.

## Essential process acceptance

The process-only acceptance record is [Sprint 1 essential process acceptance](plans/2026-11-04-sprint-1-process-acceptance.md). GuestBookingFlowTests now include separate guest/Staff HTTP approval/rejection/cancellation journeys and a real local SMTP acceptance test using the existing SmtpEmailSender. Start the complete Docker Compose stack, including Mailpit at 127.0.0.1:1025, for the Integration category. The SMTP test uses only unique synthetic example.test recipients; it never uses production SMTP credentials. AllocationTests cover receipt eligibility before/after a state change, stale Pending suppression and missing reservations. Mobile/visual checks were excluded by the owner for this pass.
