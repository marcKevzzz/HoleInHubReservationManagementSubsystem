# Admin Block Time Implementation Plan

> **For agentic workers:** Use superpowers:executing-plans to implement this plan task by task.

**Goal:** Let Admins reserve an otherwise free single bay for maintenance or another stated reason from Staff → Reservations.

**Architecture:** Extend the existing schedule_blocks collection and ReservationService. Block insertion contends on the same catalog/resource/date guards as reservation insertion and checks bookings and blocks inside the transaction. Reuse the booking drawer styling and submission behavior, with a shorter block form and a server-rendered fallback.

**Tech Stack:** .NET 10 MVC, MongoDB.Driver, Razor, existing Tailwind CSS and vanilla JavaScript.

**Spec:** Owner-approved scope in this chat; AGENTS.md; Documentation/PROJECT.md; Documentation/specs/2026-10-18-production-foundation.md.

## Global constraints
- Admin-only GET/POST; anti-forgery on POST; actor comes from authenticated claims.
- One active Regular/VIP bay, Manila date/time, existing opening hours and 30-minute increments; permit current half-hour as staff booking does.
- Reject overlaps with Pending, Confirmed, Checked-In, targeted blocks and all-bay closures. Adjacent [start,end) intervals remain allowed.
- Store UTC timestamps, required trimmed reason (maximum 500 characters), creator and creation time.
- Derive a deterministic block ID from actor + idempotency key; same payload returns the saved block, changed payload fails. No customer notification or reservation record.
- Preserve existing blocks and immutable migrations. Do not apply migrations to a shared database or replace a configured preview.
- Creation only; block editing/removal, recurring blocks, multi-bay and venue-wide creation remain outside this change.

## Review focus
- Concurrent block and booking saves must have one winner: integration race test.
- Retries after an uncertain response must not duplicate blocks: replay and changed-payload tests.
- Empty ResourceIds on a legacy closure must still block every bay: conflict test.
- Staff without Admin cannot reach block GET or POST: authorization metadata tests.
- Invalid dates/times/reasons and a retired bay must fail without saving: unit and integration tests.

## Task 1: Atomic block persistence and business validation
**Files:** Business/Reservations/StaffBlock.cs; Data/Reservations/ScheduleBlocks.cs; Data/Reservations/Records.cs; Data/Migrations/Definitions/011_AdminScheduleBlocks.cs; Data/Extensions/ServiceCollectionExtensions.cs (under HoleInHub.*); tests/HoleInHub.Tests.Unit/Reservations/StaffBlockTests.cs; tests/HoleInHub.Tests.Integration/Reservations/ScheduleBlockTests.cs.
**Interfaces:** StaffBlockRequest(ResourceId, Date, StartTime, DurationMinutes, Reason, IdempotencyKey); ReservationService.CreateScheduleBlockAsync(request, actor, ct) returns Task<string>; ReservationPersistence.InsertBlockAsync(ScheduleBlock, ct) returns Task<ScheduleBlock>.
- [x] Add failing validation tests and MongoDB tests for conflict states, boundary adjacency, UTC/audit storage, retries, inactive resources and concurrent booking/block creation.
- [x] Implement validation and idempotency; reuse GuardAsync and HasAllocationConflictAsync through an allocation envelope inside a bounded transaction.
- [x] Add optional typed audit fields through migration 011; register it, test reruns and legacy compatibility.
- [x] Run targeted unit/integration tests using isolated local databases.

## Task 2: Admin form and schedule integration
**Files:** HoleInHub.Web/Controllers/BookingReviewController.cs; ViewModels/StaffBlockForm.cs; Views/BookingReview/{CreateBlock,_StaffBlockForm,Index}.cshtml; wwwroot/js/staff-booking.js; Business/Reservations/StaffSchedule.cs; Styles/staff-workspace.css; tests/HoleInHub.Tests.Web/Smoke/StaffBlockAccessTests.cs.
**Interfaces:** GET/POST /staff/reservations/blocks/new; JSON success redirect and 422 validation response, matching the existing booking form.
- [x] Add authorization/anti-forgery regression tests and run them before implementation.
- [x] Add Admin-only Block time link and dialog, explicit bay/time selection, required reason, fallback page, validation recovery and duplicate-submit protection.
- [x] Reuse drawer submission setup without changing booking drag behavior. Show block reason in timeline tooltip and list; legacy blocks keep a generic label.
- [x] Run targeted Web/JavaScript tests and build CSS. HTTP tests exercise the rendered fallback and schedule; drawer tests exercise conflict recovery and focus. Visual browser QA was not run; the existing configured preview was left running.

## Task 3: Documentation and verification
- [x] Update DATABASE.md and published schedule-block schemas; keep schema/installed-validator test aligned with migration 011.
- [x] Run solution build, non-integration tests, Docker Compose and integration suite, UI tests, CSS build and git diff --check; report actual failures separately.
- [x] Review changed files for authorization, atomicity, retries, accessibility and preservation of existing work.

## Execution record
- Existing checkout contains the prior staff workflow and unrelated uncommitted UI changes. Reuse this checkout on codex/admin-schedule-blocks; do not commit or discard unrelated work.
- Owner explicitly requested a plan then execution; proceed inline without another approval gate.


## Verification evidence
- Release solution build: passed, 0 warnings, 0 errors. Debug Web verification hit files locked by the existing preview, so subsequent Web/build checks used Release.
- Unit suite: 85 passed, including 8 new block validation cases.
- Data integration suite: 70 passed on isolated local replica-set databases, including 12 new block cases, legacy schema compatibility and published-schema parity.
- Block Web tests: 6 passed (real HTTP role checks, anti-forgery, save/replay/conflict recovery, staff UI visibility, plus authorization metadata).
- Full Web non-integration suite: 40 passed, 10 failed in account/public-page/catalog markup expectations outside this feature.
- Full Web integration suite: 34 passed, 1 failed in AccountHubIdentityTests expecting the old PHP 40,000.00 transaction text.
- JavaScript suite: 58 passed, including existing drag tests and new block drawer duplicate-save/error/focus checks.
- CSS build: passed. Impeccable detector on changed UI: no findings. git diff --check: passed.
- Docker Desktop was initially stopped; it was started and Compose services are running. Tests explicitly selected mongodb://localhost:27017/?replicaSet=rs0 and disposable test databases.
- Migration 011 was exercised in test databases only. No shared database migration, preview restart, commit or push was performed.
- Final reviewer pass: no actionable findings in the supplied code. The reviewer checked guards, conflicts, concurrent retries, actor-scoped idempotency, authorization, anti-forgery, encoding and drawer recovery. Its fresh filesystem read was blocked awaiting approval, so this was a transcript-based review rather than an independent file inspection.
