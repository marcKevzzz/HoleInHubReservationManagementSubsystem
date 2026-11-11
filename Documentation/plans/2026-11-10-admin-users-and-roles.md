# Admin Users & Roles Implementation Plan

**Goal**

An Admin can grant, change, or revoke staff workspace access for an existing account from **Users & Roles**.

**Architecture**

Extend the existing account directory with a server-rendered edit page. Business validates the role change. Data commits the change, session revocation stamp, and audit event together in MongoDB. The staff PIN cookie already revalidates the account on every authenticated staff request.

**Stack**

.NET 10, ASP.NET Core MVC and Identity, Razor, and MongoDB replica-set transactions.

**Sources**

[Sprint 2 staff operations](2026-11-06-sprint-2-staff-operations.md), [RBAC and registered desktop PIN](../specs/2026-11-07-rbac-staff-authentication.md), and [project requirements](../PROJECT.md).

## Scope and proposed defaults

- Assign one workspace role from Staff, Manager, or Admin. **No staff access** removes the workspace roles. Preserve unrelated roles and customer account data, with Customer as the fallback for an otherwise empty role list. Keep existing account IDs, reservation ownership, and sign-in paths.
- Grant or change a workspace role only for an existing account with a verified email and a password it can use for PIN setup. Revocation works even when the target can no longer enroll. Keep registration, new employee account creation, whole-account disabling, and the activity log viewer outside this slice.
- Require a short reason of 1–500 characters after trimming and an explicit save. Block changes to the acting Admin’s own roles. A different signed-in Admin can make that change. Two Admins attempting to remove each other's access concurrently must leave one Admin in place.
- Rotate the target’s security stamp on an actual role change. The old staff session is rejected on its next request. Since existing PIN hashes incorporate that stamp, users who retain or gain staff access must use the existing PIN setup flow on an enrolled desktop. Show that consequence before saving. Existing Identity security-stamp behavior also applies to any customer session for the target.
- Keep role editing unavailable in PIN presentation mode. That mode pins identities to configured roles and omits PIN setup. Use the normal registered-desktop flow with isolated data for acceptance.

## Before implementation

- [x] Verify the in-progress calendar move work separately. Run `node --test tests/staff-move.test.cjs` and the targeted `ScheduleChangeTests` suite. Use a clean implementation branch based on the intended Sprint 2 baseline so the role work can be reviewed independently.

## Review focus

- A replayed or stale form must never overwrite a newer change or add a second audit event.
- A simultaneous attempt to remove both Admins must leave one of them with usable staff access.
- A failed audit insert must roll back the role change and session revocation.
- An employee who loses access must fail both staff sign-in and PIN setup while retaining customer account ownership.
- A Manager, an existing staff cookie after a role change, or a session in PIN presentation mode must be unable to save a role change.

## Task 1 — Provision and serialize role writes

**Files**

`HoleInHub.Data/Migrations/Definitions/017_StaffRoleAdministration.cs` (new), `HoleInHub.Data/Extensions/ServiceCollectionExtensions.cs`, `tests/HoleInHub.Tests.Integration/Migrations/MongoMigrationRunnerTests.cs`.

- [x] Add a migration test for both a fresh database and a database already on 016. Rerunning 017 must preserve the guard revision, existing accounts, and existing audit rows. Run the targeted migration tests to confirm the missing schema case fails.
- [x] Add and register migration 017. Create the `staff_role_audit` collection and seed one persisted guard document in `staff_role_guards` with ID `staff-role-administration`. Use idempotent operations. Leave earlier migrations intact.
- [x] Verify the new test and the migration runner suite. Run 017 through the leased Migrator on an isolated database before trying a role write. A missing guard must fail the feature closed, with no attempt to initialize it from the web app.

## Task 2 — Validate, commit, and audit role changes

**Files**

`HoleInHub.Business/Accounts/AccountService.cs`, `HoleInHub.Business/Accounts/StaffRoles.cs`, `HoleInHub.Data/Accounts/AccountPersistence.cs`, `HoleInHub.Data/Accounts/StaffRoleAuditRecord.cs` (new), `tests/HoleInHub.Tests.Unit/Accounts/StaffRoleRulesTests.cs` (new), and `tests/HoleInHub.Tests.Integration/Accounts/StaffRoleChangeTests.cs` (new).

**Contract**

`AccountService.ChangeStaffRoleAsync(StaffRoleChange command, CancellationToken ct)` returns `Task<StaffRoleChangeResult>`. The command carries actor ID and staff-session security stamp, target ID, expected target version, requested role (null for revocation), reason, and server correlation ID. The result distinguishes Applied, Unchanged, Invalid, Forbidden, NotFound, Stale, and Unavailable. Web obtains the actor and stamp from the authenticated staff principal.

- [x] Add failing tests for invalid roles, blank or oversized reasons, self-edit, unverified and passwordless targets, revocation from an ineligible target, unchanged roles, and preservation of unrelated roles. Add MongoDB tests for stale forms, a duplicate POST using the same version, simultaneous changes to one target, two Admins trying to demote each other, and a formerly authorized actor whose stamp changed.
- [x] Run the unit and integration tests with `--filter FullyQualifiedName~StaffRole` in their respective test projects. Confirm the new checks fail before adding the role-change behavior.
- [x] In Business, validate the command against `StaffRoles.Workspace`. Read the target and compare the submitted version before computing the new role array. A matching no-op must leave the target’s version, stamp, and audit history intact. A stale submission must report Stale even if the requested role now matches.
- [x] In Data, follow the existing `WithTransactionAsync` pattern. Increment the seeded guard before reading the actor. Recheck the persisted Admin role, verified email, lockout, and current session stamp. Apply the target update with an ID-and-version filter, touching only roles, version, and stamp. Use a fresh version and stamp for each actual change. Every normal staff-role write must use this transaction. The existing customer account updates already compare account versions, so an older profile write cannot restore superseded roles.
- [x] Append one audit event in the same transaction. Store event ID and type, UTC time, actor ID and role, target account ID, previous and new workspace roles, reason, and server correlation ID. Keep the event ID stable through driver retries. Do not record credentials, stamps, PINs, or cookie values. A version mismatch or failed audit insert must leave account state intact.
- [x] Verify the race tests against MongoDB. In the mutual-demotion test, exactly one change and one audit event must commit. Force the audit insert to fail in an isolated test and assert the target retains its roles, version, and stamp. Confirm replay produces no second event and an old profile write cannot overwrite the committed roles.

## Task 3 — Add the Admin form and verify sign-in effects

**Files**

`HoleInHub.Web/Controllers/LeadershipController.cs`, `HoleInHub.Web/Views/Leadership/Directory.cshtml`, `HoleInHub.Web/Views/Leadership/EditUserRole.cshtml` (new), `HoleInHub.Web/ViewModels/StaffRoleForm.cs` (new), and `tests/HoleInHub.Tests.Web/Smoke/StaffPinAccessTests.cs`.

- [x] Add failing HTTP tests for Admin GET and POST, anonymous and customer denial, Staff and Manager denial, missing antiforgery, presentation-mode denial, invalid form input, and stale form handling. Exercise a real registered-desktop staff cookie before and after a role change.
- [x] Add an Admin-only **Edit staff access** link to the existing directory, with no self-edit link. Add the account ID to the directory projection in Business and Data. Read the target’s version on the edit GET and pass it back in the form. Give the Manager team directory its existing read-only behavior.
- [x] Use GET and POST `/admin/users/{id}/role` with the existing Staff PIN policy, Admin requirement, and antiforgery. Disable both edit routes in presentation mode. Render the target’s name, email, current role, allowed role choices, reason field, and PIN setup consequence. Use a normal Razor form with keyboard-visible focus and readable validation. Take the target ID from the route and actor identity from the staff principal.
- [x] Redirect back to the directory with a success message after a commit. For a stale version, return a conflict with a link to reopen the form and review the current roles. Leave the submitted reason visible on validation errors. Show a recoverable error if the migration prerequisite is missing.
- [x] Verify the old staff cookie is rejected on the next request and the old PIN fails. A promoted employee can enroll a PIN on the registered desktop and receives only the new workspace permissions. A revoked employee cannot enroll a PIN or sign in to staff and still owns the same customer reservations. Cover a grant to a verified customer, Staff-to-Manager, Manager-to-Admin, and Admin-to-Staff across the integration and HTTP tests.

## Task 4 — Verify and prepare the walkthrough

- [x] Update `Documentation/ONBOARDING.md`, `Documentation/DATABASE.md`, and `Documentation/MIGRATIONS.md` with the new audit and guard collections, migration order, PIN re-enrollment, and the controlled recovery path for bootstrapping Admin access. Normal staff role changes go through Users & Roles so the guard and audit apply.
- [x] Run the repository checks below. For integration, use isolated local MongoDB databases and verify both fresh and migrated data. Check `/health/ready` on the configured isolated preview and verify that 017 is recorded before the walkthrough.
- [x] Walk through grant, PIN setup, role change with a session open, revocation, and denial from a Manager account. Check the saved audit events. Record the commit, commands, actual results, and any defects against the 19 November Sprint 2 milestone.

```powershell
dotnet build HoleInHubReservationManagementSubsystem.slnx
dotnet test HoleInHubReservationManagementSubsystem.slnx --filter "Category!=Integration"
docker compose -f docker-compose.mongo.yml up -d
dotnet test HoleInHubReservationManagementSubsystem.slnx --filter "Category=Integration"
npm --prefix HoleInHub.Web run build:css
npm --prefix HoleInHub.Web run test:ui
```

## Verification evidence — 10 November 2026

- The calendar work in the original checkout passed 11 drag-to-move tests and 15 schedule-change integration tests.
- The solution built with zero warnings and errors. The unit and web smoke suites passed 125 and 60 tests respectively. The CSS build and all 60 JavaScript UI tests passed.
- The isolated MongoDB run passed all 50 web integration tests and 116 of 117 data integration tests. The existing `GuestProofIsConsumedWithBookingAndRetryReturnsOriginal` case failed with the same allocation conflict in the original checkout at the baseline.
- The leased CLI applied migrations 001–017 on a fresh local database. A rerun applied zero migrations. The upgraded-database test verified that 017 preserves accounts, the guard counter, and existing audit entries. The isolated preview reported healthy at `/health/ready`.
- Registered-desktop HTTP coverage exercises role changes, revocation, PIN re-enrollment, stale forms, and denied actions. The follow-up live walkthrough is recorded below.
- Review added transactional PIN enrollment against the current account stamp, transaction contention on the acting Admin's account version, and a server-generated audit correlation ID.

## Live access walkthrough — 10 November 2026

The approved walkthrough ran against an isolated loopback HTTPS preview from the working tree based on `6be87b4`. The CLI applied migrations 001–017 to a fresh local replica-set database and the preview reported healthy.

- Admin password sign-in opened the workspace without a desktop certificate. Staff and Manager PINs signed in with the enrolled walkthrough certificate.
- The Admin revoked and granted Staff access, then promoted the employee to Manager while their Staff session was open. The old session ended and the old PIN was rejected. The employee enrolled a new PIN, signed in as Manager, and lost that active session after revocation.
- The Manager account received HTTP 403 from the Admin Activity Log. The Admin saw all four role changes in newest-first order, including the actor, affected account, previous and new roles, reason, and Manila timestamp.
- The solution and Tailwind builds passed. The checks passed 125 unit, 61 Web smoke, 118 data integration, 49 Web integration, and 60 JavaScript tests.

A second browser pass verified the styled Activity Log at desktop and 390 px widths, including a long wrapped reason, horizontal scrolling within the table, and visible keyboard focus.

The walkthrough used synthetic accounts and role changes on the local database. It covered staff access and the audit page.
