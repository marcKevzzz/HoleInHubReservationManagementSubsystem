# Staff Check-in / Check-out Implementation Plan

> **For agentic workers:** Use superpowers:executing-plans to implement this plan task by task after the owner approves implementation. Checkboxes represent planned work, not completed features.

**Goal:** Staff and Admin can record arrival and departure, immediately release unused time on early departure, and retain a reliable attendance and reservation history.

**Architecture:** Extend the existing reservation lifecycle with narrow Business attendance commands and guarded MongoDB transitions. Reuse staff booking details and the schedule; preserve original scheduled intervals and store actual attendance separately. Add a reservation revision and command identity so stale pages and uncertain retries cannot change attendance twice.

**Tech stack:** .NET 10 MVC, MongoDB.Driver transactions, Razor, existing Tailwind/staff CSS and locally bundled JavaScript. No new dependencies.

**Spec:** Owner decisions in this chat, 7 November 2026; [PROJECT.md](../PROJECT.md); [Sprint 2 staff operations](2026-11-06-sprint-2-staff-operations.md); [foundation specification](../specs/2026-10-18-production-foundation.md).

## Owner-approved policies

1. Check-in is permitted from scheduled start until scheduled end: `StartUtc <= now < EndUtc`.
2. Early check-out immediately releases the remaining bay allocation.

## Proposed workflow and implementation decisions

| Current state | Staff action | Result | Capacity effect |
| --- | --- | --- | --- |
| Confirmed, within check-in window | Confirm check-in | Checked-In | Keep all booked bays held through the original scheduled end |
| Checked-In | Confirm check-out | Completed | Release all remaining capacity immediately |
| Any other state | Check-in or check-out | Reject; show current state | Preserve allocation and audit |

- Staff and Admin have the same attendance permissions. Customers and anonymous visitors have none.
- Check-out is the staff record that the customer has finished and all booked bays are clear. For a multi-bay or whole-shop booking, it applies to the entire booking; individual-bay departures are deferred.
- Require explicit confirmation for both actions. Check-in notes are optional. An early check-out requires a reason of 1–500 trimmed characters; ordinary check-out notes are optional, maximum 500 characters. The owner approved this planned default by authorizing implementation.
- A pending cancellation must be resolved through the existing cancellation controls before check-in. Do not silently decline it through arrival.
- Coaching still requires recorded coach confirmation, and whole-shop attendance requires the existing verified/demo deposit state. Attendance must never create or change payment evidence.
- Late arrival does not move `EndUtc`. Check-out after scheduled end is permitted, records actual departure, and does not retroactively extend capacity. Checked-In status alone never holds beyond the scheduled end.
- Preserve `StartUtc` and `EndUtc`. Store nullable `CheckedInUtc` and `CheckedOutUtc`; display scheduled and actual times separately in Manila time.
- Legacy Checked-In records without actual arrival time remain readable and can be checked out. Show arrival as “Not recorded”; never invent a timestamp.
- At `min(StartUtc + 1 hour, EndUtc)`, an unattended Confirmed booking gets “Needs follow-up”. This is a derived flag, not an automatic expiry or no-show. Check-in remains allowed after the flag until scheduled end, unless staff have already changed the booking state.
- Manual No-show writes, attendance corrections, automatic check-out, extensions, reassignment, partial departures, payments and new notification types are deferred. Cancellation remains separate.

## Global constraints

- Web → Business → Data; no MongoDB access from controllers.
- UTC storage, Asia/Manila presentation, half-open intervals `[start, end)`.
- Staff/Admin authorization, anti-forgery and server validation on every write; actor comes only from authenticated claims.
- Reuse allocation guard documents, MongoDB transactions and bounded cancellation-aware I/O. Never release by deleting a reservation.
- Never alter applied migrations or run migrations on Web startup.
- Keep all initial content usable without JavaScript; preserve keyboard focus, reduced-motion behavior and mobile list fallback.
- Reuse the current checkout and preserve unrelated uncommitted work. No shared-database migration, preview replacement, commit or push is implied by this plan.

## Review focus

1. Check-in racing with cancellation, another attendance action, or a new allocation: exactly one valid transition commits.
2. A second staff member acts from an old page: stale revision yields a reload message without side effects.
3. A network retry repeats a successful command after later state changes: recognize the original command without reopening capacity or duplicating history.
4. Early completion remains visible as a past activity but no longer blocks drag selection, whole-shop coverage or availability.
5. Missing legacy audit, Manila midnight, exact start/end boundaries and a departure after scheduled end must not invent times or extend bookings.

## Task 1: Add attendance audit and concurrency metadata

**Files:**
- `HoleInHub.Data/Reservations/Records.cs`
- New `HoleInHub.Data/Migrations/Definitions/013_ReservationAttendance.cs` (verify next ID at implementation time)
- `HoleInHub.Data/Extensions/ServiceCollectionExtensions.cs`
- `HoleInHub.Business/Reservations/Models.cs` and mapping in `ReservationService.cs`
- Existing reservation schemas in `Documentation/schemas/`, `Documentation/DATABASE.md`
- Migration tests under `tests/HoleInHub.Tests.Integration/Migrations/`

**Interfaces/data:** `ReservationRecord.Revision : long` (initial 0), `CheckedInUtc : DateTime?`, `CheckedOutUtc : DateTime?`. Expose the three values on `ReservationDto`. Add optional `CommandId`, `CommandHash` and `PreviousReservationStatus` to `HistoryRecord` for attendance events. Add `ReservationCheckedIn` and `ReservationCompleted` to the allowed event types; retain existing status identifiers `Checked-In` and `Completed`.

- [x] Write migration tests: populated legacy records retain their states, scheduled times and history; missing revision becomes 0; absent arrival remains absent; invalid timestamp types/revision values fail; event types and command metadata are accepted; rerun is safe.
- [x] Run those tests and observe the missing migration/field failure.
- [x] Implement an additive migration with preflight validation and published-schema parity. Do not backfill attendance timestamps from assumptions.
- [x] Map the new values through Business DTOs; keep existing constructors/callers compatible.
- [x] Run migration/schema tests against an isolated local database.

## Task 2: Guarded attendance commands and stale-write protection

**Files:**
- New `HoleInHub.Business/Reservations/AttendanceRules.cs`, `StaffAttendance.cs`
- New `HoleInHub.Data/Reservations/AttendancePersistence.cs` (same narrow partial persistence type)
- Existing `BookingPersistence.cs`, `ReservationPersistence.cs`
- `BookingReviewController.cs` / existing decision forms for revision propagation
- Unit/integration tests under `tests/HoleInHub.Tests.{Unit,Integration}/Reservations/`

**Business interface:**
`AttendanceCommand(string ReservationId, string Action, long ExpectedRevision, string CommandId, string? Reason)`.
`ReservationService.RecordAttendanceAsync(AttendanceCommand command, string actor, CancellationToken ct) : Task<AttendanceResult>`.
`Action` accepts only `check-in` or `check-out`. `AttendanceResult` distinguishes `Applied`, `AlreadyApplied`, `Stale`, `NotFound`, and `NotEligible`. Invalid request data produces the existing validation exception. Generate a new Guid in N format for each rendered action; require that format server-side and retain it when validation fails. Compute SHA-256 over stable UTF-8 JSON containing ReservationId, Action, ExpectedRevision, authenticated Actor and trimmed Reason (empty when absent); exclude server time so an uncertain retry has the same fingerprint.

**Persistence boundary:** Add `AttendanceChange(string Status, string EventType, DateTime AtUtc, DateTime? CheckedInUtc, DateTime? CheckedOutUtc, bool CheckAllocation)` in Data. The narrow persistence operation receives the reservation ID, expected revision, command ID/hash, actor, normalized reason, `Func<ReservationRecord, AttendanceChange?> decide`, and cancellation token. It returns an `AttendanceWriteResult` with the same five outcomes mapped to the Business `AttendanceResult`. Invoke `decide` only after reading and guarding the current reservation and validating replay/revision; Business owns this callback and reads the current clock there. Null means NotEligible. Data checks conflicts when `CheckAllocation` is true, and atomically applies the proposed values/audit. The callback never writes to the database or emits notifications. This keeps time/service policy in Business and transaction mechanics in Data without exposing MongoDB types.

- [x] Write pure policy tests using a fake clock: one tick before start rejected; exact start accepted; exact end rejected; check-in after grace but before end accepted; invalid states/payment/coach prerequisites rejected; pending cancellation rejected; early departure requires reason; legacy Checked-In checkout allowed; actual checkout never edits scheduled end.
- [x] Write integration tests for stale pages, duplicate commands and concurrent check-in/check-out/cancellation; completion versus new single/multi-bay and whole-shop allocations; conflicting block or inactive resource on arrival; actor/timestamps/reason and scheduled interval preservation.
- [x] Run the focused tests and observe failures before implementation.
- [x] In the transaction, read the booking, contend on its existing catalog/resource/date guards, recheck current state, enforce expected revision and validate eligibility against the current server time. Check for the same committed command before rejecting a stale revision.
- [x] Store one history event with actor, actual UTC time, before/after status, normalized reason, command ID and a canonical payload hash. A matching replay returns AlreadyApplied and current state; the same command ID with a different actor/action/payload fails. A new command from a stale page does not silently become a retry.
- [x] Atomically write state, actual attendance time, history and revision increment. Keep payment data and scheduled timestamps intact. Audit checkout before releasing capacity through Completed status.
- [x] Increment revision in every existing reservation writer: approval/rejection/coach/cancellation, expiry and demo payment. Existing staff decision forms send expected revision; retain current customer/worker interfaces where they reread within their transaction. Increment once per committed state/audit mutation; preserve the existing replay/no-op paths without an extra revision increment.
- [x] Reuse existing overlap checks excluding the booking itself on arrival. Fail safely on conflicting allocations, blocks or inactive resources. Do not partially check in/out a multi-bay reservation.
- [x] Add no new attendance emails: approval/cancellation outbox behavior remains covered by regressions.
- [x] Run policy, allocation, booking process, expiry, payment and outbox tests.

## Task 3: Staff detail actions and truthful schedule display

**Files:**
- `HoleInHub.Web/Controllers/BookingReviewController.cs`
- New `HoleInHub.Web/ViewModels/StaffAttendanceForm.cs`
- `HoleInHub.Web/ViewModels/StaffReviewModels.cs`, `StaffTimeline.cs`
- `HoleInHub.Web/Views/BookingReview/Details.cshtml`, `Index.cshtml`
- `HoleInHub.Business/Reservations/StaffSchedule.cs`
- `HoleInHub.Data/Reservations/BookingPersistence.cs` schedule query
- `HoleInHub.Web/wwwroot/js/staff-booking.js`, `staff-schedule.js` only where needed
- `HoleInHub.Web/Styles/staff-workspace.css`, `ViewModels/ReservationLabels.cs`
- Web, timeline and JavaScript regression tests

**HTTP:** Staff/Admin-only POST `/staff/reservations/{id}/attendance`, with anti-forgery, Action, ExpectedRevision, CommandId, Reason and explicit confirmation. Render validation in the existing detail page; use POST/redirect/GET after a committed action. Missing booking returns 404; stale/invalid transitions show current state and require a fresh review.

- [x] Add HTTP tests denying anonymous/customer access, missing anti-forgery, forged actor/status/time, unchecked confirmation and invalid action; verify validation retains entered reason and successful repeats keep one audit event.
- [x] Add “Check in” to eligible Confirmed booking details and “Check out” to Checked-In details. Show a clear consequence and a confirmation step, including remaining time/all allocated bays for early departure. Keep payment checkout visibly distinct from attendance check-out.
- [x] Show scheduled session, actual arrival/departure and “Needs follow-up” eligibility. Before start, show when check-in opens; after end, do not offer check-in. Client updates are enhancement only; the server always rechecks time.
- [x] Retain Completed records in the day view with a clearly completed treatment and original/detail links. Show actual departure; do not make an early-completed session disappear from the day's operational record.
- [x] Distinguish visible history from held capacity: only Pending/Confirmed/Checked-In and active blocks affect reserved peak, busy drag ranges and venue-buyout coverage. Completed entries must not block client selection even while visible. Preserve customer/guest ownership queries and terminal-status history.
- [x] Test completed row visibility, zero reserved capacity after early completion, nonblocking completed bars, multi-bay/buyout behavior, late departure and Manila-midnight boundaries.
- [x] Verify desktop/mobile, keyboard confirmation/focus, screen-reader labels and no-JavaScript behavior against isolated data after readiness is healthy.

## Task 4: Verification and walkthrough

- [x] Build the full solution and run unit/non-integration, isolated MongoDB integration, JavaScript and CSS checks from AGENTS.md. If a configured preview locks outputs, use separate build artifacts and run Migrator CLI unit tests in their expected conventional output layout.
- [x] Demonstrate: confirmed → check in → early check out → new booking succeeds in freed time; original schedule and actual audit remain visible.
- [x] Demonstrate: a second staff page is stale; duplicate POSTs preserve first audit; cancellation versus arrival has one winner; pending/unpaid/unapproved bookings cannot arrive; follow-up does not automatically mark no-show.
- [x] Review legacy data and schema upgrade/rerun, authorization, UTC/Manila boundaries, duplicate history/outbox safety and the completed schedule/drag distinction.
- [x] Record fresh evidence and independent review before completion. Apply migration explicitly only to the intended configured database during an authorized rollout; avoid mixed old/new writers after introducing revision checks.

## Implementation status

Implemented after owner authorization. Early-departure reasons and the deferred manual no-show slice follow the approved plan. Migration 013 has run only in disposable test databases; the configured Web process and shared database are untouched.


### Plan self-review
- All approved timing/capacity rules have explicit policy and concurrency tests.
- Completed-session visibility is covered separately from real capacity and client drag behavior.
- All existing reservation writers were located: staff/customer transitions, expiry and demo payment. Their revision increments are part of Task 2 rather than a later follow-up.
- New audit events require validator expansion even though Checked-In/Completed status strings already exist.
- Proposed defaults are labeled above; no automatic no-show, attendance correction, payment change or schedule extension is implied.


## Execution evidence

- Release solution build with `--artifacts-path .local/attendance-artifacts`: passed, 0 warnings/errors. Separate outputs preserve the configured running app.
- Full conventional Release unit suite: 99 passed (includes 10 attendance policy tests). Migrator was built in its normal output layout for CLI tests.
- Full Web non-integration suite: 52 passed.
- Full local data integration suite: 89 passed (includes 14 attendance cases: lifecycle, multi-bay/whole-shop release, retries, cancellation/allocation races, revision writers, legacy migration/rerun/preflight, and follow-up without automatic state change).
- Full Web integration suite: 36 passed. Attendance HTTP checks cover Staff/Admin permissions and anti-forgery, confirmation, invalid actions, forged metadata, exact actual timestamps, early reason validation, repeated commands and Completed schedule visibility.
- JavaScript suite: 59 passed, including completed-bar drag versus click. CSS build passed. UI detector reported no findings before the bounded browser inspection; final diff whitespace checks passed.
- Desktop/360px visual checks used disposable fixtures rendered by the HTTP test with current local assets. Verified attendance layout, early departure warning, checkbox/button keyboard focus, actual timestamps and history. Completed schedule shows 0/7 reserved capacity; dragging from a completed bar opens new booking and clicking opens its details. Actual writes were exercised through the HTTP integration test, not the static fixture.
- Review: no confirmed security/concurrency defects. Checked a suggested Razor variable-scope concern by successful builds and renamed the inner pattern variable for clarity. Added malformed legacy metadata preflight tests and fixed completed-bar drag start as follow-ups. Final focused JavaScript review found no concrete issues.
- The existing pending-expiry script still targets the review decision panel; attendance uses its own class. No automatic no-show/checkout, schedule extension, payment override or new attendance email was added.
- Temporary fixture exporters were removed; preview tabs and fixture server were closed. No shared migration, configured app restart, commit or push was performed.
- Rollout: stop old Web writers, explicitly apply migration 013 through the leased Migrator, then start updated Web and require healthy readiness. Avoid mixed old/new reservation writers because revisions now protect staff commands.
