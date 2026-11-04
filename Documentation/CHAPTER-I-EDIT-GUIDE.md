# Chapter I synchronization: exact edit guide

Compared on 2026-11-04 against the Google document supplied by the owner and the current working-tree implementation. The Google document was read/exported only; it has not been edited. Use heading names and paragraph descriptions below because page numbers change with Google Docs layout.

Source manuscript: https://docs.google.com/document/d/1AMkSOE_qeHkxGIzejvAJxTRC3hBpynhqcIHzG3QUZ2I/edit

Implementation references: CURRENT-SYSTEM.md, DATABASE.md, PROJECT.md, account/booking controllers, ReservationRules, BookingRules, reservation persistence and migrations 001–007.

## 1. Introduction: replace paragraph 4 (Pending review and grace period)

Replace the entire paragraph discussing temporary holds with:

> The subsystem distinguishes unverified booking drafts, Pending requests and Confirmed reservations. Guest customers complete email-code verification before a request creates a capacity hold; verified account holders submit directly. Under the current owner-directed provisional policy, Regular requests have a 30-minute review period, while VIP and coaching requests have 60 minutes. Whole-shop demonstration requests have a separate 60-minute deposit deadline. Requests must be submitted during operating hours and the configured approved review coverage, with the complete applicable period fitting before closing and the scheduled start. Unresolved eligible requests expire and release their capacity. The one-hour late-arrival and staff-managed no-show procedure remains a target workflow and is separate from Pending expiry.

This changes current policy, not the interview account. Preserve the distinction between owner-directed provisional rules and final client acceptance.

## 2. Introduction: qualify the planned workflows in paragraph 3

Keep its description of the proposed overall subsystem. Immediately after that paragraph, insert:

> The present implementation supports customer and guest submissions, multi-bay allocation, staff review, coaching confirmation and staff decisions on cancellation requests. General staff-assisted booking creation, reassignment, extension, rescheduling, attendance recording and no-show administration remain planned workflows.

Do not remove these from the project objectives merely because development is incomplete.

## 3. Project Background and Context: add current catalog after paragraph 1

The first paragraph reports the original walkthrough's eight bays and duration menu. Retain it as historical evidence; do not silently rewrite what the walkthrough showed. Add:

> The original walkthrough described eight bays. The current application catalog was subsequently revised to seven active resources: five regular bays displayed as Bay 1–5 and two VIP bays displayed as VIP 1–2. This catalog reflects the subsequent owner direction and should be confirmed against the venue's final approved configuration. Ordinary application sessions use 30-minute start and duration increments, with a minimum of 30 minutes and a maximum of 14 hours subject to the day's opening hours. Fixed coaching sessions and whole-shop packages retain their own schedule restrictions.

The original ten-hour menu can remain a historical observation; do not describe it as the current application-wide maximum. Do not put retired database identifiers into this business-background paragraph.

## 4. Project Background and Context: replace paragraph 6 (review alerts/deposits)

Use:

> Regular online requests use a provisional 30-minute review deadline, with staff alerts at 15 and 25 minutes. VIP and coaching requests use a 60-minute review deadline, with alerts at 30 and 50 minutes. Each deadline is saved with the booking so that subsequent policy changes do not silently rewrite existing commitments. Unresolved eligible requests become Expired, release their held resources and queue customer/staff notifications. Whole-shop demonstration bookings use a 60-minute deposit deadline and a simulated deposit of 50% of the demonstration rental amount; successful simulated deposit processing confirms the request. These settings are owner-directed implementation rules requiring operational validation with the client. They do not establish live payment collection or final deposit/refund terms.

## 5. Project Background and Context: qualify the POS paragraph (paragraph 8)

Retain the intended integration boundary, then append:

> The current application demonstrates whole-shop deposit outcomes through an explicitly enabled Development/Demo checkout. It does not yet exchange authenticated payment evidence with the designated POS, collect money or produce receipts. Live POS connectivity, evidence validation, reconciliation and duplicate-event handling remain integration deliverables to be implemented and evaluated with the participating team.

Do not describe the simulated outcome as an actual bank/POS-verified payment. Preserve that failed/missing live evidence must never verify a real deposit.

## 6. Specific Objectives: replace items 1–8

Keep the Main Objective as the overall research goal. Replace the numbered specific objectives with the following so implemented and planned work are distinguishable:

1. To develop service and availability views with interactive date, time and resource selection for single-bay, multi-bay and whole-shop requests under one reservation record, and to extend staff scheduling with the planned Gantt-style view.
2. To enforce operating-hour, resource-eligibility, closure and maintenance checks, together with persisted concurrency controls and submission idempotency, so competing requests cannot commit overlapping allocations or duplicate effects. Planned reassignment, extension and rescheduling shall apply the same checks and preserve existing bookings when replacement operations fail.
3. To support online submission by email-verified guests and verified account holders, optional Google authentication, ownership-restricted account pages and secure guest status links. Staff functions shall use role-based authorization, with general walk-in, phone and messaging creation retained as a planned staff workflow.
4. To manage contact snapshots, total guest counts, reservation references, histories, staff review and cancellation requests. Regular requests shall use a provisional 30-minute review deadline with alerts at 15/25 minutes; VIP and coaching shall use 60 minutes with alerts at 30/50 minutes. Whole-shop demonstration requests shall use a 60-minute deposit deadline and simulated 50% deposit confirmation. Attendance, completion, no-show administration and general booking changes remain planned workflows requiring approved operational rules.
5. To maintain MongoDB account, resource, reservation, scheduling, verification and notification records through controlled persistence, indexes and explicit migrations. To develop the agreed live POS interface for reservation details, transaction references and deposit/payment statuses while preventing duplicate effects and incorrect verification. The current demo shall remain distinct from live integration.
6. To provide ownership-restricted customer reservation histories and transaction-status views, and to develop the planned searchable staff records and administrative reports for reservation counts, service usage, reserved-time utilization, peak periods, cancellations, no-shows and customer trends. Report outputs shall be validated against known records once implemented.
7. To deliver verification messages, booking-status notifications and Pending-review alerts through the durable email outbox, and to develop the planned pre-reservation reminders and predefined-answer chatbot. Automated messages shall remain separate from manual staff contact; whole-shop demo confirmation messages shall be identified separately from staff approval.
8. To evaluate implemented features through documented booking, access-control, concurrency, idempotency, expiry, notification and demo-payment scenarios. Future attendance, reporting and live POS tests shall be recorded as pending until the corresponding features and partner interface exist. Evaluation shall report actual expected/observed outcomes and shall not infer production readiness or performance improvement solely from automated test success.

## 7. Scope: replace the introductory sentence

Use:

> The project scope includes implemented reservation features and planned extensions. The following items distinguish the current application from functions that remain subject to further development, integration or operational approval.

This avoids presenting every listed capability as already available.

## 8. Scope: replace items 1–5

### Item 1: Customer Booking and Reservation Access

> Customers can browse services and availability anonymously. Guests enter contact details, verify an email code and receive a Pending booking receipt with secure status access; verified account holders submit directly and access their own reservations. Unverified drafts hold no capacity. Account features include email/password access, email verification and recovery, optional Google authentication, contact-profile editing and owned transaction-status views. Booking contact edits affect that reservation rather than the saved account profile. General staff-assisted creation for walk-in, phone and messaging customers remains planned, while existing review functions require Staff/Admin authorization.

### Item 2: Resource Scheduling and Allocation

> The current catalog contains five regular bays (Bay 1–5) and two VIP bays (VIP 1–2). Single-bay, multi-bay and whole-shop requests use calendar/time selection and atomic allocation of all selected resources. Whole-shop requests include every active bay. Venue hours are Monday 11:00 a.m.–midnight and Tuesday–Sunday 10:00 a.m.–midnight in Asia/Manila. Ordinary sessions use half-hour increments, minimum 30 minutes, within opening hours; whole-shop packages use 3:00–9:00 p.m. or opening–midnight. Availability considers recorded active reservations and schedule blocks. Separate private-space resources, named-coach availability and a general staff Gantt scheduling view are not currently implemented.

### Item 3: Reservation Administration and Accountability

> Staff review supports approval/rejection, recorded coaching confirmation and approval/decline of cancellation requests. Regular requests use a 30-minute review period and 15/25-minute alerts; VIP/coaching use 60 minutes and 30/50-minute alerts. Unresolved eligible Pending requests expire and release capacity. Customers may request cancellation of Pending or Confirmed bookings at least one hour before start; the request alone does not release resources. Whole-shop demonstration requests use a 60-minute deadline and confirm after a successful simulated 50% deposit. General rescheduling, extension, attendance, completion and staff-managed no-show workflows remain planned. The proposed grace-period flag shall not itself cancel a confirmed booking or automatically release capacity.

### Item 4: Reservation Communication and Operational Reporting

> The application queues verification messages, booking receipts/status messages, Pending-review alerts and expiry notifications through a durable SMTP outbox. Delivery is retried and is not a guarantee of immediate receipt. Customer pages display owned reservation history and demonstration transaction status. Automated pre-start reminders, a predefined-answer chatbot, administrative search/reporting and operational analytics remain planned. Any later utilization reports shall measure reserved resource time using documented status rules rather than physical occupancy.

### Item 5: Data Management and POS Integration

> MongoDB stores accounts, resources, reservations, schedule blocks, allocation guards, verification records, notification outbox records and migration metadata. Reservation documents embed contact snapshots, selected resource IDs and history; the current model does not use separate customer, allocation or coaching-session collections. Database validators provide limited structural checks, while application validation, unique indexes and transactional guards enforce additional rules. Live POS integration remains planned. The present checkout simulates payment outcomes only and does not collect money, settle accounts, issue receipts or authenticate real payment evidence.

## 9. Delimitations: replace the full paragraph

Use:

> The Reservation subsystem covers scheduling, capacity allocation and reservation records. Inventory, merchandise and food ordering, accounting, payroll, real payment processing, receipts, voucher processing and direct simulator-hardware control remain outside its implementation. The current whole-shop checkout is an explicitly enabled Development/Demo simulation, with no actual collection or production POS verification. Deposit percentages, deadlines, example prices, capacity assumptions and coaching schedules used by the demonstration require final client validation before operational rollout. Online submission requires the full review window to fit operating hours, approved review coverage and booking start; requests outside those conditions are unavailable. The application does not detect physical arrival or bay occupancy. General staff-assisted creation, rescheduling, extensions, attendance/completion/no-show workflows, operational reports, the chatbot and pre-start reminders remain planned. Named-coach scheduling, separate private-space resources, live POS evidence, reconciliation and refund rules are not established by the current implementation. Current automated communication uses email; SMS/Viber integration is not implemented. Delivery depends on valid contacts, connectivity, SMTP availability and operation of the background worker. Customer transaction pages show reservation-related demo status and history, not an accounting ledger or official receipts.

Keep broader research commitments in the objectives; this paragraph records current delivery limits rather than cancelling future requirements.

## 10. Operational Definition of Terms: replace/add entries

Replace Simulator Bay with:

> **Simulator Bay.** An individually selectable golf-simulation resource whose schedule is protected against conflicting allocations. The current application displays five regular bays, Bay 1–5, and two VIP bays, VIP 1–2; the earlier walkthrough described a different catalog.

Replace Reservation History with:

> **Reservation History.** Timestamped status and action entries associated with a reservation, including the recorded actor and applicable reason. These entries are embedded in the reservation document and displayed through authorized account or guest access.

Keep Analytics, Bay Utilization and Gantt-Style View as target concepts, but add to each entry: “This capability remains planned in the current implementation.” The current calendar/time picker does not establish a completed staff Gantt view or analytics dashboard.

Add, alphabetically:

> **Guest Booking.** A booking submitted without creating an account after verification of the customer's email address; subsequent access uses a protected status link.
>
> **Pending Reservation.** A verified, committed request that temporarily protects its resources until confirmation, rejection, cancellation or expiry under its saved policy.
>
> **Review Deadline.** The saved time by which an eligible Pending request must be resolved before expiry. Current provisional windows differ by service.
>
> **Idempotency.** Protection that returns the existing result for a repeated submission key with the same request and rejects reuse with different details.
>
> **Simulated Deposit.** A demonstration payment outcome used to exercise whole-shop booking behavior without collecting money or verifying an actual POS transaction.
>
> **Secure Status Link.** A bearer link authorizing access to a guest reservation; a booking reference alone is insufficient.

## 11. Where database detail belongs

The supplied document contains Chapter I only: there is no current schema chapter, database diagram or data dictionary to replace in that export. Keep collection/field implementation details out of the Introduction. Add a technical-design subsection in the later design chapter, or an appendix titled “Implemented MongoDB Data Dictionary,” using Documentation/DATABASE.md and the C# models as sources.

Include collection purpose, exact BSON field names/casing, types/optional values, indexes, embedded records, transaction boundaries and migrations. Distinguish the typed current schema (reservation.schema.json), its full interval validator (reservation.validator.json), and the historical proposal (reservation.proposed.schema.json). AccountId does not imply every customer has a login account: guests use an internal ownership scope. Never include credentials or real customer data.

Do not create an ERD suggesting separate customer/allocation/coaching collections already exist. If a future design includes them, label it proposed and explain the migration needed.

## 12. Where diagrams belong

In the later architecture/design section, place a current Web → Business → Data/MongoDB diagram and the guest/account booking flow from CURRENT-SYSTEM.md. The separate Migrator CLI points to Data; migrations do not run during Web startup.

Update any later booking flow so email verification precedes a guest capacity hold, Regular and other service deadlines branch correctly, and whole-shop demonstration deposit confirmation is labelled simulated. Draw attendance, rescheduling, reporting, chatbot and live POS as planned paths or separate proposed diagrams. Keep original October exports as historical appendices if needed; do not relabel them current without editing their boxes/arrows.

## 13. Keep these parts

Keep the interview evidence about messaging, overlooked warnings, bay transfers and suspected duplicates; the distinction between reservation grouping and consolidated billing; UTC/Manila and half-open interval design; all-or-nothing multi-resource allocation; the separation of Pending expiry from no-show; preservation of original bookings after failed changes; the POS billing boundary; and the absence of a measured performance baseline.

Do not change cover/approval dates, institutional titles or signatures based on software changes. The approval sheet contains an incomplete sentence at the end of its opening paragraph; complete it using the school's approved template. Confirm names/titles against school records rather than guessing.

## 14. Repository follow-up discovered by this cross-check

Documentation/PROJECT.md still contains an older one-hour-minimum sentence in its open-decisions list, although its scope table and subsequent owner update specify 30 minutes. Replace only that old minimum with 30 minutes. Keep CHAPTER_1.md a faithful transcription of the older manuscript until the Google-document revision is accepted; annotate its historical status rather than silently rewriting the source transcript.

## Completion boundary

This guide supplies proposed manuscript edits. It does not alter the Google document, approve venue policies, certify live database state, or make unimplemented features complete. Retain future-tense objectives where they are intentional project commitments and use the status qualifiers above to report current delivery honestly.
