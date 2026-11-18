# Customer Profile Consolidation Implementation Plan

> **For agentic workers:** Use superpowers:executing-plans for inline implementation after owner review. Steps use checkboxes for tracking. Owner authorized implementation on 2026-11-11; implementation, local verification and owner-authorized development activation are complete.

**Goal:** Let staff combine duplicate staff-facing customer profiles and histories, with reviewed contact choices, audit history and guarded undo.

**Architecture:** Keep a stable staff customer profile and one explicit membership per reservation, separate from account ownership. The Business layer validates merge intent; narrow Data operations perform guarded MongoDB transactions. MVC renders a full-page comparison and uses the shared reason-confirmation modal for the final write.

**Tech Stack:** .NET 10, ASP.NET Core MVC, MongoDB replica set, Tailwind v4, existing local JavaScript.

**Spec:** `Documentation/specs/2026-11-11-customer-profile-consolidation.md` (read before implementation).

## Global constraints

- Merge staff-facing profiles and history only; the owner explicitly confirmed this boundary.
- Preserve reservation contact snapshots, AccountId/CustomerAccountId, access tokens, pricing, status, payment and allocations.
- Staff, Manager and Admin may use the staff-only flow. Every write requires anti-forgery and current staff authorization.
- All database/I/O methods accept `CancellationToken ct` and return Task/Task<T>.
- No generic repository, secondary database, new frontend dependency or runtime CDN.
- Explicit CLI migrations only; no changes to applied migrations or Web startup migration behavior.
- Reduced-motion mode snaps to final states. All initial content and no-JavaScript fallbacks remain usable.
- No automatic merge by name, email or phone; two profiles per comparison in the first release.

## Review focus

- A shared-email group may contain different people: Task 3 supports selecting specific bookings instead of moving the entire source.
- Concurrent A→B and B→A operations must not create cycles: Task 2 exercises persisted guards and revision conflicts.
- Customer-owned and guest-owned bookings must remain private after staff consolidation: Tasks 2 and 5 test the original ownership endpoints.
- New bookings after the comparison must not be silently merged: Tasks 2–3 validate membership revisions and require a renewed preview.
- Undo must not overwrite later merges/contact edits: Task 4 rejects changed semantic results and exposes subsequent operations in the history.

## Task 1 — Add stable profile storage and compatibility reads

**Files:**
- Create: `HoleInHub.Data/Customers/CustomerProfileRecord.cs`, `CustomerMembershipRecord.cs`, `CustomerProfileOperationRecord.cs`, `CustomerProfilePersistence.cs`.
- Create the next migration under `HoleInHub.Data/Migrations/Definitions/` (currently next is `018_CustomerProfiles`; recheck the manifest).
- Modify: `HoleInHub.Data/Extensions/ServiceCollectionExtensions.cs`, `HoleInHub.Business/Reservations/StaffCustomers.cs`, `HoleInHub.Data/Reservations/CustomerQueries.cs`.
- Tests: `tests/HoleInHub.Tests.Integration/Reservations/CustomerProfileTests.cs` and existing `CustomerQueryTests.cs`.

**Contracts:** Profile records have preferred contact fields, contact variants, contact/membership revisions and optional canonical destination. Membership is keyed uniquely by reservation ID. Define `CustomerProfileVersion(long Contact, long Membership)` in Business DTOs; map Data values rather than exporting Mongo types. Canonical reads return a stable profile ID plus the existing booking-reference context.

- [ ] Add failing isolated-database tests for unique membership, deterministic backfill, migration reruns and legacy booking-ID profile links.
- [x] Implement validators/indexes and an idempotent backfill that preserves the current account/guest/booking-contact groups without consolidating them further.
- [x] Resolve legacy staff links via membership; switch directory/history reads to canonical profiles with distinct reservation IDs and stable pagination.
- [x] Add contact-variant search so old names and phone/email variants still find the canonical row. Preserve the existing query timeout and literal-regex escaping.
- [x] Run the new migration and customer-query tests against an isolated local MongoDB replica set; verify no reservation ownership/contact fields changed.

## Task 2 — Implement merge preview and guarded commit

**Files:**
- Create: `HoleInHub.Business/Customers/CustomerProfileService.cs`, `CustomerMergeModels.cs`.
- Modify: `HoleInHub.Data/Customers/CustomerProfilePersistence.cs`.
- Tests: `tests/HoleInHub.Tests.Integration/Reservations/CustomerMergeTests.cs`.

**Interfaces:**
- `PreviewMergeAsync(string sourceId, string targetId, IReadOnlyList<string>? reservationIds, CancellationToken ct)` returns canonical profiles, contact variants, distinct booking counts, current `CustomerProfileVersion` values, and a bounded preview.
- `MergeAsync(CustomerMergeCommand command, string actorId, CancellationToken ct)` returns an operation receipt or a typed validation/conflict result. Command carries source/target IDs, both expected versions, preferred contact choices, null-for-all or explicit reservation IDs, reason and operation ID. Actor identity always comes from the authenticated server context.

- [ ] Add failing tests for full merge, selected-booking merge, missing contacts, duplicate retries, timeout-after-commit receipt lookup and a missing/invalid reason.
- [ ] Add race tests for two employees claiming the same source, opposite-direction merges, and a new membership added after preview.
- [x] Validate IDs, selection ownership, bounded write size, preferred contacts, reason and idempotency ID in Business. Return a conflict requiring renewed review when versions or counts differ.
- [x] Under the seeded persisted customer-membership guard, revalidate and commit memberships, preferred contacts, full-merge redirects and audit receipt in one transaction. Partial merges leave the source active.
- [ ] Test that cancelled/expired bookings, payment history, capacity allocations, guest links and account-owned reservation access are unchanged.

## Task 3 — Build the staff comparison and confirmation journey

**Files:**
- Modify: `HoleInHub.Web/Controllers/CustomersController.cs`, `HoleInHub.Web/Views/Customers/Details.cshtml`, `HoleInHub.Web/ViewModels/CustomerLabels.cs`, `HoleInHub.Web/Styles/staff-workspace.css`.
- Create: `HoleInHub.Web/ViewModels/CustomerMergeModel.cs`, `HoleInHub.Web/Views/Customers/Merge.cshtml`, `HoleInHub.Web/wwwroot/js/customer-merge.js`.
- Reuse: `HoleInHub.Web/Views/Shared/_ReasonConfirmation.cshtml`, `ReasonConfirmationModel.cs`, `wwwroot/js/reason-confirmation.js`.
- Tests: `tests/HoleInHub.Tests.Web/Smoke/CustomerMergeAccessTests.cs`, `tests/customer-merge.test.cjs`.

**Interfaces:** authenticated GET comparison and POST merge at `/staff/customers/profiles/{id}/merge`; POST accepts the Task 2 command fields and anti-forgery token. Successful writes redirect to the canonical profile with an operation receipt; conflicts render the comparison with an explicit reload/review action. Do not accept an arbitrary external return URL.

- [ ] Add access tests for Staff/Manager/Admin, customer/anonymous rejection, anti-forgery, stale previews and operation retries.
- [x] Add the profile action, record picker and aligned comparison, preserving the initiating profile/filter/page as the return context.
- [x] Implement preferred-field radio choices and the `Choose bookings` selection for ambiguous shared-contact groups. Server-render every preview total; JavaScript enhancements must not become authoritative for counts or membership.
- [x] Show the staff-only effect statement once in the final reason dialog. Cancelling keeps comparison choices; confirming performs one write. Preserve selections on a verified failure.
- [x] Add the combined-profile contact-variants disclosure and legacy source redirect behavior. Verify one canonical row and no duplicated history/counts.
- [ ] Check keyboard use, reduced motion, desktop/mobile layouts, empty search, long names, missing fields, stale/auth-expired and unavailable-server states.

## Task 4 — Ship merge history and guarded undo

**Files:**
- Modify: `CustomerProfileService.cs`, `CustomerMergeModels.cs`, `CustomerProfilePersistence.cs`, `CustomersController.cs`, customer profile view.
- Create: `HoleInHub.Web/Views/Customers/MergeHistory.cshtml`.
- Tests: extend `CustomerMergeTests.cs`, `CustomerMergeAccessTests.cs` and `customer-merge.test.cjs`.

**Interfaces:** `UndoMergeAsync(string operationId, CustomerProfileVersion expectedSource, CustomerProfileVersion expectedTarget, string reason, string commandId, string actorId, CancellationToken ct)` creates a new inverse operation; it never deletes the original audit entry.

- [ ] Add failing tests for successful full/partial reversal, repeated undo retries, later contact edits, later merges and new memberships.
- [x] Implement the inverse transaction only when semantic profile/membership results and operation lineage still match. Expose subsequent operations in history when undo is blocked.
- [x] Show actor, time, sources/target, reason, included booking count and current undo eligibility. Use the shared reason modal for confirmation.
- [x] Verify original booking contacts/statuses/account access remain unchanged through merge and undo.

## Task 5 — Keep newly created bookings associated correctly

**Files:**
- Modify the existing reservation creation orchestration in `HoleInHub.Business/Reservations/ReservationService.cs` and its actual partials/callers after tracing account, guest and staff creation paths.
- Modify the corresponding narrow Data transaction writers, `HoleInHub.Web/Views/BookingReview/_StaffBookingForm.cshtml`, `wwwroot/js/staff-booking.js`, and staff booking ViewModel.
- Tests: extend existing reservation/customer integration and staff-booking tests.

**Contract:** attach a new booking to its staff profile in the existing reservation transaction; never use a selected staff profile as proof of customer account ownership.

- [x] Trace every account, guest and staff reservation writer, including idempotent retries, and pin current public ownership/access behavior with tests.
- [x] Reuse immutable account identity where a stable account profile already exists. Add explicit profile selection for staff-created bookings; show enough contact context to disambiguate shared phones/names.
- [x] For guest/walk-in cases without a safe explicit association, retain separate reviewable records rather than guessing identity from matching contact values.
- [x] Increment membership revisions on association changes so an older comparison cannot silently include the new reservation. Verify booking idempotency and capacity behavior remain unchanged.

## Task 6 — Rollout and verification

**Files:** update `Documentation/DATABASE.md`, `Documentation/MIGRATIONS.md`, `Documentation/ONBOARDING.md` for the staff-only boundary and recovery process.

- [x] Build the solution, build CSS, run all JavaScript checks, non-integration .NET checks and isolated local integration tests. Do not substitute the shared Atlas database when local Docker is unavailable.
- [x] Verify migration reruns, a clean install and an upgrade with account/guest/walk-in duplicates. Record actual test evidence.
- [ ] Capture one desktop/mobile review batch and one correction-confirmation batch at most. Check the comparison, selected-booking flow, merged profile and undo conflict.
- [ ] Prepare the explicit migration command and intended-environment verification. Take the existing deployment backup before applying; do not run it as part of preparing this plan.
- [ ] Release merge and undo together. Roll back by stopping writes and restoring the tested backup/forward recovery path; do not remove memberships while this Web build is serving staff.

## Verification commands

```powershell
dotnet build HoleInHubReservationManagementSubsystem.slnx
npm --prefix HoleInHub.Web run build:css
node --test tests/*.test.cjs
dotnet test HoleInHubReservationManagementSubsystem.slnx --filter "Category!=Integration"
docker compose -f docker-compose.mongo.yml up -d
dotnet test HoleInHubReservationManagementSubsystem.slnx --filter "Category=Integration"
```

**Acceptance:** the explicit comparison yields one staff profile/history for a full merge, keeps individual bookings intact, preserves customer login/booking permissions, and supports an audited undo when no later operation conflicts. Suggestions never mutate records by themselves.

## Self-review

The tasks cover all eight journey steps and the spec's edge-case table. No account/ownership transfer, fuzzy automatic merge or bulk action is included. The current grouping is retained during backfill, partial selection handles shared-contact ambiguity, and guard/revision/idempotency tests cover races and retries. Review the preferred-field wording and selected-booking interaction before implementation.


## Verified execution and remaining activation

- [x] All six isolated merge tests pass, including partial membership, stale previews, opposite races, idempotent merge/undo, linked-booking staleness, legacy/new guest separation and reversed dependency chains.
- [x] Staff/Manager/Admin merge/undo HTTP tests and anti-forgery checks pass; original account-owned history stays separate.
- [x] Browser comparison, contact choices, selected-booking counts, zero-selection disablement, confirmation cancellation, test merge, variants/history and audited undo verified. One layout correction batch applied and confirmed.
- [x] Clean solution/CSS builds; 80 JavaScript checks and all 366 .NET tests pass (187 non-integration, 179 integration).
- [x] Migration 018 explicitly applied to isolated local MongoDB; idempotent backfill and upgrade tests pass.
- [ ] Native browser saved-password autofill and the full outage/auth-expiry/reduced-motion interaction matrix remain manual follow-ups; filled labels and account heading gaps were inspected.
- [ ] Owner is configuring the separate Atlas migration credential privately. Preserve the healthy existing preview until configuration is confirmed ready.
- [ ] Maintainer backup, explicit migration 018, coordinated Web refresh and shared readiness/ownership spot-check. No shared customer merge was executed.


## Development activation — 11 November 2026

Owner explicitly authorized migration 018 on the database used by port 5275 with its current connection. Backup restore verification succeeded (223 documents). The explicit CLI applied 018 only; account/reservation fingerprints and 23 original bookings were unchanged. The restarted preview is ready and its Merge customer action and picker were verified through the browser. No shared customer merge was executed. Earlier pending-activation notes above describe the pre-approval state.
