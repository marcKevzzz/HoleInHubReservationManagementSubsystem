# Hole In Hub Golf & Lounge Reservation Management Subsystem

# CHAPTER I

## PROJECT BACKGROUND

> Aligned final-paper manuscript prepared from the supplied chapter, proposal, and owner clarifications. Academic approval, reference verification, and institutional formatting remain subject to review. Existing operational descriptions are supplied context, not new field observations.

## Introduction

In a facility where customers may require the same space at different times, reservation management involves coordinating limited resources accurately and efficiently. Hole In Hub Golf & Lounge, located in Quezon City, Philippines, offers indoor golf and leisure services through simulator bays, private spaces, and group or event packages. Resource availability affects customer experience, front-desk workflow, and daily operations.

The proposed Reservation Management Subsystem is the component of the broader Hole In Hub Integrated Management System responsible for customer bookings, schedules, resource allocation, and reservation records. It accommodates regular reservations, promotional services, packages, and group bookings involving multiple bays. Other operational components, including inventory, ordering, and point-of-sale processing, remain outside this subsystem's research implementation.

Managing several resources requires a consistent process that maintains accurate availability while users submit requests or change schedules. Manual allocation, repeated customer information entry, and disconnected records can increase operational effort and the possibility of conflicting bookings. A centralized relational system connects customer information, services, schedules, resources, and statuses within one workflow.

This study focuses on designing, developing, and evaluating a Reservation Management Subsystem with calendar and Gantt-style scheduling, unified multi-bay booking, overlap prevention, centralized customer records, staff approval, and status tracking. Reports, notifications, and a rule-based inquiry chatbot support reservation operations. A separately labelled POS and voucher demonstration illustrates the wider customer journey without extending research scope to payment or voucher processing.

## Project Background and Context

The supplied context describes a local browser-based reservation interface supported by manual procedures. While sufficient for basic bookings, limitations become apparent when handling group allocations, customer records, simultaneous requests, and changes in availability.

### Fragmented multi-bay allocation

The described workflow requires staff to reserve individual bays and merge records sequentially for group bookings. This adds work and can produce inconsistent allocations. The proposed subsystem creates one reservation with all selected resources. Bays need not be adjacent if they are available for the requested interval.

### Disconnected customer records

Customer information is maintained separately from portions of the booking workflow, requiring staff to cross-reference records. Repeated entry can create duplicate profiles and delay reservation or check-in procedures. The proposed subsystem links centralized customer records directly to reservation histories. Customer records are distinct from authentication accounts.

### Concurrent requests and overlap risks

Without consistent allocation protection, simultaneous submissions or rapid schedule changes can reserve the same resource for overlapping intervals. The proposed subsystem applies server-side validation and database-appropriate concurrency protection. Multi-resource reservations succeed only when all required allocations can be committed together.

### Interface responsiveness

The supplied context reports startup delays associated with local file initialization and external resource loading. The proposed system will be evaluated for startup and schedule responsiveness under documented test conditions. Those reported delays have not been independently measured or shown to be resolved in the current repository.

### Approval and attendance workflow

Successful submission creates a Pending reservation that holds its resources. Staff manually confirm or cancel requests; no automated Pending expiration is specified. Whole-shop reservations block every facility resource and require deposit verification before confirmation. Actual collection remains external to the research implementation.

Operating hours begin at 11:00 AM on Monday and 10:00 AM Tuesday through Sunday, ending at midnight. Sessions normally last 60 minutes, with certain services or promotions allowing 30 minutes. No designated turnaround buffer or daily customer booking limit has been supplied.

Staff contact customers 15 minutes before the scheduled start. The described grace period is 60 minutes. Late arrival does not automatically extend the original end time; reassignment, shifts, and extensions must respect resource availability. Staff record missed attendance as No-Show and intentional withdrawal as Cancelled. Handling grace periods that consume an entire session requires final policy clarification.

## Project Purpose and Description

### General Objective

To design, develop, and evaluate a Reservation Management Subsystem for Hole In Hub Golf & Lounge that organizes customer bookings, monitors resource availability, supports multi-bay allocation, prevents overlapping reservations, maintains customer and booking records, and provides an efficient staff-controlled reservation workflow.

### Specific Objectives

1. Develop interactive calendar and Gantt-style views showing available dates, time intervals, services, and resources.
2. Implement single- and multi-bay allocation under one reservation, without requiring adjacent bays.
3. Prevent overlapping allocations through server validation and database-appropriate transactional concurrency protection.
4. Provide role-based access for customer accounts, front-desk staff, supervisors, and administrators, including customer ownership restrictions.
5. Establish centralized customer records linked to reservation histories, organizer information, and attendee counts.
6. Provide controlled Pending, Confirmed, Checked-In, Completed, Cancelled, and No-Show status management, including staff approval and whole-shop deposit verification.
7. Design relational data connecting customers, accounts, services, reservations, resources, allocations, and history while reducing unnecessary duplication.
8. Generate reservation references and demonstrate an external POS handoff without implementing production billing, payments, or voucher processing as research deliverables.
9. Provide reports on volume, services, reserved-time utilization, peak booking periods, and customer trends.
10. Provide confirmations and automated 24-hour reminders, subject to valid contact details, connectivity, provider availability, and an approved short-notice reminder policy.
11. Provide predefined chatbot responses about hours, rates, policies, and packages without independent booking approval.
12. Evaluate reservation correctness, concurrent allocation, access control, usability, and responsiveness under documented conditions.

## Scope and Delimitations of the Project

### Scope

The research covers creation, scheduling, allocation, management, and monitoring of reservations. Functions include:

1. Interactive calendar and Gantt-style resource schedules.
2. Service selection for regular bookings, packages, promotions, and whole-shop reservations.
3. Availability for Bay 1 through Bay 6 and applicable premium/private spaces.
4. Multi-resource allocation committed together under one record.
5. Authorized creation, modification, rescheduling, reassignment, extension, and cancellation.
6. Automatic overlap prevention for resources during all allocation changes.
7. Central customer names/contact details, reservation organizer, and attendee information.
8. Reservation references, summaries, histories, and staff search/filter views.
9. Authorized status management and manual staff approval.
10. Pending capacity holds until staff resolution, without automatic expiration.
11. Whole-shop resource blocking and deposit verification before confirmation.
12. Resource schedules and closure/maintenance blocks.
13. Reserved-time reports with documented status inclusion rules.
14. Confirmations, reminders, and predefined inquiry responses.
15. Customer-facing and operational access for customers, front-desk staff, supervisors, and administrators.

The target environment is a web application accessed on customer devices, front-desk workstations, administrative terminals, and authorized devices. Membership is not required. Guest booking with optional accounts is recommended, subject to final approval.

### External process demonstration

A labelled simulation may show estimated charges, an illustrative voucher, whole-shop deposit verification, and final settlement using a reservation reference. These examples demonstrate the complete customer journey. They do not implement real payment acceptance, voucher eligibility checking, balance management, redemption, or live POS connectivity. Evaluation must distinguish actual reservation functions from simulated external events.

### Delimitations

The research does not implement inventory, merchandise ordering, accounting, payroll, payment collection, sales transactions, receipts, voucher sales/redemption, or simulator hardware control such as launch monitors, cameras, and ball-tracking equipment.

The external POS is the stated authority for pricing, packages, promotions, vouchers, and billing. Reservation interfaces may show estimated or demonstration prices, but do not maintain a payment ledger. Deposit and settlement states remain separate from reservation status. A deposit alone cannot complete a reservation. Production POS integration is outside this research implementation.

Availability reflects recorded reservations and configured schedules, not physically detected occupancy. Utilization is based on reserved time rather than measured play time. The chatbot uses predefined responses and cannot approve reservations or resolve complex concerns. Notifications depend on valid contact details, connectivity, and a configured provider. Manual 15-minute attendance contact remains a staff task.

Deposit amounts, cancellation/refund conditions, maximum duration, start-time increments, attendee capacities, and exceptional transitions require policy confirmation. Without automatic Pending expiration, timely staff review is an operational responsibility.

## Operational Definition of Terms

**Reservation Management Subsystem.** The component managing customer bookings, schedules, allocation, records, and statuses.

**Reservation.** A record identifying customer/organizer, service, scheduled interval, attendees, resources, reference, and status.

**Pending.** A submitted request awaiting staff resolution that actively holds its allocated capacity without automatic expiration.

**Confirmed.** A staff-approved reservation; whole-shop bookings require verified deposit first.

**Checked-In.** A reservation with customer arrival recorded by authorized staff.

**Completed.** A reservation recorded as finished under the approved completion rule; deposit verification alone does not produce this state.

**Cancelled.** An intentionally withdrawn or cancelled reservation that releases remaining capacity.

**No-Show.** A reservation staff record as missed under the attendance/grace-period rule.

**Resource Allocation.** Assignment of an available bay/private space to a reservation interval.

**Simulator Bay.** A reservable indoor golf resource, including Bay 1 through Bay 6; applicable private spaces are separate resources.

**Multi-Bay Reservation.** One booking allocating multiple bays together, without requiring adjacency.

**Whole-Shop Reservation.** An exclusive booking blocking all facility resources and requiring deposit verification before confirmation.

**Double Booking.** Allocation of one resource to different reservations with overlapping intervals.

**Gantt-Style View.** A resource timeline displaying booking intervals and durations.

**Reservation History.** Customer booking records and relevant authorized changes.

**Reserved-Time Utilization.** Booked resource time relative to bookable time under documented status inclusion rules, without occupancy sensing.

**POS Demonstration.** A labelled simulation of external billing/payment events linked by reservation reference, outside production payment implementation and evaluation.

**Rule-Based Inquiry Chatbot.** A predefined-response interface for basic facility and booking inquiries.

## Manuscript provenance and submission review

This manuscript reconciles the supplied PDF and proposal with owner clarifications. Earlier voucher implementation objectives and live POS scope are replaced with an explicit demonstration boundary. Approval sheets, dedications, and acknowledgements are not reproduced because they are not Chapter I content.

Source literature citations have not been independently verified and are not presented as confirmed references here. Before academic submission, add verified supporting literature, check institutional headings/reference style, approve remaining policies, and review with the adviser. No evaluation results or institutional approval are claimed.
