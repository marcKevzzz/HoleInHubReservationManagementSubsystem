# Aligned system diagrams

Prepared 27 October 2026 from the revised Chapter I, [business requirements](../PROJECT.md), supplied diagram drafts, and the owner's subsequent objective/scope wording in chat. These are proposed designs, not claims of implemented features or approved client operating policies. Existing source images in Downloads were not modified.

## Simple version

For easier reading and individual editable files, use the [simple diagram package](simple/README.md). It separates booking, attendance and changes, and keeps one Level 2 process group per page.

## Open and edit

- [Editable draw.io workbook](reservation-diagrams.drawio): open in diagrams.net or the draw.io desktop application. Each PDF sheet has a corresponding editable page.
- [Vector PDF, 12 sheets](reservation-diagrams.pdf): zoom without losing text clarity. The combined DFD is intentionally an oversized overview; use the individual Level 2 sheets for paper-sized layout and detailed reading.
- SVG files retain vector text and shapes for insertion into the manuscript. PNG previews are supplied for quick viewing; use SVG/PDF for final export.

| PDF page | Sheet | Purpose |
| --- | --- | --- |
| 1 | [Architecture](01-architecture.svg) | Customer/staff access, Web → Business → Data, Atlas, external POS/messaging, explicit Migrator |
| 2 | [System flowchart](02-system-flowchart.svg) | Booking through approval, expiry, staff attendance, no-show, changes and reporting |
| 3 | [Context DFD](03-dfd-context.svg) | Reservation boundary and customer/staff/POS/provider exchanges |
| 4 | [Level 1 boundary reference](04-dfd-level1.svg) | Six parent processes, with repeated endpoint/process IDs for readable boundary panels |
| 5 | [Combined Level 2 DFD](05-dfd-level2-combined.svg) | One connected decomposition of all six parent processes, containing 21 subprocesses |
| 6 | [Level 2: 1.0 Accounts/access](06-dfd-level2-1.svg) | Registration, email verification, authentication, role/ownership authorization |
| 7 | [Level 2: 2.0 Reservations](06-dfd-level2-2.svg) | Availability, capture, atomic allocation, review/expiry/attendance, staff-reviewed changes |
| 8 | [Level 2: 3.0 Notifications](06-dfd-level2-3.svg) | Prepare eligible messages, delivery, delivery results |
| 9 | [Level 2: 4.0 Reports](06-dfd-level2-4.svg) | Authorized criteria, aggregation, records/report output |
| 10 | [Level 2: 5.0 POS/deposits](06-dfd-level2-5.svg) | Reservation exchange, result validation, verification review, recorded outcome |
| 11 | [Level 2: 6.0 Inquiries](06-dfd-level2-6.svg) | Predefined answer matching or staff referral |
| 12 | [Integration flow](07-pos-integration.svg) | POS exchange and Reservation-side handling of failures, invalid results, duplicates and verified deposits |

## Notation and balancing

The supplied drafts' numbering convention is retained: the context has process 0; Level 1 uses 1.0–6.0; Level 2 uses 1.1–6.2. The 21 Level 2 processes decompose six parents; they are not 21 consecutive workflow steps. A DFD represents data movement, while the system and integration flowcharts represent control/decision flow.

DFD green boxes are numbered processes; blue rectangles are external entities, except labelled sibling-process references on detail sheets; white boxes with D-identifiers are logical data stores. Repeated IDs denote the same logical entity/store/process, not separate systems or duplicate databases. All stores are logical MongoDB records; no collection layout is finalized. Blue arrows on the combined sheet are actual exchanges between process groups. Reference boxes on individual sheets identify those same connections. Lines crossing without an explicit shared endpoint are not junctions.

| Store | Logical content |
| --- | --- |
| D1 | Accounts, roles, verified-email state and customer-account links |
| D2 | Customer/organizer contacts and customer-account links |
| D3 | Reservations, allocations, review deadlines, status and recorded attendance |
| D4 | Services, resources, operating schedules, closures and maintenance blocks |
| D5 | POS transaction references/statuses and verification history; not a payment ledger |
| D6 | Approved predefined FAQ content |
| D7 | Reservation history and customer change requests/decisions |
| D8 | Notification delivery state and duplicate-prevention identifiers |

| Cross-process flow | Source → destination |
| --- | --- |
| F1 Authorized identity, role and ownership context | 1.4 → 2.2 |
| F2 Authorized staff context | 1.4 → 4.1 |
| F3 Committed reservation events | 2.4 → 3.1 |
| F4 Reservation/whole-shop deposit request | 2.4 → 5.1 |
| F5 POS status/deposit verification outcome | 5.4 → 2.4 |
| F6 Committed change outcome | 2.5 → 3.1 |

Parent boundary flows are derived from the same process definitions as the Level 2 sheets. The context groups related field-level flows; individual Level 2 sheets retain their incoming/outgoing sibling references. Account access results cover identity/permissions only: every reservation operation must still enforce current role and ownership, and customers may request changes rather than directly approve them.

## Process rules used in the drawings

- **Access:** public service/availability browsing needs no login. Online submission requires verified email; staff-assisted bookings require authorized staff, not a customer account. Failed authentication or unverified email cannot reach submission. The flowchart groups access validation rather than drawing every sign-in error screen.
- **2.3 Atomic save:** validate operating hours, closures/maintenance, resource eligibility and persisted concurrency guards. One transaction commits the Pending reservation/reference, every requested allocation, idempotency result and history. The following flowchart box displays the committed result; it is not a second write. A repeated submission key returns the original result. Whole-shop allocations exclude every facility resource for the interval.
- **2.4 Review/expiry:** only eligible ordinary online Pending requests use the proposed 30-minute clock during agreed review coverage. Staff alerts occur at 15/25 minutes; unresolved eligible requests become Expired, release all allocations and notify customer/staff. Expiry competes safely with approval, so a stale timer cannot expire a confirmed booking. Staff-assisted and whole-shop requests do not inherit that timer without an approved policy.
- **2.4 Attendance:** confirmed unattended bookings are flagged at `min(start + 1 hour, scheduled end)`. Only staff record arrival and No-show after customer follow-up. The timer does not detect occupancy or automatically mark No-show. Remaining allocations can be released after the recorded outcome; no extension or renewed hold is implied after the scheduled end. Completion criteria remain to be agreed.
- **2.5 Changes:** customers submit requests for their own account-linked reservations; authorized staff decide. Proposed replacement allocations use 2.3, including version/conflict checks, and commit together. Failure/rejection preserves the original record and allocations. Record change history; successful changes revise notifications and relevant POS details. Reconfirmation after changed terms is a policy decision.
- **3.0 Communication:** approval email only follows committed staff confirmation, not deposit verification alone. Reservation reminders are approximately 15 minutes before start. Review alerts at 15/25 minutes are separate from the pre-start reminder. Automated notices remain distinct from staff's manual customer contact. Delivery failure never rolls back a booking; current-state and duplicate checks prevent stale/repeated effects. The provider carries actual messages; DFD customer outputs represent the logical customer-facing communication.
- **4.0 Reporting:** filters and role checks precede reporting. Show reservation counts, services, peak periods, cancellations, no-shows, customer trends and reserved-time utilization. Status inclusion and bookable-time denominator require agreement. No sensor-derived occupancy or actual playing time is claimed.
- **5.0 POS:** exchange relevant reservation/customer/service/time/allocation details and receive transaction references and applicable payment/deposit statuses. Whole-shop deposit evidence and required staff verification must satisfy the agreed contract before deposit verification is recorded. A failed, absent, invalid, or insufficient result cannot establish verification. Repeated results preserve the previously recorded outcome without duplicate effects. Ordinary payment-status updates may be recorded without any whole-shop deposit requirement.
- **6.0 Assistance:** return approved predefined answers or refer unmatched/complex questions to staff; never approve bookings independently.

## Decisions still requiring client/team agreement

1. Staff review coverage, outside-hours submissions, clock start and behavior across review-hour boundaries. Venue opening hours do not establish review coverage.
2. Whole-shop deposit amount/evidence, verification deadline, follow-up and missed-deadline outcomes. No automatic deposit-timeout cancellation has been invented.
3. Change approval/reconfirmation, late-arrival exceptions, exact status transitions and completion criteria.
4. Service eligibility/durations, selectable start times, attendee capacities, report inclusion and short-notice reminder handling.
5. POS protocol/endpoints, authentication, correlation identifiers, payloads, status/evidence mapping, ordering, failure/retry policy and integration test arrangements.

## Regeneration and checks

Run `generate_diagrams.py` with Python and ReportLab to regenerate the draw.io workbook, 12 SVG sheets and PDF. No runtime website dependency is added. The script checks node bounds/text fit, edge endpoints, the presence of all 21 Level 2 subprocesses, and an input/output for each. Render the PDF with Poppler and visually check labels/arrows after editing. These checks validate the artifacts, not application behavior or client approval.

The existing Chapter I file remains a transcription of the supplied PDF; the refined objective/scope wording discussed afterward has not silently replaced the source manuscript. These diagrams use the clarified rules from that discussion.
