# Admin Unblock Time Implementation Plan

> **For agentic workers:** Use superpowers:executing-plans to execute these tasks inline.

**Goal:** Admins can open a schedule block, confirm its release with a reason, and free capacity without deleting audit history.
**Architecture:** A one-way guarded transition writes ReleasedUtc/ReleasedBy/ReleaseReason on schedule_blocks. Every active-block query excludes released records. Razor provides an Admin-only details and confirmation page with no new JavaScript or dependencies.
**Tech Stack:** .NET 10 MVC, MongoDB transactions, existing staff CSS/Razor.
**Spec:** Owner-approved Unblock time scope in this chat; AGENTS.md; Documentation/PROJECT.md.

## Constraints and review focus
- Admin-only details and release routes; anti-forgery and explicit confirmation on POST; authenticated actor only.
- Required trimmed release reason, maximum 500 characters; UTC storage, Manila display.
- Preserve original interval, creation audit, idempotency hash and reason; never delete a block.
- Transaction contends on the same allocation guards as bookings. First release wins; duplicate or concurrent release attempts do not overwrite its audit.
- Ignore released blocks in public availability, transactional conflict checks, and staff schedule; leave other blocks and bookings intact.
- Legacy targeted and venue-wide closures can be released. Missing creation audit is displayed as unrecorded.
- Creation replay must not reactivate or report a released block as newly blocked.
- No recurrence, editing, partial-interval release, customer notifications, or shared-database rollout.

## Task 1: State and capacity release
- [x] Add failing validation, release/retry/concurrency, legacy closure and capacity-query tests.
- [x] Add optional release audit fields and migration 012, preserving immutable migrations and old block documents.
- [x] Implement GetScheduleBlockAsync and ReleaseScheduleBlockAsync in Business; guarded conditional release in Data; update all three active-block query paths and creation replay behavior.
- [x] Verify unit/data integration tests and migration/schema parity.

## Task 2: Admin block page
- [x] Add HTTP permission, anti-forgery, missing confirmation/reason, release and retry tests.
- [x] Add Admin-only links on blocked timeline/list entries using BlockId separate from ReservationId.
- [x] Render block details and audit, required confirmation and reason; validation errors keep the form; successful POST redirects to details with first-release audit visible.
- [x] Verify rendered HTTP flow and existing timeline/booking JavaScript tests.

## Task 3: Verification and handoff
- [x] Update DATABASE.md and published schemas; record fresh build, unit/smoke/integration, CSS and UI test results.
- [x] Review changed code, including concurrency and unknown/legacy blocks; keep unrelated work intact.

## Execution notes
- Reuse existing codex/admin-schedule-blocks checkout and its prior uncommitted implementation. No commit, push, configured-preview restart, or migration against shared databases.
- The owner explicitly requested execution; proceed without another planning approval.


## Verification evidence
- Full solution Release build used `--artifacts-path .local/unblock-artifacts`: passed with 0 warnings/errors. Normal Release Web outputs are locked by the existing app, which was left running.
- Full unit suite in the conventional Release layout: 89 passed. Five CLI tests initially failed in the isolated artifacts layout because their helper computes a conventional Migrator path; rebuilding Migrator and rerunning the normal unit suite resolved those harness failures without code changes.
- Data integration suite: 75 passed on isolated local replica-set databases, including release races, duplicate releases, remaining conflicting blocks, legacy all-bay closures, availability/schedule queries, migration reruns and published-schema parity.
- Block Web suite: 7 passed, including real HTTP role/anti-forgery checks, confirmation/reason validation, URL links, successful release, replay audit preservation and schedule removal.
- Full Web non-integration suite: 41 passed, 10 failed in preexisting account/public-page/catalog markup assertions.
- Full Web integration suite: 34 passed, 1 failed in preexisting AccountHub transaction-price text assertion.
- JavaScript suite: 58 passed. CSS build passed. UI detector reported no findings. git diff --check passed.
- Desktop and 360px browser checks used an HTML fixture rendered by the real HTTP test, with current CSS served from the existing app. Confirmed readable form, checkbox alignment and keyboard focus; no release was submitted through the fixture. A checkbox CSS specificity issue found during inspection was fixed. Temporary fixture-export code was removed and the fixture server/tab closed.
- Fresh independent filesystem review: no actionable findings in authorization, confirmation, guards, retries, audit preservation, capacity queries, legacy closures, migration or schedule links.
- No shared database migration, configured app restart, commit or push. Apply migration 012 explicitly through Migrator before deploying the updated Web application.
