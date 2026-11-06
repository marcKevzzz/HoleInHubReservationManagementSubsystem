# IPT102 project timeline

Source: [Group 2 project timeline, page 1](sources/GROUP2_PROJECT-TIMELINE-IPT102.pdf). The dates below are the week markers printed above each sprint in the source. The PDF does not give separate start dates or task estimates.

| Sprint | Week marker | Theme | Planned scope |
| --- | --- | --- | --- |
| 1 | Week 13, 5 November 2026 | Customer access and booking | Registration and authentication, guest and account booking, service and availability pages, My Reservations, atomic bay allocation, ownership and verification email |
| 2 | Week 15, 19 November 2026 | Staff operation and scheduling | Staff dashboard, pending requests, calendar and resource timeline, reservation details, change requests, customers, resources, and users and roles |
| 3 | Week 17, 3 December 2026 | Reports and evaluation | Reports and utilization, notification status, email and SMS, basic chatbot, profile and POS simulation, evaluation, deployment, and backup/restore verification |

Sprint 2 acceptance covers role-based access, staff approval and deposit verification, customer change requests, safe rescheduling/reassignment/extension, attendance and cancellation, stale-edit protection, audit history, approval confirmations, and lifecycle and failed-rescheduling tests. See the [Sprint 2 implementation plan](plans/2026-11-06-sprint-2-staff-operations.md) for sequencing, acceptance evidence, and open decisions.

Use [PROJECT.md](PROJECT.md) for current booking rules and [CURRENT-SYSTEM.md](CURRENT-SYSTEM.md) for implementation status. The PDF describes Pending holds without automatic expiry. The newer owner-approved policy sets expiry for ordinary online requests and a separate deadline for whole-shop deposits. The PDF schedules profile and POS simulation later, while the repository already includes a customer profile and limited Development/Demo deposit simulation. Sprint 2 may reuse the simulation to demonstrate deposit verification. The agreed POS exchange and later-milestone evaluation remain separate.
