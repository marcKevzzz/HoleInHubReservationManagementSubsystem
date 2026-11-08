# Sprint 2 implementation plan

**Target milestone:** Week 15, Thursday, 19 November 2026.
**Plan prepared:** 6 November 2026. The work windows below are proposed, in Asia/Manila.
**Source:** [IPT102 timeline](../PROJECT-TIMELINE.md), [current requirements](../PROJECT.md), [foundation spec](../specs/2026-10-18-production-foundation.md), and [implementation inventory](../CURRENT-SYSTEM.md).

## Baseline and release boundary

The existing Staff/Admin booking review queue already supports approve, reject, coach confirmation and cancellation decisions. The customer side already supplies verified guest/account access, cancellation requests and booking history. Allocation already uses MongoDB transactions, persisted resource/date guards and submission idempotency. SMTP runs through a durable outbox. Whole-shop deposit success is demonstrated through Development/Demo checkout. Reuse those flows.

The remaining Sprint 2 scope is the broader staff workspace, calendar, assisted bookings, customer lookup, change requests and safe schedule edits, attendance, resource/role controls, stale-edit protection across staff writes, and end-to-end acceptance. Sprint 3 owns utilization/reports, scheduled pre-start reminders, SMS, chatbot, integrated POS simulation and deployment/restore evaluation. Keep Sprint 2 deposit verification behind the existing demo configuration until the POS evidence and authorization contract is agreed.

## Work packages

| Proposed window | Delivery slice | Exit evidence |
| --- | --- | --- |
| 6–9 Nov | Confirm policy decisions. Establish concurrency contracts and write baseline tests. | Role/action matrix and lifecycle rules approved or blocked actions explicitly scoped out. Existing approval/cancellation and allocation cases recorded. |
| 9–11 Nov | Staff workspace, calendar and protected lookup. | Staff can find a reservation, see actionable requests, and inspect a Manila-day schedule. Customer and anonymous access is denied. |
| 11–13 Nov | Assisted booking, resources and role controls. | Staff can record an authorized booking without a customer login. Admin-only controls and conflict checks verified. |
| 13–16 Nov | Change requests and atomic schedule updates. | Approved moves succeed atomically. Conflicts, stale edits and declined requests leave the original booking intact. |
| 16–18 Nov | Attendance, deposit verification, audit and notices. | Staff lifecycle demonstrated. Failed or repeated demo results cannot incorrectly verify deposits. |
| 18–19 Nov | Regression, staff walkthrough, fixes and evidence. | Release candidate, reproducible verification commands, acceptance results, and documented deferrals. |

### 1. Establish write and authorization contracts

- [ ] Define action permissions in the Business policy and enforce Staff/Admin access in Web. Existing roles are the starting point. Give Staff operational access and reserve resource and role mutations for Admin unless a different matrix is approved. Keep supervisor exceptions disabled until explicitly defined.
- [ ] Add a reservation revision and require the expected revision on staff commands that change state, time or resources. Integrate the compare-and-update with existing writers in `ReservationPersistence.cs` and `BookingPersistence.cs`, including cancellation, expiry and demo deposit. Existing records must remain readable and have a defined initial revision. Return a stale-edit result and prompt the staff member to reload rather than replaying the action.
- [ ] Update MongoDB validation/indexes via the Migrator only if the new fields, statuses or query plans require them. Review existing legacy status values, migration checksums and health requirements. Verify upgrade of populated data and a fresh setup. Keep all database I/O asynchronous with cancellation.

### 2. Staff dashboard, calendar and customer lookup

- [ ] Expand `BookingReviewController.cs` and its views for queue counts, pending/deposit indicators, an actionable change queue and links to staff details. Reuse the existing details and decisions.
- [ ] Add a bounded day query in Business/Data using overlap with the selected Asia/Manila day converted to UTC. Show active bays, closures/maintenance and reservation states. Show a multi-bay booking on each allocated bay with one shared detail reference. Keep customer identity/contact in protected details and staff lookup. Bound and paginate lookup, search by exact reference or approved normalized contact fields, and limit displayed PII to the task.
- [ ] Use server-rendered navigation and list fallback for small screens, with keyboard-operable controls. Smoke-test authorization, empty days, midnight boundaries, retired resources and linked multi-bay entries.

### 3. Assisted booking, resources and roles

- [ ] Add a staff-only create flow using the existing reservation service and allocation path. Capture booking source (walk-in, phone or messaging), customer contact, service, attendees, schedule, resources and staff actor. Give assisted submissions their own stable idempotency namespace. Keep staff identity separate from customer ownership and keep existing guest access and account ownership checks intact. Use approved handling for customers without email.
- [ ] Reuse the resource catalog and schedule block data for resource listing and maintenance/closure controls. Keep existing IDs and history. Resource deactivation or a new block must contend on the same guards as allocation, and preserve already-booked capacity according to the approved policy. Exercise concurrent block-versus-booking and whole-shop catalog changes.
- [ ] Reuse ASP.NET Core Identity and the existing role store for Admin-only approved role changes. Audit actor, target, prior and new role. Verify role changes take effect for existing sessions and protect against losing the last administrator.

### 4. Change requests and safe scheduling

- [ ] Add an owned/secure guest entry point to propose a reschedule or extension, and preserve the current cancellation request flow. Store the proposal, base revision, reason and disposition in a bounded pending request representation with durable history. Submitting a proposal does not change or hold the proposed allocation. Add the protected staff review and reassignment actions to the existing queue/detail views.
- [ ] Add narrow Business commands for approve/decline and staff reassignment. Validate service, capacity, participants, operating hours, schedule blocks, coaching and whole-shop policy before accepting. Disable whole-shop schedule changes that would affect deposit terms until that policy is approved.
- [ ] In Data, contend on the catalog guard and the sorted union of old and proposed resource/date guards. Include every affected Manila date supported by the operation. Re-read the current reservation in the transaction, enforce the expected revision and eligible state, and exclude only that reservation from overlap checks. Atomically update the schedule/resources, request disposition, audit history and notification outbox. Release old capacity only as part of a successful commit. On conflict, stale revision, closure or transaction failure, keep the original schedule and allocations intact.

### 5. Attendance, deposit verification and notices

- [ ] Add explicit attendance actions to staff details. Proposed baseline for owner approval: Confirmed → Checked-In → Completed, and Confirmed → No-show only after the grace deadline and staff follow-up. Apply the earlier of start plus one hour and scheduled end. Flag eligibility without automatically declaring a no-show. Late arrivals retain their scheduled end unless an extension is approved. Keep cancellation separate from attendance, and map existing legacy Checked-In records consistently in capacity queries.
- [ ] On staff details, show existing whole-shop deposit state, deadline and reference. Reuse the Development/Demo verification path to prove success, failure, timeout and duplicate outcomes. Do not add a production manual paid override without a signed-off evidence policy. Deposit state and attendance are separate.
- [ ] Commit append-only history with actor, time, event, reason and relevant before/after values. Use the existing outbox for approval and accepted/declined customer changes where applicable, with stable deduplication and stale-message checks. Deliver after commit. Verify retry and notification failure cannot roll back a reservation or double-send a committed event.

## Owner decisions that affect implementation

| Decision | Needed before | Default while unresolved |
| --- | --- | --- |
| Staff-assisted initial status, required contact data and notification/guest recovery rules | Assisted booking writes | Keep existing public booking and staff queue rules. Do not infer staff-assisted expiry or ownership. |
| Customer change lead time, eligible states and services, and deposit/refund treatment | Approve schedule changes | Preserve existing cancellation rules. Keep unapproved change types disabled. |
| Check-in window, grace exceptions, completion and early capacity release | Attendance writes | Use the documented grace flag. Keep uncertain overrides and capacity-release behavior disabled. |
| Admin/Supervisor matrix, resource conflicts and staff provisioning | Resource and role mutations | Deliver read-only views first. Require Admin for authorized mutations. |
| Accepted evidence for real deposit verification | Production payment verification | Demonstrate with the existing explicitly enabled simulation. |

## Acceptance and release evidence

- [ ] Unit: extend `BookingRulesTests.cs` and `ReservationPolicyTests.cs` or add focused staff policy tests for the transition matrix, lead times, grace boundary, schedule validation and legacy status mapping.
- [ ] Integration: extend `BookingProcessTests.cs`, `AllocationTests.cs` and `OutboxRecoveryTests.cs` for concurrent moves to the same slot, simultaneous stale decisions, failed multi-bay rescheduling, block/deactivation races, whole-shop exclusivity, late deposit versus expiry, no-show eligibility/release, audit and outbox deduplication. Assert the original interval, resource IDs, revision and competing availability after each failed move.
- [ ] Web: extend staff and guest smoke coverage for anonymous/customer denial, Staff/Admin boundaries, antiforgery, forged ownership, stale-edit messages, filtered calendar and existing booking/checkout routes. Run a keyboard and mobile pass on calendar, queue, detail and change review.
- [ ] Staff walkthrough on isolated demo data: create assisted booking → locate it on calendar → review a conflicting change and verify original allocation → approve a valid change → record arrival/completion. Separately demonstrate cancellation, manual no-show and successful/failed/duplicate demo deposit.
- [ ] Record date, commit, scenario, expected/actual result, and evidence for each acceptance item. Document owner decisions, defects and deferrals before marking the milestone complete. Apply any migration explicitly through the leased Migrator and check readiness before the walkthrough.

Run from repository root:

~~~powershell
dotnet build HoleInHubReservationManagementSubsystem.slnx
dotnet test HoleInHubReservationManagementSubsystem.slnx --filter "Category!=Integration"
docker compose -f docker-compose.mongo.yml up -d
dotnet test HoleInHubReservationManagementSubsystem.slnx --filter "Category=Integration"
npm --prefix HoleInHub.Web run build:css
npm --prefix HoleInHub.Web run test:ui
~~~

Use an isolated local MongoDB replica set for integration. Run browser acceptance against the configured preview only after `/health/ready` reports healthy. An unchecked item is planned work. The PDF and this plan do not establish implementation or release readiness.

## Owner-approved first change-request slice — 7 November 2026

Confirmed Regular/VIP customers may propose a new schedule or longer duration at least one hour before their current start. Proposed starts must allow the normal 30/60-minute operating-hours review window. Approval requires an unchanged base revision, no pending cancellation, active matching-service bays and availability, before either current or proposed start. The number of bays and guest count stay unchanged. Staff can select replacement bays during review. Declining or failing a change preserves the original booking. Whole-shop/coaching changes, in-session extensions, direct staff moves, manual no-show actions and attendance corrections remain deferred.
