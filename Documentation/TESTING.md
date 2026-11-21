# Testing and verification

Synchronized with current test files and commands on 2026-11-04. This is coverage inventory, not a claim that these tests passed in this revision. Historical counts in the foundation progress ledger apply only to that earlier run.

## Suites

| Suite | Current coverage |
| --- | --- |
| Unit | Booking windows, service/participant rules, reservation policy, MongoDB options, migration ordering/checksums/registration and CLI behavior |
| Web | Public pages, availability, account identity, Google callbacks, account codes, account hub/navigation, guest booking and database-unavailable handling through WebApplicationFactory |
| Integration | Real replica-set allocation contention, guest booking process, outbox recovery, migration leasing/reruns, typed validator rejection, legacy-data preflight, account/guest ownership, structured history and schema-reference parity |
| JavaScript | Booking verification, reservation behavior, shared/button motion, account UI/menu and page transitions |

Mocked Web services test HTTP behavior without proving real MongoDB transactions, SMTP delivery or Google authentication. Integration tests use isolated local databases; never point them at Atlas or a shared preview. MongoDbFixture connects to the local rs0 replica set on port 27017 and allocates a uniquely named test database. See the fixture and test attributes for actual category membership.

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

The test:ui script runs all top-level JavaScript tests and all JavaScript smoke tests, including menu navigation and motion.

## Manual checks

Require a healthy `/health/ready` (MongoDB and all registered migrations) before end-to-end testing. Verify actual email delivery with Mailpit or configured SMTP, guest draft/code/receipt, repeated submission, owned/foreign account access, staff review and explicitly enabled demo checkout. Google requires its configured OAuth client and callback. Check keyboard focus, reduced motion, mobile layouts and non-JavaScript forms. A rendered sign-in page does not prove sign-in works.

## CI and evidence

The current CI workflow installs local assets, checks dotnet formatting, builds/tests/publishes, and runs replica-set integration tests in a separate job. Both jobs install the SDK specified in global.json. The build job runs npm run test:ui before formatting and publishing. Record command exit codes, actual failures and environmental blockers rather than copying earlier test totals. See [ONBOARDING.md](ONBOARDING.md) for configuration and [CURRENT-SYSTEM.md](CURRENT-SYSTEM.md) for implemented boundaries.

## Guest reservation recovery coverage

`GuestAccessTests` exercise case-insensitive guest ownership, confirmed/history access after challenge cleanup, account/other-email isolation, 25-row paging, migration 007 schema compatibility/idempotency, persisted cross-browser delivery quotas, concurrent issuance/redemption, replacement codes, five-attempt lockout and outbox-failure transaction rollback.

`GuestRecoveryTests` exercise native forms, CSRF, generated detail/cancellation routes, confirmed booking access, account-profile isolation, original bearer links, unknown-email empty history, code expiry/resend with server-bound email, safe return destinations, separate guest cookies, tampered/expired sessions, normal cookie removal, Leave and endpoint IP limits. Factories always configure isolated local replica-set databases.

Run the required solution build, non-integration/integration suites, JS tests and Tailwind build. A browser preview is additional evidence: inspect recovery forms, invalid/expired states, guest upcoming/past/empty history, details/back-link and Leave on desktop/mobile. Do not treat rendered pages alone as proof of database readiness.

## Contact rules and public catalog

`ContactRulesTests` and `contact-inputs.test.cjs` cover Unicode names without numbers and ASCII-digit phone input. Signup/profile/booking HTTP tests reject forged invalid fields with JavaScript bypassed. Existing booking-flow tests verify receipt content and the secure status link while retaining idempotency checks. `ServicesCatalogTests` exercise schema seeding/rerun safety, active ordering, edited DB content, safe local imagery/actions and full/partial fresh fetches. `services.test.cjs` covers successful and failed refresh, hidden tabs and focus acquired during an in-flight request. Browser layout checks remain additional evidence, not replaced by these tests.

## Essential process acceptance

The process-only acceptance record is [Sprint 1 essential process acceptance](plans/2026-11-04-sprint-1-process-acceptance.md). GuestBookingFlowTests now include separate guest/Staff HTTP approval/rejection/cancellation journeys and a real local SMTP acceptance test using the existing SmtpEmailSender. Start the complete Docker Compose stack, including Mailpit at 127.0.0.1:1025, for the Integration category. The SMTP test uses only unique synthetic example.test recipients; it never uses production SMTP credentials. AllocationTests cover receipt eligibility before/after a state change, stale Pending suppression and missing reservations. Mobile/visual checks were excluded by the owner for this pass.

## Browser acceptance and report reconciliation

Run the browser journeys from a clean checkout with the local MongoDB replica set and Mailpit running. The Playwright configuration launches its own HTTPS instance on port 54893. Desktop Chromium and a touch-enabled mobile Chromium viewport exercise the same workflows.

```powershell
docker compose -f docker-compose.mongo.yml up -d --wait
npm --prefix HoleInHub.Web ci
node HoleInHub.Web/node_modules/playwright/cli.js install chromium
npm --prefix HoleInHub.Web run test:browser
```

The integration test `ReportSummaryChartsCsvAndEveryWorksheetReconcile` checks known totals in the staff report, CSV and all seven workbook tabs. Its fixture covers a reservation spanning midnight across two bays, a day with zero starts, and an empty reporting period.

## Concurrent 90-day report benchmark

With MongoDB, Mailpit and Chromium set up as above, run the desktop booking journey with the benchmark enabled. It creates a disposable database with 363 in-range reservations, warms the page, workbook and status endpoints, then measures five requests per endpoint alone and five on each of three concurrent request streams during guest confirmation. It also checks worker-only expiry before visiting any application page. The regular browser suite uses the smaller fixture.

```powershell
$env:HIH_REPORT_BENCHMARK = '1'
try { npm --prefix HoleInHub.Web run test:browser -- --project=desktop booking.spec.cjs }
finally { Remove-Item Env:HIH_REPORT_BENCHMARK }
```

Local medians in milliseconds on 21 November 2026:

| Request | Before, alone | After, alone | Before, concurrent | After, concurrent |
| --- | ---: | ---: | ---: | ---: |
| Full report | 81 | 72 | 85 | 95 |
| Workbook | 65 | 76 | 82 | 91 |
| Status list | 58 | 22 | 76 | 23 |

The status response shrank from 25,238 to 9,885 bytes. Guest confirmation through receipt took 371 ms before and 412 ms after, one observation each. These small local samples establish the benefit for status filtering only. The full report and exports read the last persisted statuses. The notification worker processes pending reservations on its normal polling cycle.
