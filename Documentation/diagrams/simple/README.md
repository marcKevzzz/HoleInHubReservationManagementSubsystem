# Simple diagrams

This version uses short labels, a clear top-to-bottom direction and separate pages for separate tasks. The earlier detailed package is retained as a technical reference.

## Open in draw.io

1. Download or locate [reservation-diagrams.drawio](reservation-diagrams.drawio).
2. In draw.io / diagrams.net, choose **File → Open From → Device** and select that file. You can also drag the file onto the editor.
3. Use the page tabs at the bottom to choose a diagram. All boxes, labels and arrows are editable objects, not a flattened image.

To add only one diagram to your existing document, use **File → Import From → Device** and select one of the individual `.drawio` files below. Open the `.drawio` file rather than a PNG or PDF to edit shapes.

## Pages

| Page | Editable diagram |
| --- | --- |
| 1 | [System architecture](01-architecture.drawio) |
| 2 | [Flowchart 1: Booking and approval](02-booking.drawio) |
| 3 | [Flowchart 2: Arrival and no-show](03-attendance.drawio) |
| 4 | [Flowchart 3: Booking changes](04-changes.drawio) |
| 5 | [DFD reading map](05-dfd-reading-map.drawio) |
| 6 | [Level 2 DFD: Accounts/access](06-dfd-level2-1.drawio) |
| 7 | [Level 2 DFD: Reservations](06-dfd-level2-2.drawio) |
| 8 | [Level 2 DFD: Notifications](06-dfd-level2-3.drawio) |
| 9 | [Level 2 DFD: Reports](06-dfd-level2-4.drawio) |
| 10 | [Level 2 DFD: POS/deposits](06-dfd-level2-5.drawio) |
| 11 | [Level 2 DFD: Basic inquiries](06-dfd-level2-6.drawio) |
| 12 | [POS integration flow](07-pos-integration.drawio) |

[View all pages as PDF](reservation-diagrams.pdf). Matching PNG previews and SVG vector exports are also in this folder.

## How to read them

- Flowcharts: follow the arrows from Start; diamonds ask a question and labelled branches show outcomes.
- DFDs: arrows name the information being exchanged. Their layout is not a sequence of decisions. Green numbered boxes process information; blue rectangles are people/external systems; white D-labelled boxes are stored records.
- The six Level 2 pages retain process IDs 1.1–6.2. Repeated IDs refer to the same records or process. For example, 2.3 on the change row refers back to the allocation check above.
- The reading map is only a guide to the six areas; it is not a substitute for a Level 1 or Level 2 DFD.

## Rules behind the short labels

- Customers browse without signing in. Online booking requires verified email; staff-assisted booking requires an authorized staff account but no customer account.
- Availability includes operating hours, closures, maintenance and competing bookings. Check and save one Pending booking and every requested allocation together; a conflicting save creates no partial booking. Retried submissions return the original result.
- Ordinary online Pending requests receive staff alerts at 15/25 minutes and expire at the proposed 30-minute review deadline. Expiry releases all allocations and notifies customer/staff. Review coverage and outside-hours clock handling require client agreement. Approval and expiry must safely check current booking state.
- Whole-shop bookings require deposit verification before staff confirmation. Their separately agreed deposit deadline is not the ordinary online timer; a missing/failed POS result never proves payment. Staff can still review or cancel an unverified request; they cannot confirm it without verified deposit evidence.
- Confirmed unattended bookings are flagged at the earlier of start plus one hour or scheduled end. Staff follow up and record attendance/No-show. No-show is not decided automatically; late arrival does not automatically extend a booking.
- Change rejection/failure preserves the original reservation and allocations. Approved replacement allocations commit together; affected notices and relevant POS details are updated.
- Notifications follow committed outcomes. Approval email follows staff confirmation, reminders are about 15 minutes before start, and manual staff contact stays separate. Duplicate/stale deliveries are checked.
- POS owns billing, payments, receipts, vouchers and settlement. Reservation stores references, statuses and verification outcomes. A repeated POS result preserves the recorded outcome without applying it twice. A payment response does not automatically complete a reservation.
- Reports cover counts, services, peak periods, cancellations/no-shows, customer trends and reserved-time utilization. They do not measure physical occupancy.

These pages show the main flows using plain language. The [detailed package](../README.md) retains the complete technical reference, context/parent diagrams and process notes. Client review coverage, outside-hours handling, deposit terms/deadline and exact completion/exception rules remain open. No application behavior or original source image was changed.
