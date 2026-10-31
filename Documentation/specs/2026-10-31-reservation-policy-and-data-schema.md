# Reservation policies and MongoDB data design

Status: proposed design for owner review, 2026-10-31. This document does not change application behavior or override Chapter I. Rules marked **baseline** come from [Chapter I](../CHAPTER_1.md) and [PROJECT.md](../PROJECT.md); rules marked **proposal** are design recommendations, not approved venue policy. No deposit percentage, refund entitlement, or coaching capacity is inferred from the manuscript.

## 1. Terms and boundaries

- Reservation: one customer arrangement, possibly involving several resources, with one lifecycle.
- Resource: a bay, private space, or individual coach whose availability must be protected.
- Allocation: a resource and protected half-open UTC interval `[start, end)` belonging to a reservation.
- Hold: allocations protected while a reservation is Pending; browsing creates none.
- Review deadline: deadline for staff to decide an ordinary online request.
- Deposit deadline: separately agreed deadline for whole-shop deposit verification.
- Deposit verification: a recorded decision based on authenticated POS evidence; a screenshot or customer claim alone is insufficient.
- Group coaching session: one scheduled lesson sharing a coach and bay among several customer reservations.
- Customer: a contact record, distinct from a login account and from an attendee.

Reservation owns scheduling, lifecycle, and verification decisions. POS owns quotes, billing, payment collection, refunds, and receipts. Store external references and verification facts here, not a second financial ledger.

## 2. Reservation rules

| Area | Rule | Basis |
| --- | --- | --- |
| Venue hours | Monday 11:00–24:00; Tuesday–Sunday 10:00–24:00, Asia/Manila. End at midnight belongs to the next date. | Baseline |
| Online access | Verified-email account required; public availability remains anonymous. | Baseline |
| Assisted access | Staff may book for a customer without a login account; record channel and staff actor. | Baseline |
| Availability | Validate the full interval against schedules, closures, maintenance and protected allocations. Multi-resource operations succeed together or fail together. | Baseline |
| Ordinary bay duration | 30-minute start grid and duration increments; 30 minutes–10 hours, within venue hours. Applies to bays only until service-specific terms are approved. | Proposal based on walkthrough |
| Booking horizon | Initially permit online requests up to 90 local calendar days ahead and at least 30 minutes before start; staff handle shorter notice without bypassing conflicts. | Proposal |
| Review coverage | Configure staff review days/hours independently of venue opening hours. Accept ordinary online requests only if a full 30-minute window remains and the deadline is no later than booking start. Outside coverage, offer contact instructions without creating a hold. | Proposal |
| Pending clock | Ordinary online bay requests expire 30 elapsed minutes after submission; reminders at 15 and 25 minutes. At equality with deadline, expiry wins; confirmation requires `now < deadline`. | Baseline, boundary proposed |
| Staff-assisted Pending | Require a staff-entered, customer-communicated deadline no later than start; never inherit ordinary online expiry silently. Staff may directly confirm if all prerequisites hold. | Proposal |
| Customer changes | A request does not alter or release the current booking. Staff approval atomically replaces allocations, terms and notifications after conflict checks. Reject stale requests after the reservation changes. | Baseline, version behavior proposed |
| Cancellation | Customer submits a request; staff cancellation records actor, reason and financial follow-up. No fee or refund is calculated here. | Proposal consistent with reviewed changes |
| Attendance | Staff record arrival. Late arrival retains scheduled end. Flag unattended confirmed bookings at `min(start + 60 minutes, end)`; staff follow up before marking NoShow. | Baseline |
| Completion | Staff complete a CheckedIn reservation at or after its scheduled end. Early departure requires an explicit staff end-time amendment before completion. | Proposal |
| Overrides | Supervisor may approve documented operational exceptions, but cannot bypass resource conflicts, ownership, deposit verification, or missing capacity limits. | Proposal |

All services require positive attendee counts and approved capacity limits; do not guess capacity from the number of bays. A customer cannot reopen a terminal booking. Staff create a new linked reservation after fresh availability checks.

### Lifecycle

Proposed canonical storage identifiers: `Pending`, `Confirmed`, `CheckedIn`, `Completed`, `Cancelled`, `Expired`, `NoShow`. Map current `Checked-In` explicitly during migration.

| From | To | Preconditions / actor |
| --- | --- | --- |
| New | Pending | Successful atomic submission with finite applicable hold deadline |
| New | Confirmed | Staff-assisted only; same checks as Pending → Confirmed |
| Pending | Confirmed | Staff; unexpired deadline, valid allocations and required deposit/coach prerequisites |
| Pending | Expired | System; applicable deadline reached; release capacity atomically |
| Pending | Cancelled | Staff rejection or accepted withdrawal; reason required |
| Confirmed | CheckedIn | Staff records arrival; proposed early check-in limit 15 minutes; never after scheduled end |
| Confirmed | NoShow | Staff after grace deadline and recorded follow-up |
| Confirmed | Cancelled | Staff; financial follow-up tracked separately |
| CheckedIn | Completed | Staff and completion rule satisfied |
| CheckedIn | Cancelled | Supervisor terminates interrupted service with reason and financial follow-up |

Pending, Confirmed and CheckedIn protect allocations. Terminal transitions release remaining capacity without deleting historical allocations. An elapsed reservation interval has no future capacity even when attendance review remains outstanding.

## 3. Coaching policy

**Baseline:** coaching exists, but Chapter I does not establish named coaches, days, prices, durations, capacities, or cancellation terms. Current code's KPGA Tuesday/Thursday/Saturday and Tuesday 15:00 group lesson are existing application assumptions, not manuscript approval.

**Proposal:**

1. Private coaching reserves an eligible bay and a named, active coach in the same transaction. Coach availability must cover the lesson, including configured preparation/turnover buffers. Neither a free bay nor a selected coaching category establishes coach availability.
2. Start with staff-assisted coaching. Publish online coaching only after coach roster, service eligibility, schedule, duration, maximum learners and terms are approved. No invented default capacity; an unconfigured service is unavailable.
3. Use configurable service durations; propose 60 minutes for a private lesson for review. Coach approval is recorded before confirmation; a substitute requires customer acceptance and fresh checks.
4. Coach absence triggers staff contact and an offered substitute, reschedule, or cancellation. Keep the original booking until an agreed replacement commits; refunds remain POS decisions under the accepted terms.
5. A group lesson has one `coaching_sessions` record holding the coach and bay once. Each customer's reservation consumes `seatCount` from that session. Do not allocate the same coach/bay separately for each participant.
6. Session capacity changes and seat reservations contend on the same session document in a transaction. Pending and Confirmed participants consume seats; cancellation/expiry releases seats exactly once. CheckedIn participants retain seats until the session ends. Session capacity cannot drop below protected seats.
7. Release a group session's physical allocations only on explicit session cancellation or completion. A session with zero customers may stay published and hold its scheduled resources. Cancelling it atomically cancels active participant reservations and queues notices; do not silently detach customers.
8. Whole-shop rentals conflict with group session allocations as well as private lessons. Customer-owned whole-shop time does not automatically book coaches; explicitly add coaches if agreed.

## 4. Whole-shop and deposit policy

**Baseline:** exclusive use of all facility resources, with negotiated deposit terms and recorded verification before staff confirmation. Use a separately agreed deadline; ordinary 30-minute expiry does not apply.

**Proposal:** whole-shop requests begin as staff-reviewed inquiries without capacity. Once the event scope, quote and written terms are accepted, staff create a Pending reservation protecting all facility resources. Coaches remain separately scheduled people rather than implicitly included facility resources.

- Record event type, organizer, attendee count, event start/end and exclusive setup/cleanup intervals. All protected time fits venue hours unless an approved dated opening-hours exception exists.
- Current half-day 15:00–21:00 and whole-day opening–midnight packages remain proposed catalog offerings. Do not treat the generic 10-hour bay limit as a whole-shop limit.
- Obtain a POS quote reference/revision, PHP currency, exact required deposit in integer centavos, verification deadline, balance due date, cancellation/rescheduling/refund terms and customer acceptance timestamp before starting the hold. Deposit amount is negotiated; there is no fabricated fixed percentage.
- Proposed default offer: 24 elapsed hours to verify, capped at event start. Staff negotiate a shorter deadline for near-term events and communicate it before submission. No past or unlimited deadlines. Never silently extend a deadline.
- Customer proof can start follow-up, but only an authenticated POS response tied to the quote/reservation establishes eligible verified value. POS is authoritative for amount and transaction status. Never count the same payment twice.
- Verification must show matching reference, currency and sufficient eligible deposit for the accepted quote. Partial, rejected, missing or unknown evidence does not permit confirmation. Record the verifier, verification time and source revision.
- Expire an unresolved Pending whole-shop hold at its agreed deadline, release all allocations and notify both parties. Late payment does not resurrect an expired booking; staff check availability and arrange a replacement booking or POS follow-up.
- A POS outage does not verify payment. A supervisor may extend a still-active hold with recorded reason and customer notice before expiry, subject to allocation checks. After expiry, reacquire capacity as a new booking.
- A verified deposit does not automatically confirm a reservation. Staff must approve. A later reversal flags the confirmed booking for urgent staff review; it does not silently cancel the customer's booking.
- A reschedule or scope increase requires a revised quote/terms and fresh deposit eligibility check before replacing the original booking. Refund status remains separate from reservation status; cancellation does not mean refunded.

Suggested deposit projection identifiers: `NotRequired`, `AwaitingVerification`, `PartiallyVerified`, `Verified`, `Rejected`, `Reversed`. Refund follow-up: `None`, `Requested`, `PendingPos`, `Resolved`. Financial amounts are snapshots of POS facts, never independently calculated balances.

## 5. Logical MongoDB schema and data dictionary

This is a target schema, not an applied migration. The companion [reservation core JSON schema](../schemas/reservation.schema.json) defines the proposed reservation BSON structure. Below, `id` means a stable string; UTC dates are BSON dates; `version` is an integer incremented by conditional writes. Optional values are omitted rather than invented empty strings. Existing PascalCase BSON fields require explicit mapping/backfill before adopting these camelCase names.

| Collection | Required data and relationships | Optional / conditional data |
| --- | --- | --- |
| `accounts` | Keep existing account identity, authentication and role model; no duplicated password storage | Customer link established through verified ownership or staff-authorized process |
| `customers` | `_id`, `displayName`, `contactMethods[] {kind,value}`, `createdAtUtc`, `updatedAtUtc`, `version` | `accountId`, preferred contact, staff notes with access restrictions. At least one reachable contact required for booking; online requires verified account email |
| `resources` | `_id`, `name`, `kind` (RegularBay/VipBay/PrivateSpace/Coach), `active`, `facilityExclusive`, `version` | Approved `capacity`, coach identity link. Eight seeded bays; no invented private rooms |
| `services` | `_id`, `name`, `kind` (Bay/PrivateCoaching/GroupCoaching/WholeShop), `active`, `policyVersionId`, `eligibleResourceKinds[]`, `allowedDurationsMinutes[]`, `startStepMinutes` | Approved attendee limit, eligible coach IDs, package intervals, setup/cleanup buffers; external POS product reference |
| `schedule_rules` | `_id`, `scopeType` (Venue/Resource/ReviewCoverage), `scopeId`, `timezone`, `effectiveFromLocalDate`, weekly windows, `version` | `effectiveToLocalDate`; dated exceptions take precedence. Windows use local minutes 0–1440; review coverage remains separate |
| `schedule_blocks` | `_id`, explicit `resourceIds[]`, `startUtc`, `endUtc`, `reason`, creator/time, `version` | Maintenance/closure category. Venue-wide block resolved using catalog guard; resource changes update its scope safely |
| `policy_versions` | `_id`, `effectiveFromUtc`, `status` (Draft/Approved/Retired), policy values, approver/time, terms text | Retirement timestamp; approved versions immutable; booking retains accepted version |
| `reservations` | See companion schema: identity/reference, customer, source, service, lifecycle, event interval, attendee count, policy snapshot, allocations, idempotency, version, created/updated timestamps | Account owner, coaching participation, event details, accepted terms, deposit projection, attendance, POS references, deadlines |
| `coaching_sessions` | `_id`, `serviceId`, `coachResourceId`, `bayResourceId`, `startUtc`, `endUtc`, `allocations[]`, `capacity`, `reservedSeats`, `status` (Scheduled/Completed/Cancelled), `version` | Coach acceptance, cancellation reason. Transactionally maintain seat counter with reservation writes |
| `reservation_events` | `_id`, `reservationId`, `sequence`, `type`, `actorType`, `actorId` where applicable, `atUtc`, `reason`, bounded before/after changes, command ID | POS evidence reference; append-only business history, no credentials or unbounded raw payloads |
| `change_requests` | `_id`, `reservationId`, `baseVersion`, requester/time, requested changes, `status` (Pending/Approved/Rejected/Withdrawn), `version` | Decision actor/time/reason. Approval mutates booking and request in one transaction |
| `allocation_guards` | `_id` = resource ID + Manila date; incrementing `version` | Shared catalog guard; guards coordinate transactions, not inventory or TTL holds |
| `outbox` | `_id`, aggregate ID/version, message type, due time, recipient, payload, attempts, delivery state | Lease token/expiry, sent time, provider reference, last error code. Use existing leased delivery mechanism |
| `pos_inbox` | `_id`, `source`, `externalEventId`, `reservationId`, payload hash, source revision, received time, processing state | Normalized authenticated evidence, processed time, rejection reason. Unique source/event ID rejects replay; mismatched replay payload is an error |

Private and whole-shop reservations embed allocations. Group participants reference a session and seats instead; the session owns physical allocations. Keep a single authority per allocation; do not independently write a second allocation collection. For availability, query both active reservation allocations and active session allocations plus blocks, under the same guards.

### Reservation field contract

Required fields, BSON types and enums are in the schema. Conditional invariants enforced by Business and transactions:

- `startUtc < endUtc`; all allocation intervals valid and within approved hours; preparation/cleanup may widen the protected interval and must be included in checks.
- `source=Online` requires `accountId`, verified account email and a matching customer link. Assisted bookings require staff actor and a contact channel.
- A Pending reservation has exactly one expiry policy and a finite applicable deadline: `OnlineReview`, `StaffAgreed`, or `WholeShopDeposit`. Confirmation must race against expiry using status/version/deadline predicates.
- WholeShop requires `event`, accepted `deposit`, quote reference and terms; `depositDeadlineUtc` replaces `reviewDeadlineUtc`. Required deposit must be positive, verified value nonnegative and currency PHP. Confirmation requires verified value at least required value and current eligible POS evidence.
- GroupCoaching requires `coachingSessionId` and positive `seatCount`, with no embedded physical allocations; attendees equal seats. Other service kinds require nonempty allocations. PrivateCoaching requires exactly one eligible coach plus the agreed eligible facility resources.
- Resource IDs are unique within a reservation allocation list. Attendees fit approved service and resource capacities; group session capacity is authoritative for shared lessons.
- Accepted policy/terms snapshots remain immutable. Store accepted quote revision and requirement amount so later catalog edits cannot rewrite the original agreement. Changes create another event and accepted revision.
- Every mutation checks expected version, authorization and ownership; writes audit and outbox in the same transaction. Payload hash is computed from normalized booking intent; duplicate keys with different intent fail rather than return another customer's booking.

### Indexes

| Collection | Index / purpose |
| --- | --- |
| reservations | Unique `reference`; unique `(idempotencyScope, idempotencyKey)`; `(accountId,startUtc)` partial where accountId exists; `(customerId,startUtc)` |
| reservations | `(status,reviewDeadlineUtc)` and `(status,depositDeadlineUtc)` with partial existence filters; `(allocations.resourceId,status,allocations.startUtc,allocations.endUtc)` for candidates, using `$elemMatch` for same-allocation overlap |
| reservations | `(coachingSessionId,status)` for participants; `(status,startUtc)` for attendance/reminders |
| coaching_sessions | `(status,allocations.resourceId,allocations.startUtc,allocations.endUtc)`; unique `_id` supplies seat-contention identity |
| customers | Unique `accountId` partial where string exists; names/phones are not unique customer identities |
| reservation_events | Unique `(reservationId,sequence)`; `(reservationId,atUtc)` |
| change_requests | `(reservationId,status)`; at most one Pending request per reservation through a partial unique index |
| schedule_blocks | `(resourceIds,startUtc,endUtc)` |
| outbox | Unique `_id` built from event ID, message type and recipient; delivery-state/due/lease index |
| pos_inbox | Unique `(source,externalEventId)`; `(processingState,receivedAtUtc)` |
| allocation_guards | Unique `_id`; no TTL deletion during active operations |

Indexes find candidates; they do not enforce nonoverlapping intervals. MongoDB validation cannot enforce cross-document capacity, reference integrity or authorization. Enforce these through Business orchestration and transactional persistence.

## 6. Concurrency and background processing

1. Acquire shared catalog guard, then every affected resource/date guard in stable order, including old/new allocations, coach buffers and dates touched by `[start,end)`. Ending exactly at midnight does not lock the next date.
2. In the transaction, re-read current state, policy, relevant schedules, session capacity and overlap candidates. Confirm that the catalog still contains all whole-shop facility resources. Never use an earlier availability response as authority.
3. Conditionally write expected version/status, reservation or session allocations, seat counters, audit events and outbox together. Retry transient transaction conflicts within bounded attempts; re-evaluate on retry.
4. Expiry uses the same guards and state predicates. Once expired, stale verification or confirmation cannot restore allocations. Persist a POS event even if it needs staff follow-up rather than capacity mutation.
5. A background worker processes deadlines independent of page visits, reminder delivery and attendance flags. Delivery checks reservation/session version and status to suppress stale messages. Short-notice confirmation sends one confirmation and suppresses a redundant reminder when less than 15 minutes remain (proposal).
6. Authenticated POS events are deduplicated durably; older source revisions cannot overwrite newer facts. If POS cannot supply revisions, fetch its current authoritative transaction before updating eligibility. An unrecognized booking/event goes to controlled reconciliation, never implicit creation.

## 7. Access, privacy and reporting

Customers see their account-linked reservations only, submit change requests, and never inspect other group participants' contacts. Front desk handles daily operations; supervisors authorize exceptions and term revisions; administrators configure catalogs/roles but receive operational permissions only through explicit role assignment. Staff cannot self-escalate. Public availability contains resource/time availability only, never customer names or payment details.

Store organizer contact snapshots for historical communication and a customer ID for current contact information. Do not collect attendee identities for ordinary bookings unless an operational need is established. No card data, payment credentials, identity-document scans or public proof URLs. Restrict POS evidence access. Decide retention periods with the owner before production; do not invent destructive TTL retention for reservations, audit or payment evidence.

Reporting separates Pending held minutes, Confirmed reserved minutes and Completed scheduled minutes. Denominator is eligible resource opening time minus closures/maintenance; count half-open intersections once per physical resource. Group participants must not multiply physical utilization. Coach utilization and learner seats are separate measures. No report claims actual play time or sensed occupancy.

## 8. Existing implementation gaps and rollout

Observed 2026-10-31 in the working tree: reservation records embed contact data and resource IDs but do not model customers, coaches, policy versions, deposit evidence or optimistic versions. Migration 002 creates limited required-field validation. Existing coaching and whole-shop schedule constraints live in `ReservationRules`; group session seat accounting is absent. Whole-shop is excluded from ordinary expiry but has no separate deposit deadline. Existing ordinary expiry is invoked by reservation service calls; this design requires independently scheduled processing. These observations are not a complete code audit.

Apply through a future numbered migration, never by modifying an already-applied migration or running migrations on Web startup. Expand first: add nullable/new fields and collections, deploy compatible readers, backfill in bounded batches, verify records, then enforce stricter validators. Existing whole-shop bookings without accepted terms require staff reconciliation, not invented historical deposits/deadlines. Existing coaching bookings require explicit coach/session assignments. Preserve IDs, references, credentials and history. Resolve duplicate references/keys before creating unique indexes. Change PascalCase storage only through explicit serializer mapping or a planned compatible backfill.

Work must reach Data records/migrations/persistence, Business rules/DTOs/services, Web validation/forms/scheduling/staff workflows, worker registration/configuration, POS adapter, fixtures and tests. Existing uncommitted application work is outside this documentation change.

## 9. Acceptance scenarios

- Two simultaneous overlapping bay or coach bookings: one succeeds; adjacent intervals both succeed.
- Multi-bay request with one occupied bay: nothing allocated; rejected reschedule preserves original booking.
- Whole-shop versus bay, group session, closure or catalog addition: exclusivity preserved.
- Confirmation versus expiry at the exact deadline: one legal outcome; no orphan allocations or duplicate seat release.
- Review request outside coverage or too near start: no hold created; 15/25-minute messages deduplicated.
- Two customers request the final group seat: one succeeds; expired participant releases exactly one seat.
- Duplicate submission: same scope/key/intent returns original; different intent conflicts; staff keys do not collide across actors.
- Partial, duplicate, stale, reversed, mismatched-currency and late POS evidence never wrongly confirms a booking.
- POS outage: pending verification remains unverified; late payment after expiry triggers reconciliation.
- Whole-shop quote revision and cancellation: old agreement retained; refund never inferred from cancellation.
- Customer cannot view another reservation or group member; stale staff edit fails.
- Manila midnight and buffers checked consistently; utilization counts shared sessions once.
- Worker restart replays due work safely; failed email never rolls back a booking.

## 10. Owner review checklist

Before activation, approve review coverage; 90-day/30-minute booking limits; staff Pending expiry; capacities and coach roster; lesson durations/buffers; online coaching availability; whole-shop package/setup scope; default 24-hour offer; negotiated deposit amounts and acceptable POS evidence; refund/reschedule terms; early check-in and completion rules; POS authentication/event contract; retention and notification channels. Until approved, leave affected online offerings disabled rather than inventing customer commitments.
