# POS settlement and reservation checkout

## Rollout and ownership

By default staff check-out records departure, releases bays and completes attendance in one transaction. The reservation-side split is implemented behind ReservationPolicy__RequirePosSettlement, which defaults to false. Apply migration 023 through the Migrator before enabling the switch in an isolated acceptance environment. Whole-shop deposit verification is a separate booking-approval concern. The proposed workflow separates physical departure from financial completion. The venue must approve releasing the bays on departure while a balance is outstanding.

| Action | Reservation outcome | Bay availability | Financial evidence |
| --- | --- | --- | --- |
| Staff check in guest | Checked-In | Occupied | Existing deposit rules apply |
| Staff confirm the guest has vacated all booked bays | Awaiting settlement, actual departure recorded | Released at departure | May still be unpaid or unknown |
| POS verifies the entire reservation balance is settled | Eligible for staff checkout once departure is recorded | Released | Persist authoritative POS bill reference, status and confirmation time |
| Authorized staff complete checkout | Completed, completion time and actor recorded | Released | Settlement covers all linked bills |

A settlement arriving before departure may be recorded. It must never release a bay or complete the visit. A deposit, receipt upload, partial payment or browser-supplied payment claim cannot satisfy the final settlement rule. For split bills POS determines when the entire reservation balance is settled. POS owns amounts, collection, discounts, refunds and financial receipts. The reservation confirmation identifies the booking.

## Reservation-side implementation design

Use the existing public reservation_id to correlate records. Keep the internal MongoDB key and existing access checks. Store departure, final checkout time and trusted settlement evidence independently. Store amounts only if the signed-off POS contract requires them. A POS outage leaves the visit awaiting settlement and offers staff a retry of verification. Repeating a successful verification or checkout returns the existing result. An unknown reservation or mismatched bill cannot update a visit.

Reuse the attendance transaction, revision checks, command IDs and history for staff actions. The checkout decision must evaluate authoritative settlement and attendance state in the transaction. Authenticate POS independently of staff sessions. Agree on message identity, ordering and reversal handling before allowing a POS update to replace newer evidence. Keep secrets out of source control and audit records.

Staff booking details should show actual departure, settlement status, POS bill reference and checkout availability. Attendance and usage reports use actual departure for time on site. Financial completion reports use checkout completion time. All bays in a multi-bay booking release together on departure. Historical completed records retain their original meaning without inventing payment evidence.

Inventory and Online Ordering require no direct Reservation changes for this rule. POS is responsible for including any relevant sales in its final settlement decision.

With the switch enabled, End session records CheckedOutUtc and the Awaiting settlement state. Complete checkout requires FinalSettlement with the same reservation ID, a bill reference and a valid verification timestamp. It records CheckoutCompletedUtc separately and preserves departure. No public or staff endpoint accepts settlement claims. The trusted POS writer is the next integration deliverable. Keep the switch off for operational use until that writer and reconciliation are available. Turning the switch off does not implicitly complete visits already awaiting settlement.
