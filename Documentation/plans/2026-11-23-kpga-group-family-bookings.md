# KPGA Group shared seats and family reservations

## Deliverable and acceptance example

One booking contact can reserve seats for multiple adults or children in one published KPGA Group class. The family receives one HIH reservation ID. Staff see one Schedule row and one roster row per learner. Start with the existing Tuesday 3–4 PM Manila session definition. Shared capacity applies only to KpgaGroup. Private KPGA, regular bays and whole-shop bookings retain their existing behavior.

Example: a class has three seats left. A parent chooses two, enters two children and one shared guardian contact, and submits. Pending review holds both seats. One seat remains. Staff approval leaves that count unchanged. A permitted cancellation for one child restores one seat and leaves the other child enrolled.

## Decisions and limits

- No separate per-family limit. The maximum selectable is the number of available learner seats, bounded by the approved class and physical capacity. Eight is the documented group ceiling. Confirm the assigned bay configuration and existing player limits allow the offered seats before publishing. Never infer that eight players fit a single bay merely because eight people can occupy it.
- Both adult and under-18 learners use one seat each. Confirm whether they may attend the same occurrence and whether an accompanying adult must stay. If spectators use physical capacity, collect the accompanying-person count and enforce occupancy alongside seats. Until the venue defines this, offer only configurations whose capacity staff can substantiate.
- A guardian relationship is declared by the booking contact. It does not verify legal guardianship. Collect learner name and adult/under-18 category, then guardian name and relationship for minors. Reuse the booking contact's existing contact details when applicable. Confirm any additional guardian contact or consent requirement with the venue. Leave account signup unchanged.
- Confirm pricing before opening sales: per learner or per family, whether adult and child rates differ, and how a one-seat cancellation affects a saved quote. Show the price basis and total before submission; preserve the booked terms. Coach confirmation and venue approval continue under the current coaching rules.
- Scope ends at single-class bookings. Recurring enrollment, next-class reminders, coach scheduling, POS changes and CMS work belong to separate plans.

## Implementation order

### 1. Publish classes and protect bay allocation

- Data: add a class occurrence document with internal MongoDB key, unique class_session_id, service offering, UTC start/end, assigned bays, approved learner capacity, held-seat count and lifecycle state. Use the next available Migrator number; 024 is already present in the working tree. Add validators and indexes for session ID and date-range discovery. Prevent duplicate publication for the same offering, slot and assigned resources.
- Staff: add a permission-checked publish/close action in Schedule. Publishing checks blocks, active reservations, other classes and whole-shop bookings under the same per-bay/date guards used by ReservationPersistence. Expose the class allocation to ordinary availability, staff moves, extensions, blocks and whole-shop conflict checks. Closing enrollment stops new sales; freeing the physical bay is a separate action requiring resolution of existing enrollments.
- Existing group reservations remain readable and keep their existing allocation. Do not backfill learner identities from guest counts. An overlapping legacy reservation prevents publication unless staff explicitly resolve it.
- Verify two competing publications and a class versus a whole-shop booking cannot acquire the same bay. The class holds each assigned bay once even when partially enrolled.

### 2. Create the family reservation and its roster atomically

- Extend the reservation document with optional class_session_id and named learner entries with stable IDs, age category, guardian association and individual enrollment state. Store a shared guardian once per family booking and reference it from siblings. Keep booking contact and learner identities distinct. Preserve existing per-bay participant counts and legacy document readers.
- Business: validate learner count, names, age categories, guardian requirements, class eligibility and pricing on the server. The number of active learner entries determines the seat request. Use the same submission path for signed-in, verified guest and staff-assisted group bookings.
- Data: claim all requested seats with a conditional write on the class document in the same MongoDB transaction as the reservation, learner entries, verification consumption and existing notifications. Use the existing submission key and include class and roster in its payload hash. Same key and payload returns the original HIH ID; a changed payload cannot reuse the key.
- A draft or verification email holds zero seats. Check availability again on final submission. On conflict, keep form data and offer eligible later classes with enough seats. Do not silently split a family or switch classes.
- Verify the key race on an isolated replica set: three seats left, two concurrent requests for two seats, exactly one successful reservation, one seat remaining. Also test double submission, failed transaction rollback and two independent family bookings sharing one class.

### 3. Connect the customer form and staff review

- Start in Reservations/Create.cshtml and reservation.js. Show class time, learner seats remaining and quantity before the roster. Generate one accessible, clearly numbered field group per learner. Support a server-rendered form path. Keep the roster through validation errors and guest email verification; avoid putting names in URLs, browser storage or logs.
- Use “Booked by”, “Learner” and “Parent or guardian”. Offer “I'm participating”, “I'm the guardian” and reuse of a previous guardian. An account pre-fills the contact; it does not imply participation. Adults do not need guardian fields. Handle mixed-age siblings and different guardians within one reservation.
- Staff review approves/rejects the family reservation using existing coach-confirmation rules. Schedule shows one reservation and learner count. Authorized staff can open the class roster showing learner, family reservation ID and enrollment/attendance. Limit guardian details to authorized booking details. Availability pages return aggregate seats only.

### 4. Release seats exactly once and reconcile counts

- Rejection, expiry and full cancellation release all remaining active seats transactionally. Individual cancellation identifies a learner by stable ID, checks booking ownership or staff authorization and applies the existing cancellation window. Mark the learner cancelled and decrement once. Final learner cancellation also closes the family reservation. Use revision checks and idempotency for competing cancellation, expiry and approval.
- Record attendance per learner so one sibling can attend and another be absent. An absence after class does not retroactively free a sellable seat. Keep group attendance independent of releasing a bay used by other families. Define staff correction and class closure behavior before enabling shared attendance.
- Reports keep reservations as bookings. Label learner seats separately wherever totals are shown. Verify a three-family, six-learner fixture reconciles across Schedule, roster and report/export without counting guardians as learners or multiplying reserved bay-hours by the number of families.

### 5. Verify and roll out

- Unit cases: one adult, two minors sharing a guardian, different guardians, missing or invalid values, maximum seats, insufficient capacity and cutoff boundaries in Manila time.
- Integration cases: all-or-nothing claim, duplicate request, partial cancellation retries, expiry versus approval, class closure during submission, guarded competing bay writes, and legacy group bookings. Verify another customer cannot read or alter a child's roster by guessing either ID.
- Browser acceptance on desktop/mobile: parent finds a class with three seats, books two children using one contact, receives one ID, staff see two roster entries, cancellation of one child restores exactly one seat. Exercise keyboard use, validation, guest and account flows, and the class-fills-during-verification case.
- Ship behind a disabled shared-class setting. Apply the CLI migration to an isolated replica set first; verify migration reruns, build, CSS, JavaScript, unit, smoke, integration and browser checks. Pilot one future class after venue approval. If sales must pause, disable new shared enrollments while retaining staff access, roster history and existing bay holds.

First engineering milestone: complete the class allocation and concurrent seat-claim tests before building the family form. First venue milestone: agree on the sellable seats, spectator policy and pricing for one pilot class.

### Next-class reminders

1. Offer a one-time email on the family receipt. The booking contact can turn it off until a class is published.
2. When staff publish a later occurrence of the same offering, count enrolled learners and queue one message per opted-in reservation in the publication transaction.
3. Link to the selected class in the existing booking flow. Show its Manila start time, per-learner price and seat count at publication. The customer reviews current availability before submitting.
4. Verify that a two-child family hears about a one-seat class, publication retries produce one message, and the receipt controls work on desktop and mobile.
