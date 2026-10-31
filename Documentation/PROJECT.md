# Project Documentation

## Source and implementation boundary

The Hole In Hub Golf & Lounge Reservation Management Subsystem manages customer reservations, schedules, allocations, and records within the Hole In Hub Integrated Management System.

Aligned on 2026-10-27 with the owner's revised `Hole In Hub Golf & Lounge Reservation Management Subsystem (4).pdf`, Chapter I, PDF pages 6–17. [Chapter I](CHAPTER_1.md) transcribes the manuscript; this document translates it into engineering requirements. This revision supersedes earlier guest-submission, indefinite Pending-hold, relational-database, 24-hour-reminder, and demonstration-only POS requirements. Client-approval qualifications remain open decisions.

These are planned requirements, not claims of completed functionality. The repository has MongoDB/migration, health-probe, and local asset foundations. This documentation alignment changes no application behavior.

## Operational background

Customers currently request bookings through Viber, SMS, social media, or direct staff contact. Staff enter details into an existing browser-based POS scheduling interface. Interviews describe overlooked conflict warnings, bay transfers, suspected duplicate-booking investigations, and customer contact details retained in messaging conversations.

Multi-bay group appointments and consolidated POS billing are distinct: sequential bill merging is not evidence that reservation records must be merged individually. Duplicate customer profiles and the technical cause of reported connectivity disruption are not established findings. Performance improvements require measurement.

## Scope and business rules

| Topic | Revised requirement |
| --- | --- |
| Resources | Eight bays: Bay 1–6 and VIP 7–8; applicable private spaces, golf coaching, and whole-shop rentals |
| Public access | Browse services and availability without signing in |
| Online submission | Customer account with verified email required |
| Staff-assisted booking | Authorized staff may book for walk-in, phone, or messaging customers without requiring customer accounts |
| Ownership | Customers access their own account-linked reservations and submit change requests for staff review |
| Submission | Successful submission creates Pending and immediately holds all requested allocations; browsing alone holds nothing |
| Ordinary online Pending | Proposed maximum 30-minute review deadline during designated staff review hours; staff reminder at 15 minutes and escalation at 25 minutes |
| Pending expiry | Unresolved ordinary online requests become Expired at the deadline, release all allocations, and notify customer and staff |
| Review coverage | Staff review hours and outside-hours handling require client approval; venue opening hours do not establish review coverage |
| Confirmation | Authorized staff approve; whole-shop confirmation requires recorded deposit verification |
| Whole shop | Exclusive allocation of all facility resources; separately agreed deposit-verification deadline |
| Operating hours | Monday 11 AM–midnight; Tuesday–Sunday 10 AM–midnight, Asia/Manila; midnight begins the following day |
| Duration | Walkthrough shows 30 minutes to 10 hours in 30-minute increments; applicability to each service and selectable start-time grid require confirmation |
| Availability | Entire interval must fit operating hours and respect existing allocations, closures, and maintenance |
| Multi-bay | One reservation; all requested resources allocated together or the entire attempt rejected |
| Changes | Staff review customer requests; reassignment, extension, and rescheduling recheck conflicts; rejected/failed changes preserve the original booking |
| Attendance | Authorized staff record arrival, cancellation, completion, and no-show outcomes; no hardware occupancy detection |
| Grace period | Confirmed allocations protected for one hour after start, capped at scheduled end; flag unattended bookings at the earlier deadline |
| No-show | Staff follow up and may mark No-show, releasing remaining allocations; no automatic no-show decision |
| Late arrival | No automatic end-time extension; extension requires availability check and staff approval |
| Communication | Email confirmation after staff approval; automated reminder approximately 15 minutes before reservation; manual staff contact remains separate |
| Chatbot | Predefined responses to basic inquiries; cannot independently approve bookings or resolve complex concerns |
| Reporting | Reservation counts, service usage, peak periods, cancellations, no-shows, customer trends, and reserved-time resource utilization |

The proposed expiry and late-arrival procedures retain the client-approval qualifications in Chapter I (pages 9 and 14–15). Do not apply ordinary online expiry to staff-assisted or whole-shop bookings without an agreed policy. Review-clock behavior across coverage boundaries is unresolved.

## Reservation lifecycle and access

Submission, approval, arrival, completion, cancellation, expiry, and no-show are distinct events. Expired applies to unresolved ordinary online Pending requests at the agreed review deadline; No-show applies to confirmed unattended bookings after staff follow-up. Payment/deposit statuses remain separate from reservation status. A deposit or payment response alone does not establish attendance or completion.

Record actor, timestamp, reason, and relevant changes in history. Exact status identifiers, exceptional transitions, and completion criteria must be specified during feature design; the paper does not establish a complete transition matrix.

| Role | Responsibility |
| --- | --- |
| Visitor | Browse services, availability, and predefined inquiry responses |
| Verified-email customer | Submit reservations, access own linked records/history, request changes |
| Front-desk staff | Staff-assisted booking, customer lookup, schedule/Pending review, confirmation, changes, attendance and outcomes |
| Supervisor | Operational oversight, reporting, and permitted exceptions |
| System administrator | Authorized account/role, resource, schedule, and configuration management |

Enforce server-side authorization and ownership on protected operations. A reservation reference alone is not authorization. Contact records remain distinct from login accounts to support staff-assisted bookings. Detailed role permissions require agreement.

## Architecture and data

Retain one ASP.NET Core MVC application and `HoleInHub.Web → HoleInHub.Business → HoleInHub.Data`. Web owns HTTP/UI concerns; Business owns reservation policy and orchestration; Data owns MongoDB persistence and transaction mechanics. See [Architecture](ARCHITECTURE.md) and the [foundation specification](specs/2026-10-18-production-foundation.md).

MongoDB is the sole database, with MongoDB Atlas specified in Chapter I and a local replica set for development/integration tests. Proposed logical records include customers, accounts/roles, services/resources, reservations and allocations, schedules/blocks, history, notification delivery, and applicable POS transaction references/statuses. Collection design is not finalized.

- Store timestamps in UTC and derive venue-local calendar dates using Asia/Manila.
- Use half-open intervals `[start, end)` and persisted resource/date guards within MongoDB transactions to prevent concurrent incompatible allocations.
- Commit all resources together, including whole-shop exclusivity; failed changes preserve original allocations.
- Use submission idempotency keys and stale-edit protection. Preserve history after capacity release.
- Expiry and confirmation must compete safely against the same reservation state; release allocations only when the eligible Pending request actually expires.
- Deliver notifications after commit; delivery failures do not undo reservations. Prevent duplicate/stale messages after changes.
- Run migrations only through the dedicated CLI under its distributed lease, never on Web startup.

## POS integration boundary

Integration with the designated POS subsystem through an agreed interface is in scope. A simulated handoff alone does not satisfy the revised integration requirement.

| Direction | Information exchanged |
| --- | --- |
| Reservation → POS | Reservation identifier/reference, relevant customer/service details, scheduled interval, and resource allocations needed for billing |
| POS → Reservation | Applicable transaction references and payment/deposit statuses, supporting agreed deposit verification |

Reservation owns allocation, staff approval, and lifecycle decisions. POS or its connected payment service owns billing, collection, sales transactions, receipts, vouchers, whole-shop deposit processing, and settlement. Reservation does not implement a billing engine or payment ledger.

The teams must document the integration flow, data exchange specification, and failure-handling rules. Agree field mapping, correlation identifiers, verification evidence, and repeated-notification handling. Missing/unsuccessful POS responses never verify a deposit. Repeated notifications must not duplicate records or incorrectly verify deposits. Protocol, endpoints, authentication, retry rules, and ordering behavior remain to be agreed; the paper does not select them.

## Reporting and evaluation

Utilization measures reserved resource time, not sensed occupancy or actual play time. Define denominator, closure/maintenance exclusions, and status inclusion before evaluation; distinguish Pending holds from fulfilled bookings. Check reports against known records and test data.

Document expected/actual results and pass/fail outcomes for representative booking tasks, overlapping/concurrent attempts, multi-bay allocation, whole-shop exclusion, safe changes, ownership/role restrictions, Pending alerts/expiry, attendance handling, notifications, report accuracy, usability, and response times. Integration tests cover reservation retrieval, transaction-status updates, deposit verification, unsuccessful exchanges, and repeated notifications. Record workloads and operating conditions; no measured baseline, improvement, or evaluation results are claimed.

## Delimitations and open decisions

Excluded from Reservation implementation: inventory, food/merchandise ordering, accounting, payroll, billing/payment processing, receipt generation, voucher processing, and direct simulator hardware control. Notifications depend on valid contacts, connectivity, and provider availability.

Still requiring agreement:

- Staff review coverage, outside-hours requests, and precise review-deadline calculation.
- Whole-shop deposit amount, verification evidence/deadline, failure handling, and refunds.
- Change-request approval criteria and late-arrival exceptions.
- Service-specific durations, start-time grid, attendee capacities, and resource eligibility.
- Detailed permissions, status transitions/completion criteria, reporting inclusion, notification provider/channel, and short-notice reminder behavior.
- POS exchange contract and test arrangements with the participating team.

The [aligned diagram package](diagrams/README.md) provides system architecture, system flowchart, context/Level 1 references, a combined Level 2 DFD with detail sheets, and POS integration flow. The final POS exchange contract remains to be agreed. Distinguish requirements awaiting operational approval from implemented foundation behavior.
