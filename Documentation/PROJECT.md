# Project Documentation

## Source and implementation boundary

The Hole In Hub Golf & Lounge Reservation Management Subsystem manages customer reservations, schedules, allocations, and records within the Hole In Hub Integrated Management System.

Aligned on 2026-10-27 with the owner's revised `Hole In Hub Golf & Lounge Reservation Management Subsystem (4).pdf`, Chapter I, PDF pages 6–17. [Chapter I](CHAPTER_1.md) transcribes the manuscript; this document translates it into engineering requirements. This revision supersedes earlier guest-submission, indefinite Pending-hold, relational-database, 24-hour-reminder, and demonstration-only POS requirements. Client-approval qualifications remain open decisions.

This document records requirements and owner-directed provisional policies, not a completed-feature checklist. The working tree now includes accounts, guest/account booking, atomic allocation, staff review and demo checkout. See [current implementation](CURRENT-SYSTEM.md) for feature boundaries and [database structure](DATABASE.md) for implemented collections. Documentation synchronized on 2026-11-04; no application behavior changes.

## Operational background

The [IPT102 project timeline](PROJECT-TIMELINE.md) records the course milestones and links to the [Sprint 2 implementation plan](plans/2026-11-06-sprint-2-staff-operations.md). Apply the current requirements below where the timeline reflects an older policy.

Customers currently request bookings through Viber, SMS, social media, or direct staff contact. Staff enter details into an existing browser-based POS scheduling interface. Interviews describe overlooked conflict warnings, bay transfers, suspected duplicate-booking investigations, and customer contact details retained in messaging conversations.

Multi-bay group appointments and consolidated POS billing are distinct: sequential bill merging is not evidence that reservation records must be merged individually. Duplicate customer profiles and the technical cause of reported connectivity disruption are not established findings. Performance improvements require measurement.

## Scope and business rules

| Topic | Revised requirement |
| --- | --- |
| Resources | Five regular bays (Bay 1–5) and two VIP bays (VIP 1–2); coaching uses regular bays; whole-shop rentals allocate every active bay |
| Public access | Browse services and availability without signing in |
| Online submission | Guest submits details, verifies an email code, then receives a Pending request receipt; verified accounts skip codes. No account required; unverified drafts hold no capacity |
| Staff-assisted booking | Authorized staff may book for walk-in, phone, or messaging customers without requiring customer accounts |
| Ownership | Guests verify email before submission and use a secure status link. Find my booking verifies their email again for temporary access to their guest reservation history, including confirmed bookings; it never grants access to account-owned bookings with the same email. Verified account holders access their own account-linked reservations |
| Submission | Successful submission creates Pending and immediately holds all requested allocations; browsing alone holds nothing |
| Ordinary online Pending | Owner-approved provisional review deadlines: Regular 30 minutes (alerts 15/25); VIP/coaching 60 minutes (alerts 30/50) |
| Overdue review | An ordinary online Pending request releases its bay hold at the review deadline. It stays Pending, shows an Overdue flag, and notifies the customer and staff. Staff can confirm before the session starts if they can reserve the bays atomically, decline the request, or email a rebooking link if the slot is unavailable. Undecided requests expire at session start. |
| Online cancellation | Customers can cancel ordinary Pending and Confirmed bookings directly at least one hour before start, including exactly one hour before. Cancellation releases any remaining bay hold and notifies staff. Whole-shop cancellation requests follow the separate deposit workflow. |
| Review coverage | Owner directs review during operating hours only; review minutes pause outside operating hours and resume at the next staffed opening. The full review window must finish by the booked start. Existing operating hours remain authoritative |
| Confirmation | Staff approve Regular/VIP; coaching requires recorded coach confirmation plus venue approval. Whole-shop confirms automatically after a verified 50% deposit; currently demonstrated with simulated POS only |
| Whole shop | Exclusive allocation of all active bays. Provisional 60-minute deposit deadline; simulated 50% deposit confirms automatically. Live collection remains POS-owned and disabled |
| Operating hours | Monday 11 AM–midnight; Tuesday–Sunday 10 AM–midnight, Asia/Manila; midnight begins the following day |
| Duration | Ordinary sessions: minimum 30 minutes, 30-minute increments within opening hours (subsequent owner direction, reflected in current code). Whole-shop retains half day 3–9 PM or opening–midnight |
| Availability | Entire interval must fit operating hours and respect existing allocations, closures, and maintenance |
| Multi-bay | One reservation; all requested resources allocated together or the entire attempt rejected |
| Changes | Staff review customer requests; reassignment, extension, and rescheduling recheck conflicts; rejected/failed changes preserve the original booking |
| First change-request release | Owner-approved 7 November 2026. Confirmed Regular/VIP customers may propose a new schedule or duration at least one hour before the current start. The proposed start must allow the full 30/60-minute operating-hours review window. One open proposal, same service, bay count and guest count. Staff may select replacement bays during approval. Approval must precede both session starts and recheck revision and availability. Original allocations remain until successful approval. |
| Staff timeline VIP upgrade | Owner-approved 12 November 2026. Staff may upgrade an upcoming Confirmed single-bay Regular reservation to an available VIP bay after explicitly acknowledging customer agreement to VIP pricing. Preserve duration and guest count, recheck revision and allocation conflicts atomically, update the service and bay together, and record the upgrade in history. Multi-bay cross-service moves and VIP downgrades remain unavailable. |
| Attendance | Authorized staff record arrival, cancellation, completion, and no-show outcomes; no hardware occupancy detection |
| Grace period | Confirmed allocations protected for one hour after start, capped at scheduled end; flag unattended bookings at the earlier deadline |
| No-show | Staff follow up and may mark No-show, releasing remaining allocations; no automatic no-show decision |
| Late arrival | No automatic end-time extension; extension requires availability check and staff approval |
| Communication | Email confirmation after staff approval; automated reminder approximately 15 minutes before reservation; manual staff contact remains separate |
| Chatbot | Predefined responses to basic inquiries; cannot independently approve bookings or resolve complex concerns |
| Reporting | Reservation counts, service usage, peak periods, cancellations, no-shows, customer trends, and reserved-time resource utilization |

The owner approved these provisional online booking rules on 2026-11-03. Require 30-minute Regular lead time and 60-minute lead time for other online services; no maximum advance-booking limit. Owner update on 2026-11-07 allows submission at any time. Ordinary online Pending holds begin immediately. Their 30/60-minute review clock counts only configured staff coverage within venue operating hours, carries across closing, and must finish by the session start. Whole-shop retains its separate elapsed-time deposit deadline. Existing records retain their saved deadlines; no policy is inferred for staff-assisted bookings. Expiry timings still need operational validation with the client.

### Browser language support

The customer-facing web interface shall support English (`en`) and Korean (`ko`). On the first visit, it shall use the browser's preferred supported language, with English as the fallback. Customers shall be able to switch between **English** and **한국어**, and their choice shall take precedence over the browser preference on later visits.

Both languages shall cover navigation, service and availability information, guest and account booking, email-code entry screens, validation and error messages, booking receipts and status pages, and customer change and cancellation requests. Pages shall expose the active language to assistive technology. Both language versions shall use the same availability, booking rules, and Asia/Manila schedule.

Acceptance shall cover Korean- and English-preferred browsers, an unsupported browser language, persistence of a manual selection, and the same booking journey and error states in both languages on desktop and mobile. Confirm the Korean wording with a fluent reviewer. Staff screens and outbound email localization require separate scope decisions.

## Reservation lifecycle and access

Submission, approval, arrival, completion, cancellation, expiry, and no-show are distinct events. Expired applies to unresolved ordinary online Pending requests when the session starts. No-show applies to confirmed unattended bookings after staff follow-up. Payment/deposit statuses remain separate from reservation status. A deposit or payment response alone does not establish attendance or completion.

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

MongoDB is the sole database, with MongoDB Atlas specified in Chapter I and a local replica set for development/integration tests. Proposed logical records include customers, accounts/roles, services/resources, reservations and allocations, schedules/blocks, history, notification delivery, and applicable POS transaction references/statuses. The current collection layout is documented in [DATABASE.md](DATABASE.md); additional logical records remain target-design proposals.

- Store timestamps in UTC and derive venue-local calendar dates using Asia/Manila.
- Use half-open intervals `[start, end)` and persisted resource/date guards within MongoDB transactions to prevent concurrent incompatible allocations.
- Commit all resources together, including whole-shop exclusivity; failed changes preserve original allocations.
- Use submission idempotency keys and stale-edit protection. Preserve history after capacity release.
- Approval, cancellation, and final expiry must compete safely against the same reservation state. Ordinary Pending holds end at the review deadline, and any late approval must reacquire capacity atomically.
- Deliver notifications after commit; delivery failures do not undo reservations. Prevent duplicate/stale messages after changes.
- Run migrations only through the dedicated CLI under its distributed lease, never on Web startup.

## POS integration boundary

Integration with the designated POS subsystem through an agreed interface remains a production requirement. The owner authorized a clearly labeled simulated checkout for development/evaluation; this validates booking behavior but does not satisfy live POS integration. No real money is collected.

| Direction | Information exchanged |
| --- | --- |
| Reservation → POS | Reservation identifier/reference, relevant customer/service details, scheduled interval, and resource allocations needed for billing |
| POS → Reservation | Applicable transaction references and payment/deposit statuses, supporting agreed deposit verification |

Reservation owns allocation, staff approval, and lifecycle decisions. POS or its connected payment service owns billing, collection, sales transactions, receipts, vouchers, whole-shop deposit processing, and settlement. Reservation does not implement a billing engine or payment ledger.

The teams must document the integration flow, data exchange specification, and failure-handling rules. Agree field mapping, correlation identifiers, verification evidence, and repeated-notification handling. Missing/unsuccessful POS responses never verify a deposit. Repeated notifications must not duplicate records or incorrectly verify deposits. Protocol, endpoints, authentication, retry rules, and ordering behavior remain to be agreed; the paper does not select them.

## Reporting and evaluation

Utilization measures reserved resource time, not sensed occupancy or actual play time. Define denominator, closure/maintenance exclusions, and status inclusion before evaluation; distinguish Pending holds from fulfilled bookings. Check reports against known records and test data.

Document expected/actual results and pass/fail outcomes for representative booking tasks, overlapping/concurrent attempts, multi-bay allocation, whole-shop exclusion, safe changes, ownership/role restrictions, Pending alerts/expiry, attendance handling, notifications, report accuracy, usability, and response times. Production acceptance must cover reservation retrieval, transaction-status updates, deposit verification, unsuccessful exchanges and repeated notifications. Current automated coverage is described in [TESTING.md](TESTING.md); live POS acceptance remains future work. Record workloads and operating conditions; no measured baseline, improvement, or evaluation results are claimed.

## Delimitations and open decisions

Excluded from Reservation implementation: inventory, food/merchandise ordering, accounting, payroll, billing/payment processing, receipt generation, voucher processing, and direct simulator hardware control. Notifications depend on valid contacts, connectivity, and provider availability.

Still requiring agreement:

- Client validation of the owner-approved provisional review/deposit deadlines; review coverage uses operating minutes with carryover to the next staffed opening. Manual no-show actions and attendance corrections remain deferred.
- Live POS payment evidence/contract, authoritative prices (including holidays), late-payment reconciliation and deposit refunds. The owner sets a 50% deposit and provisional 60-minute deadline for the demo.
- Change-request approval criteria and late-arrival exceptions.
- Client confirmation of remaining package sessions and rescheduling. Implemented ordinary sessions use a 30-minute minimum and half-hour increments; the customer enters one total guest count including non-players, with eight total people per Regular bay or ten per VIP. Staff checks the six-player-per-bay limit because a total count does not identify players. Cancellation requests require one hour of notice and staff approval, with no cancellation fee.
- Detailed permissions, status transitions/completion criteria, reporting inclusion, notification provider/channel, and short-notice reminder behavior.
- POS exchange contract and test arrangements with the participating team.

The [aligned diagram package](diagrams/README.md) provides system architecture, system flowchart, context/Level 1 references, a combined Level 2 DFD with detail sheets, and POS integration flow. The final POS exchange contract remains to be agreed. These October exports are historical target designs; use [CURRENT-SYSTEM.md](CURRENT-SYSTEM.md) for the current flow and remaining implementation gaps.

Owner update (2026-11-03): ordinary sessions may be 30 minutes. Each time tile represents a half-hour; dragging consecutive tiles sets the complete duration (two tiles = one hour). Existing fixed coaching classes and whole-shop rental windows retain their defined durations. Review deadlines and minimum booking lead times are unchanged.
