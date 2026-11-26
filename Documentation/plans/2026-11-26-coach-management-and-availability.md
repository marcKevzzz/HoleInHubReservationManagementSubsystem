# Coach management and availability implementation plan

> For implementation: use the executing-plans workflow task by task. This document is a plan, not evidence that the changes have shipped.

**Goal:** Improve the existing coach directory/editor and complete verified coach-and-bay availability without replacing working reservation mechanisms.

**Architecture:** Keep HTTP, permissions, upload handling and Razor in Web; eligibility and scheduling policy in Business; MongoDB documents, guarded transactions and indexes in Data. Extend existing narrow operations rather than adding a repository or parallel scheduling subsystem.

**Stack:** .NET 10, ASP.NET Core MVC, MongoDB, Tailwind CSS v4 and locally bundled GSAP.

**Sources:** `AGENTS.md`, `Documentation/PROJECT.md`, `Documentation/ONBOARDING.md`, `Documentation/specs/2026-10-18-production-foundation.md`, `Documentation/plans/2026-11-14-admin-booking-configuration.md`, and `Documentation/plans/2026-11-23-kpga-group-family-bookings.md`. Current owner directions outrank older plans. The November 14 plan's service-as-coach workaround is superseded by the named Philippine coach implementation.

## Verified starting point — 26 November 2026

The current checkout is `main` and contains substantial uncommitted resource, media, validation and presentation changes. Preserve them. In particular, retain the coach silhouette and empty-state border removal in `Views/Coaches/Index.cshtml` and `_StaffIcon.cshtml`. Do not revert shared service styles, gallery components or booking code to HEAD to begin this work.

- `HoleInHub.Web/Controllers/CoachesController.cs` already exposes `/staff/coaches`, `/new`, `/{id}/edit`, `/save` and booking assignment routes. All use the StaffWorkspace policy; Staff, Manager and Admin currently manage coaches. POSTs have anti-forgery protection.
- `ViewModels/CoachEditorForm.cs` and `Views/Coaches/Edit.cshtml` already implement name, optional professional title/phone/email, Active, revision checks, multiple weekly shifts, dated exceptions, row add/remove, field errors, stale-edit feedback and affected-booking links. They use the service-editor shell. There is no coach photo, biography or supported-service list today.
- Business `Reservations/Coaches.cs` provides `CoachesAsync`, `SaveCoachAsync`, `AvailableCoachesAsync`, `AssignCoachAsync`, `ValidateCoach` and `CoachDto`. `CoachDirectory.cs` computes the next free period over 30 Manila calendar days.
- Data `Reservations/Coaches.cs` stores `CoachRecord`, weekly minute ranges and dated overrides. Overrides replace weekly hours for that date. Blank start/end means a full day off. Adjacent shifts can cover a continuous session; gaps cannot.
- `027_CoachDirectory` adds reservation coach fields/history event support, the unique coach-id index, and an assignment-range index. It does not implement photos, service eligibility, or a complete coach schema validator. Do not modify its applied checksum. Migrations currently extend through 030; 031 is only the tentative next number and must be rechecked at implementation.
- Philippine coach scheduling is controlled by `ReservationPolicy.PhilippineCoachSchedulesEnabled`. Readiness requires 027 when enabled. A healthy endpoint alone does not prove that this flag is enabled or that a roster is populated.
- `ReservationService.AvailableAsync` requires a suitable bay and eligible coach when the flag applies. `ReservationPersistence.InsertAsync` claims the coach and bay in the same transaction. Automatic coach choice is the first eligible coach by ID; no customer coach selector currently exists.
- `ClaimCoachAsync` updates `allocation_guards` at `coach:{id}:{Manila-date}`. Coach saves and assignments also acquire the catalog guard. Preserve these shared guards; do not replace them with a read-then-write check.
- Holds derive from reservation status/deadlines, not a separate coach allocation collection. Confirmed/Checked-In and unexpired ordinary Pending occupy the coach. Overdue Pending no longer holds capacity. Assignment, late approval and staff rescheduling already recheck coaches; cancellation and terminal outcomes stop occupying them through the common hold predicate.
- Existing future Philippine bookings without a coach fail closed for overlapping new availability. Staff assignment is available for resolving those bookings.
- KPGA fixed classes/packages reserve physical bays and learner seats, but named-coach conflict checks currently filter `Service == PhilippineCoach`. Do not claim cross-program coach protection.

Resource default semantics are confirmed in Business `ResourceAdministration.cs`: `Location ?? fallback` and `Description ?? fallback` supply defaults only for null/missing values. Empty strings remain empty; presentation saves trim whitespace to empty. `Images == null` means unsaved presentation, while an empty array means explicitly cleared. Do not turn intentional clears into mock data or infer coach data from these defaults.

Latest observed validation before this planning task: build 0 errors/warnings; 260 unit and 170 non-integration web tests passed. Local replica-set integration tests ran despite Docker being unavailable: 190 data/integration and 84 web integration tests passed, two web tests failed and one was skipped. Failures were `AccountIdentityTests.VerificationResetLockoutAndRolesUsePersistedIdentity` and the KpgaGroup approval case in `GuestBookingFlowTests`. They remain baseline issues, not a clean release. Readiness was rechecked during planning and reported Healthy. No application code, database configuration or migration is changed by this plan.

## Decisions and boundaries

| Decision | Recommended implementation baseline | Approval/dependency |
| --- | --- | --- |
| Management roles | Preserve Staff/Manager/Admin initially; retain separate staff booking-assignment access | PROJECT explicitly grants staff profile/schedule management. Manager/Admin-only profile edits are a proposed policy change, not yet approved. If selected, apply action-level authorization, hide edit controls for Staff, retain Staff directory/assignment and test direct POSTs. |
| Customer coach choice | Continue automatic eligible assignment, with staff reassignment | Customer selection, fairness/round-robin or preferred coaches require a separate decision. Do not add them implicitly. |
| Supported services | First release explicitly identifies Philippine Coaching; preserve existing eligibility | Multi-service assignment is not an existing field. Enabling KPGA needs Task 5 and agreement on which coaches teach which offerings. A disabled/informational label must not imply enforced cross-program eligibility. |
| Duration and lead time | Retain service-specific rules, 30-minute minimum/increments for ordinary sessions and full 60 operating-minute coaching review window before start | Use existing service rules and `BookingRules`/`ReviewClock`, not new coach-specific settings. Fixed classes/packages retain published durations and review rules. |
| Buffers | Keep current zero additional buffer | No approved coach travel/preparation buffer was found. Any nonzero setting needs a duration, before/after semantics, and effect on existing bookings before implementation. |
| Booked-session changes | Reject schedule reductions/deactivation that strand current holding bookings; show affected bookings and reassign first | No automatic cancellation, silent reassignment or forced override. Preserve existing status/revision/history and confirmation rules. |
| Blank profile fields | Blank optional biography/photo remains blank; no fabricated coach descriptions/photos | Missing service eligibility, if added, defaults only to the historical Philippine behavior. Empty eligibility must not accidentally widen availability. |

## Global constraints and review focus

- Preserve Web → Business → Data, asynchronous I/O with cancellation, UTC instants and Asia/Manila date interpretation.
- Use `[start, end)` and persisted catalog/resource/coach guards; retain atomic all-or-nothing bay and coach acquisition, idempotency and stale revision checks.
- Reuse service/bay editor sections, Active switch, upload validation, scheduling disclosures, field errors, conflict lists and tokens. No new visual system, CDN, hidden initial content or animation dependency.
- Keep profile contact information private to authorized staff. Public calendars expose availability, not staff contact details.
- Verify midnight end times, null-versus-empty edits, overlapping submissions, stale calendar results, and availability edits racing with bookings. The tasks below explicitly cover these conditions.
- Preserve the launch environment on port 5275. Never use its database for integration fixtures. No migrations on startup or changes to applied migration files.

## Task 1 — Confirm behavior and protect the existing baseline

**Files:** `tests/HoleInHub.Tests.Unit/Reservations/CoachDirectoryTests.cs`, `tests/HoleInHub.Tests.Integration/Reservations/CoachAllocationTests.cs`, new `tests/HoleInHub.Tests.Web/Smoke/CoachManagementTests.cs`, existing `HoleInHub.Web/browser-tests/coaches.spec.cjs`.

**Interfaces retained:** `Task<IReadOnlyList<CoachDirectoryEntry>> CoachDirectoryAsync(CancellationToken ct)`, `Task<bool> SaveCoachAsync(CoachDto coach, long? expectedRevision, CancellationToken ct)`, and `Task<bool> AssignCoachAsync(string reservationId, long revision, string coachId, string actor, CancellationToken ct)`.

- [ ] Record the role decision above before narrowing access. Pin current Staff/Manager/Admin permissions, anonymous/customer denial, anti-forgery rejection and assignment access in HTTP tests.
- [ ] Add focused regression cases for a full-day exception, a split-shift break, adjoining windows, obsolete revision and a deactivation conflicting with a future Pending/Confirmed booking.
- [ ] Run the existing coach unit/integration/browser tests before editing behavior. Keep the two unrelated integration failures separately identified.
- [ ] Confirm effective scheduling-flag presence, migration 027 and roster/legacy-assignment counts without printing secrets. Read-only checks only on the configured preview.

## Task 2 — Reuse the editor and add an optional profile photo

**Files:** Web `Controllers/CoachesController.cs`, `ViewModels/CoachEditorForm.cs`, `Views/Coaches/Index.cshtml`, `Views/Coaches/Edit.cshtml`, `Styles/service-admin.css`, `wwwroot/js/coach-editor.js`; Business `Reservations/Coaches.cs`; Data `Reservations/Coaches.cs`; existing media helpers `ViewModels/EditorImageUploads.cs`, Business `Services/ServiceCatalogManagement.cs` and Data `Services/ServiceCatalogManagement.cs`.

**Proposed contract:** Add optional `PhotoPath` and `Biography` init properties to `CoachDto`/`CoachRecord`, plus `CoachPhotoEdit(bool Remove, byte[]? Upload)`. Extend `SaveCoachAsync` with a trailing optional `CoachPhotoEdit? photo = null` and extend Data save with optional prepared image records. Existing callers remain valid. Reuse the established byte validation/preparation and image-serving path after confirming it accepts a coach owner key; keep policy in Business and storage transactional in Data.

- [ ] Add a failing integration test for valid photo save/removal and stale-revision failure preserving the previous photo and schedule. Invalid file content and oversize input must be rejected, regardless of filename/MIME.
- [ ] Reshape the existing edit page into the existing two-column photo/details composition, description below, and scheduling disclosures beneath. Retain title, phone/email, Active, Cancel/Save, non-JavaScript row actions and all conflict/error output.
- [ ] Use one optional JPEG/PNG/WebP photo, maximum 4 MB, matching existing validation. Do not add coach videos or external URLs. Define biography as optional, plain text, maximum 500 characters; HTML remains encoded.
- [ ] Add table/directory photo or existing coach-icon fallback, name/title, status and clearly labeled next coach-free period. It is not a promise that a bay or service slot is available.
- [ ] Retain replacement/removal values through invalid saves safely; never trust a submitted existing photo path without ownership validation. Store image and coach mutation together; do not delete a previously referenced image before commit.
- [ ] Add a new additive migration only where validator/index/storage changes require it, using the next free ID. Never rewrite 027. Verify old documents remain readable and startup readiness checks the new schema when necessary.
- [ ] Verify desktop/mobile, keyboard, reduced motion, unauthorized upload attempts and no-JavaScript submission using existing coach tests.

## Task 3 — Complete schedule editing, especially midnight

**Files:** Web `ViewModels/CoachEditorForm.cs`, `Controllers/CoachesController.cs`, `Views/Coaches/Edit.cshtml`, `wwwroot/js/coach-editor.js`; Business `Reservations/Coaches.cs`; Data `Reservations/Coaches.cs`; corresponding unit/web tests.

**Contract:** Keep persisted start/end integer minutes and existing weekly/override DTOs. Add `EndsAtMidnight` to `CoachWeekRow` and `CoachDateRow`. Render a clear end-of-day choice; `From` maps EndMinute 1440 to that choice, and save maps it back to 1440. Normal time inputs continue representing 0–1439.

- [ ] Reproduce the existing 1440 → `TimeOnly.MinValue` → 0 round-trip failure. Test 23:30–midnight save/reopen, touching windows and rejection of start >= end, negative minutes and end > 1440.
- [ ] Reuse multiple windows for breaks. Explain that date exceptions replace all weekly windows for that date, and blank start/end means day off. Do not add a second break/time-off collection.
- [ ] Reject a partial pair, overlapping windows and day-off plus another window on the same date. Preserve invalid values and focus/error associations during row add/remove.
- [ ] Keep ordinary booking intervals within venue opening–midnight. Do not enable overnight coaching merely because the directory can display adjoining shifts across dates.
- [ ] If directory availability policy is moved out of Data, move only the pure window/coverage decision into Business and pass it into guarded Data operations. Do not broaden this into a repository rewrite.

## Task 4 — Verify and harden the existing Philippine booking lifecycle

**Files:** Business `Reservations/ReservationService.cs`, `Coaches.cs`, `CoachDirectory.cs`, `Models.cs`, `ReviewClock.cs`, `ScheduleChanges.cs`; Data `Reservations/Coaches.cs`, `ReservationPersistence.cs`, `BookingPersistence.cs`, `ScheduleChanges.cs`, `CalendarAvailabilityQueries.cs`; integration tests and `browser-tests/coaches.spec.cjs`.

**Interfaces retained:** existing availability, insertion, assignment and schedule-change operations. Customer input does not acquire a coach directly; Business retains automatic assignment and Data commits the claim atomically.

- [ ] Test no coach/available bay, available coach/no bay, whole-shop/class bay holds, service closures, full-duration coverage, exact boundaries and one-minute overlaps.
- [ ] Race two requests on different bays with one coach: exactly one may hold the coach. Race with two coaches: both may succeed using distinct coaches. Duplicate submission returns one booking and one effective hold.
- [ ] Test cancellation, decline, overdue hold release, final expiry, late approval reacquisition and current-session protection. Preserve historical coach identity when occupancy ends.
- [ ] Test reassignment against another submission, deactivation against insertion, schedule reduction against insertion and two stale edits. Failed operations must preserve both the previous coach and bays.
- [ ] Test moves across Manila dates and failed rescheduling preserving the original allocation. Keep old/new guard ordering stable; document catalog serialization and retain it unless a measured change is justified.
- [ ] Verify existing future unassigned Philippine sessions keep new overlapping sales closed until staff resolve assignments. Feature disablement must not erase assignments or silently free existing holds.
- [ ] Add only fixes demonstrated by these failing tests; do not rebuild working availability. Confirm staff still records coach confirmation plus venue approval.

## Task 5 — Gated extension: named coaches across services and class packages

This is a separate releasable slice, not a checkbox in the profile UI. It is gated on the owner confirming eligible service offerings, whether one coach can teach a shared class while overseeing other sessions (recommended: no), and whether a whole-shop event itself needs a coach.

**Files to extend after that decision:** Business `Reservations/Coaches.cs`, `FamilyClasses.cs` and class/package publication orchestration; Data `Reservations/Coaches.cs`, class-session/package records and publication persistence; service catalog DTOs; class/coach controllers/views; migration definitions and concurrency tests. Resolve exact class-publication files before execution rather than introducing parallel persistence.

- [ ] Define eligibility by stable service offering IDs, not display names. Historical coaches retain Philippine eligibility; empty selection does not mean all services. Changes affecting booked commitments are rejected with affected-session links.
- [ ] Assign a coach to the class occurrence once, not once per learner/registration. Published occurrences hold coach and bay together, including classes with no enrolled learners while still published.
- [ ] Acquire the same coach/date guards for private lessons and class publication/rescheduling. Package publication must validate every included date; partial success is forbidden.
- [ ] Ensure learner cancellation changes seats only; it does not release the class's coach. Occurrence cancellation/reassignment handles coach release/history once.
- [ ] Test Philippine versus KPGA overlap, two programs sharing a coach, multi-date package rollback, publication versus time off and removal of eligibility while future classes exist.
- [ ] Add the required additive validator/index migration and rollout preflight before enabling cross-service scheduling. Keep this disabled until those checks and business decisions are complete.

## Task 6 — Verification, rollout and handoff

- [ ] Run `dotnet build HoleInHubReservationManagementSubsystem.slnx` and `dotnet test HoleInHubReservationManagementSubsystem.slnx --filter "Category!=Integration"`.
- [ ] Start local MongoDB with `docker compose -f docker-compose.mongo.yml up -d`; if Docker remains unavailable, verify the already installed local replica set before using the fixture's isolated databases. Run `dotnet test HoleInHubReservationManagementSubsystem.slnx --filter "Category=Integration"`. Never substitute Atlas/shared preview data.
- [ ] Run `npm --prefix HoleInHub.Web run build:css`, relevant JavaScript tests, and `npx playwright test browser-tests/coaches.spec.cjs` from Web with the documented test fixture. Record the actual output and remaining baseline failures.
- [ ] Use the repository db-migration workflow for any new schema: test fresh and upgrade paths, rerun/checksum behavior, backup the selected deployment database, stop incompatible writers, and execute `dotnet run --project HoleInHub.Migrator -- migrate` under its existing lease. Do not apply migration/schema changes during this planning task.
- [ ] Preserve the configured preview process environment before a rebuild/restart. Verify `/health/ready`, a known available session, an unavailable session and a coach change that conflicts with an existing booking.
- [ ] Before enabling schedules, verify roster coverage and resolve future unassigned bookings. Do not populate real coaches with invented names, schedules or contact data.
- [ ] Update `PROJECT.md` only for approved policy changes; update onboarding, migration notes and testing evidence for delivered behavior. Record that disabling the feature is not equivalent to a safe rollback of stored commitments.

## Recommended first implementation slice

Complete Tasks 1–4 using current Philippine Coaching policy: refine the existing editor, optional photo/profile, midnight-safe availability editing, and the lifecycle/race tests. Keep automatic coach assignment and zero extra buffer. Decide Staff versus Manager/Admin management before changing permissions. Treat cross-program coach eligibility and allocation as Task 5, with its own agreed scope and rollout.

No new coach implementation was made while preparing this plan. Do not interpret the existing implementation findings as fresh verification of every lifecycle path; the task-specific tests above are required evidence.

## Implementation progress — owner-authorized expanded scope

The owner explicitly requested coach photos, service-to-coach assignments, Philippine/KPGA Pro/junior availability, and clearly identified mock Philippine/KPGA coaches in Atlas. This supersedes the earlier Philippine-only implementation gate. Preserve existing permissions and automatic assignment; service selection defines the eligible pool. Allocate a coach once per class occurrence, not once per learner enrollment. No additional buffers are introduced.

Implemented so far: midnight end-time round-trip and three passing regression tests; transactional profile-photo storage using existing validated service media; service CoachIds persistence and editor selection with directory validation. Release solution build passed. These changes are not yet deployed to preview.

Remaining: apply service eligibility to availability and guarded allocation across individual and class sessions; protect class commitments on coach edits; migration/backfill compatibility; tests for photos, service assignment and cross-service concurrency; explicit Atlas mock creation; preview restart preserving environment and required verification.
