# Attendance and session extensions

Owner requested implementation in this order on 15 November 2026. Extensions cover Regular and VIP only.

## 1. No-show

- Workspace staff may mark an unattended Confirmed reservation No-show at or after the earlier of start + 1 hour and scheduled end.
- Require a follow-up confirmation and a reason. Resolve pending cancellation or schedule-change requests first. Reject recorded arrivals or departures.
- Save one replay-safe audit event, advance the revision, release remaining allocations and enqueue the customer update.

## 2. Attendance corrections

- Workspace staff can correct attendance on Confirmed, Checked-In, Completed and No-show records.
- Require a reason and explicit confirmation. Actual times are entered in Manila time and stored in UTC.
- Confirmed and No-show have no actual attendance times. Checked-In needs an arrival. Completed needs arrival and departure. Reject future times, departure before arrival, and arrival before the early check-in window or after the scheduled end.
- A correction that restores a Confirmed or Checked-In booking must atomically recheck its original bays and interval. Never overwrite another allocation.
- Preserve the previous and replacement status and actual times in history. Preserve scheduled times and ownership.

## 3. Extensions

- Staff can extend an ongoing Confirmed or Checked-In Regular/VIP reservation in 30-minute increments.
- Require customer agreement, a reason and the current booking revision. Resolve pending cancellation or schedule-change requests first.
- Preserve start, service, bay selection and guest count. The new end must fit Manila operating hours.
- Take the existing catalog/resource-date guards and atomically recheck the full extended interval against bookings, holds and blocks. Reject the entire operation if any bay conflicts.
- Save before/after scheduled end times and enqueue an updated session notification. Additional charges remain venue/POS-managed.
- Customer pages identify the saved quote as the original booking amount after an extension.

## Verification and rollout

- Add migration 020 for optional audit snapshots and the new history event types. Keep older records readable. Run it only through the explicit Migrator CLI.
- Unit tests cover time boundaries, state prerequisites, corrections and extension validation.
- MongoDB integration tests cover concurrency, stale revisions, retries, restoring released capacity, multi-bay conflicts, blocks, schema validation and failed-operation rollback.
- Smoke and JavaScript tests cover protected actions, form confirmation and rendering.
- Run solution build, non-integration tests, UI tests and Tailwind build. Integration tests require an isolated local replica set. Verify the configured preview readiness before and after restart.

## Implementation evidence · 15 November 2026

- Migration 020 was applied through the Migrator CLI to the configured preview database after a local backup. The backup was restored and compared in an isolated local database; migration replay was also checked there. Reservations, accounts, customer profiles, resources, services and settings matched their pre-migration contents.
- The final solution build passed with zero warnings or errors. All 393 non-integration tests and 152 UI tests passed. Tailwind and vendor asset builds passed.
- The full database integration run passed 164 tests. The final focused attendance rerun passed 21 tests, including the legacy attendance-command hash after the upgrade.
- The first full HTTP integration run passed 65 of 77 tests. The failures exposed stale authentication/validation test assumptions; those fixtures and assertions were updated. The final focused HTTP rerun passed all 20 cases, covering every failed case and the new attendance workflow. The entire 77-case HTTP suite was not repeated after these test updates.
- The restarted preview at `http://localhost:5275` reported Healthy with all required migrations applied. Visual review remains unverified because the browser webview could not attach.
- Extensions do not reprice the original quote or collect additional payments. Staff confirm customer agreement; additional time charges remain venue/POS-managed. Production deployment has not been performed.
