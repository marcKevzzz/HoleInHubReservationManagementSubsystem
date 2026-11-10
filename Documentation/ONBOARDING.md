# Developer Onboarding Guide

Welcome to the **Hole In Hub** engineering team! This guide walks you through setting up your local environment, building the solution, running tests, and starting local development from a clean checkout.

---

## 1. Prerequisites

Before cloning or opening the project, ensure you have installed:

| Tool | Minimum Version | Purpose |
| :--- | :--- | :--- |
| **.NET SDK** | 10.0+ | Compiles and runs web and console applications |
| **Node.js** | 20 LTS or 22 LTS | Compiles Tailwind CSS v4 and packages local GSAP |
| **Docker Desktop** | Latest | Runs local single-node MongoDB replica set |
| **Git** | 2.40+ | Version control |

---

## 2. Quickstart: Clean Checkout to Running App

### Step 1: Restore and Build
Run from the repository root:
```powershell
dotnet build HoleInHubReservationManagementSubsystem.slnx
```

### Step 2: Start Local MongoDB Replica Set
```powershell
docker compose -f docker-compose.mongo.yml up -d
```

### Step 3: Select the Local Database and Run Migrations
These overrides select the local database even if Windows has persistent Atlas settings. For Atlas, follow [MongoDB Atlas](#mongodb-atlas) instead of this step.

```powershell
$env:MongoDb__ConnectionString = 'mongodb://localhost:27017/?replicaSet=rs0'
$env:MongoDb__DatabaseName = 'holeinhub_dev'
$env:MongoDb__ReplicaSetName = 'rs0'
dotnet run --project HoleInHub.Migrator -- migrate
```

### Step 4: Run the Web Application
```powershell
dotnet watch --project HoleInHub.Web run --launch-profile http
```
Open <http://localhost:5275>. The optional `https` launch profile uses <https://localhost:7077>.

---

## 3. Daily Development Workflow

### Sprint 1 database and email setup

Start Docker Desktop before running Compose. MongoDB and the Mailpit test inbox bind only to this computer. Keep the named MongoDB volume: `docker compose down -v` deletes local data.

Use these environment variables in the same PowerShell session for both Web and Migrator:

```powershell
$env:MongoDb__ConnectionString = 'mongodb://localhost:27017/?replicaSet=rs0'
$env:MongoDb__DatabaseName = 'holeinhub_dev'
$env:MongoDb__ReplicaSetName = 'rs0'
docker compose -f docker-compose.mongo.yml up -d
docker compose -f docker-compose.mongo.yml exec mongodb mongosh --quiet --eval 'db.hello().isWritablePrimary'
dotnet run --project HoleInHub.Migrator -- migrate
```

The primary check must return `true`. Migrations create the database, collections, indexes, and resource catalog; do not create collections manually. Connect MongoDB Compass to the same local URI and select `holeinhub_dev`. Open the development email inbox at <http://localhost:8025>. Mailpit captures email locally; it does not deliver to customers.

### Configuration scope and secrets

Both Web and Migrator currently read environment variables. A `.env` file is ignored by Git but is not automatically loaded by either application. Web has a project `UserSecretsId` and loads its local secrets in Development; Migrator uses an explicit configuration builder without a user-secrets provider.

| Method | Scope | Current support |
| :--- | :--- | :--- |
| `$env:MongoDb__...` in PowerShell | That terminal and child processes | Web and Migrator |
| Windows user environment variables | Newly started processes for that Windows account | Web and Migrator; restart terminals and Codex after saving |
| `dotnet user-secrets` | Local user and project, outside Git | Web in Development; Migrator needs a code change |

Environment variables override JSON configuration and Web user secrets. Starting an app from another terminal or an agent session does not inherit variables set in your original terminal. Before replacing a running app, preserve its configured launch environment and verify `/health/ready`; a page rendering successfully does not prove database connectivity.

For Web-only user secrets, the project already contains its non-secret `UserSecretsId`. Set `MongoDb:ConnectionString` and `MongoDb:DatabaseName` privately with `dotnet user-secrets set --project HoleInHub.Web`. Commit the generated `UserSecretsId` so collaborators can reuse the commands; each collaborator stores their own secret values. This does not configure Migrator. Use environment variables for the current end-to-end setup below.

### MongoDB Atlas

1. Create an Atlas project and cluster, then create a database user restricted to the intended application database. Atlas login accounts and database users are separate.
2. Permit your current public IP for development and your deployed application's outbound IP for deployment. Avoid unrestricted network access. See the [Atlas connection guide](https://www.mongodb.com/docs/atlas/connect-to-database-deployment/).
3. Copy the .NET driver SRV URI from **Connect → Drivers**. Enter credentials privately through environment variables or a secret store; never commit or paste credentials into chat. URI-encode special characters in credentials.
4. Set `MongoDb__ConnectionString` to the SRV URI and `MongoDb__DatabaseName` to the chosen database. Leave `MongoDb:ReplicaSetName` unset so Atlas discovers its replica set from the SRV URI. The checked-in local connection URI already includes `replicaSet=rs0`; no separate override is needed. Remove any `MongoDb__ReplicaSetName=rs0` environment override when switching to Atlas. Apply these settings to both Web and Migrator.
5. Run the migration CLI explicitly with a separate migration credential authorized to create collections and indexes. Run Web with the restricted application credential. Never migrate automatically on startup.
6. Start Web and check `/health/ready`. Confirm the expected collections in Compass using the Atlas URI. Integration tests must continue using their isolated local test databases, never the shared Atlas database.

#### Collaborator setup on Windows

Each collaborator needs a database user for the development database and their current public IP in the Atlas project IP access list. From their development network, they can run `Invoke-RestMethod https://api.ipify.org` and send the returned public IP to the project owner. Add it under **Security → Network Access → Add IP Address**. Update the entry when their network or VPN changes. Use a dedicated development database; integration tests use local isolated databases.

Run this from the repository root in PowerShell 7.1 or later. The masked prompt keeps the URI out of the command itself. Enter the actual development database name at the second prompt.

```powershell
$env:MongoDb__ConnectionString = Read-Host 'Atlas connection string' -MaskInput
$env:MongoDb__DatabaseName = Read-Host 'Development database name'
Remove-Item Env:MongoDb__ReplicaSetName -ErrorAction SilentlyContinue
```

To make these settings available to future terminals and agent sessions for your Windows account, save them once:

```powershell
[Environment]::SetEnvironmentVariable('MongoDb__ConnectionString', $env:MongoDb__ConnectionString, 'User')
[Environment]::SetEnvironmentVariable('MongoDb__DatabaseName', $env:MongoDb__DatabaseName, 'User')
[Environment]::SetEnvironmentVariable('MongoDb__ReplicaSetName', $null, 'User')
```

Fully restart terminals, IDEs, and Codex after saving. Existing processes retain their original environment; the terminal above can use its `$env:` values immediately. Windows user environment variables and user secrets are unencrypted local storage. Use deployment-managed secrets in production. Rotate any credential exposed in chat, logs, or Git; keep real URIs out of repository files and screenshots.

A designated maintainer applies migrations explicitly using the separate migration credential described above. Collaborators using an already migrated database can start Web directly:

```powershell
dotnet watch --project HoleInHub.Web run --launch-profile http
```

Verify the connection from another terminal:

```powershell
Invoke-RestMethod http://localhost:5275/health/ready
```

Readiness must return HTTP 200 with `Healthy` and all required migrations applied. If it reports unreachable, check the launch process's environment, Atlas IP access list, credentials, and DNS/network access. If migrations are missing, have the maintainer run the explicit CLI; Web startup never applies them. A sign-in or availability HTTP 500 with a MongoDB timeout is a database failure, not evidence of a UI regression. Inspect configuration presence without printing its values.

#### Local email testing without Docker

Use the Mailpit Windows launcher from the repository root in a separate PowerShell 7 terminal:

```powershell
pwsh -File scripts/start-mailpit.ps1
```

The launcher supports Windows x64. On first run it downloads Mailpit v1.33.0 from the official release and checks its pinned SHA-256 digest before extracting or running it. The binary and persistent captured messages stay under the Git-ignored `.local/mailpit/` folder. Later runs reuse the installed binary. Leave the terminal running; Ctrl+C stops Mailpit. Run it again after restarting your computer. If either port is already occupied, stop the existing Mailpit instance before starting another.

Open <http://localhost:8025>. SMTP listens at `127.0.0.1:1025`, and both listeners accept only local connections. Mailpit captures messages instead of sending to real inboxes; no email-provider account is needed. This works with Atlas or local MongoDB. Verification and password-reset links in captured messages are sensitive: keep `.local/` out of Git and avoid sharing tokens or inbox screenshots.

Keep Web in Development (`--launch-profile http` already sets this). Its existing SMTP settings use `localhost:1025`, no authentication, and no STARTTLS; no application code or secret is needed for Mailpit. Environment variables override these settings, so clear any unrelated `Smtp__...` overrides in the Web terminal if necessary. Keep `AccountEmail:PublicBaseUrl` aligned with the local Web URL (`http://localhost:5275`). Use TLS and a real email provider in production.

To test the full flow, require `/health/ready` to be healthy, create a dedicated test account, wait for its verification message in Mailpit, and open the link. Then request a password reset for that verified account and check for the reset message. The notification worker polls every 15 seconds; queued failures may take longer because of retries. An unverified account intentionally does not receive a password-reset message.

#### Optional Docker Mailpit

When Docker is already available, start only the email service with:

```powershell
docker compose -f docker-compose.mongo.yml up -d mailpit
```

Use either this service or the native launcher, since both use ports 1025 and 8025. Docker remains required for the repository's local MongoDB integration-test environment, but is not required for Atlas plus native Mailpit.

Atlas connection failures commonly indicate an IP access-list mismatch, invalid database credentials, a local replica-set override, or DNS/network restrictions. Never disable TLS to work around a failed connection.

### Operational approval

Public browsing is available before booking policy approval. Online submission must remain blocked until staff review coverage, notification recipients, and service duration/eligibility rules are configured and approved. Development test policies are not venue approval.

Sprint 1 covers customer access, verification/reset email, service browsing, availability, guest/account booking, secure My Reservations and atomic capacity allocation. Existing coaching, staff review and whole-shop demonstration screens were implemented early at the owner's request; this improvement batch does not expand later-sprint features. Staff dashboards, attendance and change workflows belong to Sprint 2; reports, reminders and POS integration belong to later milestones. The owner subsequently authorized the customer profile and read-only demo transaction pages.

See the [IPT102 project timeline](PROJECT-TIMELINE.md) and [Sprint 2 implementation plan](plans/2026-11-06-sprint-2-staff-operations.md) for the Week 15 milestone, work packages, and acceptance checklist.

The owner-directed provisional policy overrides the timeline's older indefinite Pending hold: Regular requests have 30 minutes; VIP, coaching and whole-shop have 60 minutes. Ordinary sessions can be 30 minutes, following the owner's revised time-selection direction. Guest submission queues a code without holding capacity; successful verification revalidates availability and creates Pending. Verified accounts submit directly. Whole-shop submission requires explicit Development/Demo configuration; a simulated 50% deposit can confirm it without collecting money. Production rejects simulated payment confirmation. Authoritative pricing, staff coverage and live POS terms still need client approval; Sprint 1 is not an operational production launch.

### Continuous Tailwind Compilation
```powershell
cd HoleInHub.Web
npm run watch:css
```

### Running Tests
- **Unit and Smoke tests (fast, no database required)**:
  ```powershell
  dotnet test HoleInHubReservationManagementSubsystem.slnx --filter "Category!=Integration"
  ```
- **Integration tests (with Docker MongoDB running)**:
  ```powershell
  dotnet test HoleInHubReservationManagementSubsystem.slnx --filter "Category=Integration"
  ```

## Complete booking workflow (2026-11-03)

Run the explicit Migrator CLI before starting the new booking flow. Migration `004_BookingProcess` adds guest verification indexes, preserves bay IDs and retires original `bay-1`. Existing `bay-2`–`bay-6` become display Bay 1–5; `vip-7`/`vip-8` become VIP 1/2. It refuses to change the catalog while a future active whole-shop or original Bay 1 allocation exists. Resolve these through staff before retrying; never delete booking history or alter applied migrations. Existing attendee counts remain legacy totals without invented player/non-player splits.

Guest checkout now runs as: complete details and review → Submit request → email-code verification → Request received, showing Pending. Initial submission stores only an encrypted ten-minute draft and sends a code; it holds no capacity. Successful verification rechecks the schedule and availability, then atomically creates the Pending booking. Whole-shop requests proceed from the receipt to the existing demo deposit checkout.

Guest email verification and secure status-link emails use the normal SMTP/outbox worker. Codes expire after 10 minutes, allow five attempts, and have a one-minute resend cooldown plus HTTP rate limits. A proof is consumed in the same transaction as its booking; replaying the same submission returns the original booking. Codes and status-link email bodies are encrypted using ASP.NET Data Protection, while verification/access lookup values are hashed. Preserve the Data Protection key ring across restarts and deployment instances. Keep Mailpit and its captured emails local; bearer links are sensitive. Use HTTPS outside local Development and redact query strings in proxy/access logs.

The booking form collects one Number of guests value including non-players. New total-count records keep no invented per-bay player/non-player split; staff must check the six-player-per-bay limit during review. Existing detailed participant records remain readable. Total occupancy is validated against selected bay capacity.

The booking form works without JavaScript. Verified accounts skip guest codes; signed-in unverified accounts use the existing account verification page. Signed-in users receive editable first name, last name and phone defaults from their account, with latest-owned-reservation fallback for missing legacy values. Account email is displayed read-only and derived again on the server. Guest mode offers a guest/sign-in choice. Past starts and starts inside the service review lead time are disabled in the enhanced calendar and rejected by the server. Selecting a new time defaults to a full hour. Whole-shop selections always include every active bay.

Guests receive an unguessable status link. References alone never authorize access. Customers and Admins use `/account/sign-in`. Admins enter their verified work email and password to open `/staff/dashboard`. Staff and Managers use their PIN at `/staff/sign-in` on the dedicated HTTPS staff hostname. Managers and Admins can manage schedule blocks. The workspace uses a separate session from the customer account. Admin access ends when you sign out of the account or switch accounts in the same browser.

Public registration creates Customer accounts. Follow the [staff PIN setup instructions](specs/2026-11-07-rbac-staff-authentication.md#registered-desktop-pin) to configure the staff hostname, enroll TLS client certificates, and activate PIN sign-in. Apply migrations 016 and 017 through the Migrator before administering roles.

Staff, Managers, and Admins share the Dashboard and reservation schedule. Staff sign in to the schedule. Managers and Admins sign in to the Dashboard. Managers can view the staff directory and alert summary, and can manage blocks from the schedule.

After signing in with an Admin email and password, open **People → Team & access → Edit staff access**. The directory shows all accounts and can be filtered to staff accounts. Select Staff, Manager, Admin, or **No staff access**, then enter a reason and save. The account needs a verified email and a password to receive a staff role. A saved change ends the employee's existing staff session on its next request. Staff and Managers retaining or gaining access must set up a PIN. Admins sign in with their password. Revocation preserves the customer account and its reservations. Role editing is unavailable in PIN presentation mode.

Open **Administration → Staff access log** to review staff access changes with the actor, account, previous and new roles, reason, and Manila timestamp. The log uses current account emails where available and keeps the saved account ID when an account is missing.

Use the controlled database administration process to bootstrap the first Admin or recover Admin access if all Admin sign-ins are unavailable. Perform recovery during a maintenance window and document the actor, reason, and affected account. Use **Team & access** for routine changes so they pass the concurrency guard and create an audit entry.

### Enable a local demonstration

Use the same local MongoDB/database variables as above. In the Web launch session only, explicitly configure:

```powershell
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:ReservationPolicy__Approved = 'true'
$env:ReservationPolicy__DurationsApproved = 'true'
$env:ReservationPolicy__ReviewStart = '00:00:00'
$env:ReservationPolicy__ReviewEnd = '1.00:00:00'
0..6 | ForEach-Object { [Environment]::SetEnvironmentVariable('ReservationPolicy__ReviewDays__' + $_, [string]$_, 'Process') }
$env:ReservationPolicy__StaffEmails__0 = 'staff@example.test'
$env:ReservationPolicy__DemoPaymentsEnabled = 'true'
$env:AccountEmail__PublicBaseUrl = 'http://localhost:5275'
dotnet run --project HoleInHub.Web --launch-profile http
```

The configured coverage spans all days, but business validation still accepts requests only during existing venue operating hours (Monday 11 AM–midnight, other days 10 AM–midnight). The full 30-minute Regular or 60-minute other-service window must fit before closing and session start. No maximum advance date applies. The approval flags acknowledge the owner's provisional policy; do not treat them as measured client acceptance.

Whole-shop checkout displays a server-calculated demonstration total/deposit and allows success/failure/cancel simulations. It requires both the explicit flag and Development or Demo environment. Production rejects simulated checkout even if the flag is accidentally set; online whole-shop submission remains unavailable until a real POS contract and authoritative prices exist. Demo uses documented weekday/weekend rates without a holiday calendar and labels totals as demonstration values. A 50% successful simulated deposit confirms immediately. Failed/cancelled checkout may retry until the same 60-minute hold expires; retries never extend it. Duplicate successes do not confirm twice. Late successes are recorded as `LatePayment`, appear in staff review for reconciliation, and never revive an expired/rejected/cancelled allocation.

The future POS handoff must provide checkout tied to a reservation, an authoritative PHP rental/deposit quote, and an authenticated payment result with reservation/payment references, amount, currency and outcome. This release exposes no live payment callback and accepts no customer-supplied payment amount. POS retains collection, official receipts, refunds and settlement. Never connect the demo buttons to real funds.

Cancellation requests require at least one hour before the start and staff approval. The request itself releases no allocation and does not extend an existing Pending deadline. Refund terms and rescheduling remain venue-managed. Coaching uses regular bay capacity and requires staff to record external coach confirmation before venue approval; package bookings allocate only the selected first session.

Verify with unit/smoke tests, local isolated MongoDB integration tests (including guest/staff HTTP flows), `npm run test:ui`, and `npm run build:css`. A healthy `/health/ready` requires all migrations 001–007, even when Google is disabled. Do not run integration tests against Atlas or the shared preview database.

Owner update (2026-11-03): ordinary sessions may be 30 minutes. Each time tile represents a half-hour; dragging consecutive tiles sets the complete duration (two tiles = one hour). Existing fixed coaching classes and whole-shop rental windows retain their defined durations. Review deadlines and minimum booking lead times are unchanged.

### Optional Google sign-up and sign-in

Google is disabled by default. Email/password signup and guest booking remain available. First name, last name and contact number are required for new accounts and prefill booking details. Existing accounts keep working; missing account contact values fall back to the latest owned reservation. Editing booking details changes that reservation, not the saved account profile.

Before enabling Google, run the explicit migration CLI against the intended database to apply `005_GoogleAccounts`. It adds a unique index for present Google subjects without changing legacy accounts. Readiness requires all migrations through 007; migrations never run on Web startup.

Create a Google OAuth **Web application** client and register the exact application's origin plus `/signin-google` as its authorized redirect URI (for example `https://localhost:PORT/signin-google`, using your actual HTTPS port). This is the middleware callback, not `/account/google-callback`. For a consent screen in Testing, add the intended test users in Google Cloud. Use a separate production client/HTTPS redirect when deploying.

Set credentials outside source control in the environment used to launch Web:

```powershell
$env:Authentication__Google__Enabled='true'
$env:Authentication__Google__ClientId='<your client ID>'
$env:Authentication__Google__ClientSecret='<your client secret>'
```

Do not paste the real secret in chat, commit it, or put it in a shared script. Restart Web after configuration changes. Enabling Google without both credentials fails configuration validation; when disabled, Google buttons are hidden and no Google provider is registered.

New Google customers finish their first name, last name and contact number before an account is created. Google does not supply the phone number in this integration. Under the revised owner policy, the Google verified-email claim satisfies verification for any valid Google-provided email address. Unverified Google email claims are rejected rather than sent through another venue code step. Existing Google-linked accounts with matching verified provider email are marked verified on sign-in. Google identities are matched by the provider subject, never silently merged into an existing same-email account. Those customers must use their existing sign-in method; account linking is outside this change. Google tokens are not persisted. Cancellation/failure returns to a safe account screen.

References: [Microsoft Google authentication setup](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/social/google-logins?view=aspnetcore-10.0), [Google guidance on authoritative email identity](https://developers.google.com/identity/sign-in/ios/backend-auth).



### Customer account pages and verification

`/my-reservations`, `/transactions` and `/my-profile` share customer navigation and require sign-in. Profile updates accept only current-user first name, last name and contact number; the email remains immutable. Booking contact edits affect only the reservation. Transactions show existing owned whole-shop demonstration payment state and history, with separate rental/deposit and reservation/payment statuses; no real transaction ledger or receipts are created.

New email/password accounts verify using a six-digit code at `/account/verify-email`. The email is retained in a protected browser-bound account context; the page never asks for it again. On successful code verification the customer is signed in and redirected immediately to the home page. Previously issued verification links still work. Unverified sign-in queues a code as needed; already-verified users skip verification at booking.

Account and guest verification have separate components and purpose scopes, with the same EmailVerificationService and encrypted durable outbox. Codes expire after ten minutes, allow five attempts and have a one-minute resend cooldown. Account code confirmation uses a protected Identity confirmation token and a two-hour protected context cookie, so an expired code can be resent without another email entry. Account codes never allocate a bay; guest codes create Pending only after revalidation. Both support non-JavaScript form submission. The historical MongoDB collection name `guest_verifications` is retained for compatibility; account records use a distinct hashed `account-email:` scope. The account-code feature required no migration beyond 005; the subsequent schema-hardening release additionally requires 006.

Google authorized redirect URIs for the owner's configured addresses are `http://localhost:5275/signin-google` and `https://holeinhub.runasp.net/signin-google`. Root site URLs alone are not callback registrations. Development user secrets are local to the Windows account and are not deployed; the host requires its own environment configuration and persistent Data Protection key directory.

The standalone availability page and its navigation entry are temporarily hidden (404); booking availability checks remain active inside the wizard.


### Schema-hardening release (migration 006)

Read [DATABASE.md](DATABASE.md) for plain-language collection/field definitions and legacy storage-name mappings. This release adds typed validators, explicit customer-account links, verification purposes, structured history fields and a customer-history index without new admin screens. Apply 006 before starting this Web build; readiness requires 001–008, including when Google is disabled.

Stop older Web instances/workers before upgrading, back up the selected database, run the explicit Migrator with the intended credentials, then start the new Web build. Do not mix old binaries with new writers or use a binary-only rollback after additive fields have been written. A preflight error means existing data needs review; migration 006 does not automatically delete or invent data. See [migration rollout/recovery](MIGRATIONS.md#006_schemahardening-existing-feature-validation).

### Guest reservation recovery (migration 007)

**Find my booking** is available from the anonymous account menu, sign-in screen and guest booking receipt. `/guest/reservations/find` sends a code to the entered booking email; verification opens guest history directly, including confirmed reservations. It does not create an account, sign into Identity or link bookings to an account. The existing per-booking email link remains valid independently of recovery.

Apply all pending migrations through the CLI before starting this Web build. Migration 007 extends the installed challenge validator with `GuestRecovery`, adds the case-insensitive reservation email/history index, and creates the TTL-managed recovery delivery-quota collection. No existing booking, allocation or migration is rewritten. Keep the intended Web/Migrator environment and check `/health/ready` after rollout; readiness requires 001–008. For local testing explicitly override Atlas configuration with the local replica-set URI and an isolated database.

Recovery shares the configured SMTP delivery and encrypted outbox. Codes last ten minutes with five attempts. Delivery is limited to one per email per minute and five per rolling hour across browser sessions, plus ten initial/resend requests per IP per ten minutes. Verification allows thirty requests per IP per ten minutes. IP limits use the existing per-process limiter; a multi-instance deployment needs coordinated edge/IP limiting and trusted proxy configuration. Persist/share the Data Protection key ring as documented above.

Successful recovery creates a separate protected, HttpOnly guest-access cookie lasting thirty minutes with no sliding extension. Leave guest access clears that browser's access, without cancelling bookings, signing out an account or invalidating original email links. Only guest records with the durable original verification marker can be recovered by email; legacy records missing it keep their existing secure-link access. Email access never reveals account-owned bookings sharing the email.

### Service catalog and contact fields (migration 008)

Apply pending migrations through the explicit Migrator using the intended database credentials before starting this build. `008_ServiceCatalog` creates `service_catalog`, seeds the six existing offerings, and adds an active/order/ID index. Re-running the CLI preserves venue edits. Readiness now requires 001–008. No migrations run at Web startup and no existing resource IDs or reservations are changed.

`/Home/Services` (also `/services`) reads current active catalog records on each request. Its server-rendered partial `/Home/ServiceCatalog` is fetched every 45 seconds while the tab is visible; it preserves the last good content on errors and skips replacement during focused interaction. Title, local image, summary, indicative rate text, details, action label, order and visibility are database-backed. Whole-shop links remain contact-only on this public page. The catalog is presentation content: it does not replace the booking rules, server quote calculations or approved service/resource mapping, and Active controls catalog visibility only. No catalog administration UI is included.

New/edited first and last names accept international letters, combining marks, spaces, periods, apostrophes and hyphens, and reject numbers. Phone fields accept 7–15 ASCII digits and preserve leading zeroes. Browser input filtering, native form constraints and server validation apply to booking, signup, Google account completion and profile editing. Historical contact values are not bulk rewritten; formatted account phone numbers are shown as digits when prefilled for editing.

Successful reservation submission continues to enqueue one encrypted durable outbox message in the booking transaction. The receipt now includes Pending status, service, Manila session time, guest count and the existing secure status link (account holders use their owned reservation page). Regular/coaching requests still require approval. For local delivery use Mailpit; actual customer delivery needs the configured SMTP sender. This task does not introduce a second notification worker or mail pipeline.
