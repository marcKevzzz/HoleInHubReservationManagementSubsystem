# Move separate reservations together

Shift-click toggles a reservation in the selection by booking ID. Clicking any bar of a multi-bay booking selects all its bars as one reservation. Provide a visible selection mode for touch and keyboard users, a selected count, and a Clear selection action. Ordinary clicks continue to open booking details outside selection mode.

For the first release, drag any selected bar horizontally to apply the same time offset to all selected reservations on the current day. Preserve each booking's duration and bay assignments. Preview every resulting interval and show which booking or bay prevents the move. Confirm the full list of changes before submitting. Keep the selection on a failed move so staff can adjust and retry.

Send one command containing the selected booking IDs, expected revisions, and common offset. On the server, recheck permissions, move eligibility, operating hours, active bays, schedule blocks, and collisions against both unselected reservations and the proposed group. Acquire the existing resource/date guards in stable order and update all reservations in one MongoDB transaction. Use a single idempotency key for the batch, record each booking's move in history, and refresh the timeline after success.

Verify selection from either bay of one booking, selection of separate bookings with the same guest name, keyboard and touch selection, overlap with an unselected booking, a stale revision, and a retry after an uncertain response. A failed batch should leave every reservation at its original time. Add cross-day and bay reassignment after the same-day interaction and transaction are verified.
