# Customer consolidation: UX and data design

Date: 2026-11-11. Status: ready for owner review; no merging is implemented or applied by this document.

## Approved boundary

The owner confirmed: merge staff-facing profiles and history only. Do not merge logins, change `CustomerAccountId`/`AccountId`, grant customer access to another account's bookings, modify guest-access tokens, or rewrite original reservation contacts. Use the existing .NET / MVC / MongoDB tiers and local assets. Staff, Manager, and Admin may consolidate customer records within the staff workspace. These controls never appear in public customer pages.

## Why the screenshot contains duplicates

`HoleInHub.Data/Reservations/CustomerQueries.cs` currently derives directory groups from account IDs, verified guest email, or a single booking. Those are access/booking sources, not a universal person ID. A walk-in without email, a guest booking, and an account reservation can therefore represent the same person in separate rows. Conversely, the screenshot also shows different names sharing a phone number. Grouping everyone by name, phone, or email would create false merges.

Introduce an internal customer-profile identity for the staff directory. Keep account ownership and the contact snapshot recorded on every booking independent from that identity. Similar contact details become suggestions for staff review, never automatic merges.

## Primary journey

1. **Customer detail:** add a secondary `Merge customer` action beside the customer name. Keep it outside the reservation action menu. The menu also exposes `Merge history` after the first consolidation.
2. **Choose another record:** open a full page, with a persistent `Back to customer` link. Search existing profiles by name, phone, email, or booking reference. Show name, record source, contact details, booking count, and most recent visit. Hide the current profile and aliases already consolidated into it. A match may say `Same phone`; never present a made-up confidence percentage.
3. **Compare:** display two aligned columns on desktop, stacked with persistent labels on mobile. Each shows the profile name, contact variants, sources, reservation count, date range, and a link to inspect bookings without losing the comparison. Clearly identify the profile that will remain in the directory. Default to the profile staff started from; changing that choice updates the preview. Do not pick a winner based only on newest data.
4. **Choose display details:** use radio choices for the preferred name, phone, and email from the two profiles; also allow `Keep current` when a field is empty. Retain every original variant for search and context. This choice changes the staff profile, never a booking or account. New spelling corrections remain an ordinary profile-edit task, not a requirement for merging.
5. **Review bookings:** show all included bookings and a unique total. The normal path includes both profiles in full. `Choose bookings` lets staff select specific reservations if a shared-email group contains different people. In that case the source profile remains for its excluded bookings. Keep this decision visible in the final summary.
6. **Confirm:** the reusable reason-confirmation modal shows `Merge customer records?`, both names, the resulting preferred contact, and `N reservations will appear under X`. Explain once: `This combines staff records only. Customer account access and original booking details stay the same.` Require a reason. Actions: tertiary `Cancel`, primary `Merge records` with right arrow. No write occurs until confirmation.
7. **Result:** show the combined profile with a success message, one directory row for a full merge, unique booking totals, and a `Contact details` panel with distinct email addresses and phone numbers, with primary details identified. Repeated email/phone values appear once; original name/contact snapshots remain in booking and audit history. Old staff customer links resolve to the combined profile. Cancellation returns to the comparison with selections intact.
8. **Correction:** `Merge history` shows who merged which records, when (Manila display / UTC storage), why, and which reservations moved. Staff can request `Undo merge`; confirm with a reason. Undo is permitted only when membership/contact revisions still match the operation's result. If a later merge or reassignment depends on it, show the exact conflict and require reviewing/reversing the later operation first. Never silently restore an old snapshot over later work.

## States and edge cases

| Case | Required behavior |
| --- | --- |
| Same person, changed phone or spelling | Staff can merge; preferred details are explicit and old variants remain searchable. |
| Same name, different people | Show contact and booking evidence; never merge automatically. Cancel remains available throughout. |
| Family/business email or shared/recycled phone | Warn that matching contact information is not identity proof. Let staff inspect and select individual bookings instead of combining the whole group. |
| Placeholder/missing contact, e.g. repeated digits | Treat it as weak evidence, not a match key. Do not invent missing details. |
| Two customer accounts for one person | Combined staff history is allowed; each login still sees only its originally owned bookings. Passwords and verification state do not move. |
| Guest + account + walk-in | These sources may share one staff profile after review; guest and account access paths remain unchanged. |
| Cancelled, expired, past or upcoming bookings | Preserve statuses, capacity, payments, resource allocations, and their original history. Include them in staff history with existing filters. |
| Repeated click, timeout, retry | One operation ID; return the original result when it committed. Preserve the comparison if nothing committed. Never report a guessed success. |
| Two employees merge the same source, or A→B races B→A | Transactional guards + expected revisions allow one valid operation; reject the other for review. No cycles or duplicate history. |
| Booking appears after comparison | Recheck membership revision and counts before saving; ask staff to review the changed set. Do not silently include unseen bookings. |
| Source already merged | Resolve its canonical profile; show `Already combined` if both selections now resolve to the same profile. |
| Access revoked while the page is open | Reauthorize the POST and return the existing staff sign-in/forbidden result. |
| Database unavailable | Keep selections and show a retryable error. No partially merged profiles. |
| Undo after later changes | Reject the stale undo and identify the dependent operation. Do not guess which new bookings belong back in the source. |

## Storage and read behavior

- `customer_profiles`: stable ID, preferred display contacts, retained contact variants, display revision, membership revision, creation/update UTC times, and optional canonical destination for fully merged profiles. Profile fields do not authorize customer access.
- `customer_profile_bookings`: one membership per reservation (`ReservationId` unique), profile ID, association revision, and last operation ID. This is staff-directory metadata; reservation documents remain authoritative for ownership, contact snapshots, status, pricing and capacity.
- `customer_profile_operations`: unique operation/idempotency ID, merge/undo kind, actor, reason, UTC time, source/target IDs, scope (all or selected), expected/result revisions, moved-membership references, and preferred-contact before/after values. Bound operation size before writing; reject an oversized interactive operation before any writes, with a useful explanation.
- Maintain explicit indexes for profile history, unique reservation membership, canonical lookup, and operation IDs. Do not make phone or email globally unique.
- Begin with the current grouping rules for backfill, so deployment does not silently combine people. Use deterministic group IDs and idempotent membership inserts. Preserve compatibility with existing booking-ID customer URLs by resolving their membership before rendering.
- New reservations attach to the same stable account profile where an account ID already provides the link. For guest/walk-in creation, offer staff an explicit existing-profile selection; shared contact data alone does not choose a profile. If no safe selection exists, create a separate record that can be reviewed later.
- Read all variants for customer search; render one canonical row and deduplicate reservation IDs before counting/paging. Keep keyset/order rules deterministic. A full merge redirects source links; a selected-booking merge leaves the remaining source intact.
- Merging does not change dashboard booking totals: profiles combine, reservations do not multiply or disappear.

## Transaction boundary

Business validates staff authority, distinct canonical profiles, selected reservation membership, contact choices, reason length and operation ID. Data acquires the seeded persisted customer-membership guard, rechecks revisions and membership, and writes membership changes, preferred contacts, redirects and the audit operation in one MongoDB replica-set transaction. Undo uses the same guard and validates semantic result hashes plus operation lineage before its inverse write. Revision counters remain monotonic; reversing a dependent merge can restore eligibility for its predecessor. Retried operation IDs return the existing receipt. Large operations fail preflight rather than partially committing.

## Delivery order

1. Add additive collections/indexes and backfill without altering existing reservations or public ownership queries.
2. Switch staff directory/profile/history reads to stable memberships; add existing-profile selection for staff-created bookings and preserve legacy links.
3. Ship the comparison, preview and reason-confirmation flow with transactional merge and conflict responses.
4. Ship merge history and guarded undo together with merging, not as a later safety promise.

No automatic consolidation, bulk merge, account transfer, or fuzzy identity engine in the first release. Implement only after this design is reviewed. Migrations run through the explicit Migrator CLI; never on Web startup. Verify on isolated local replica-set test databases before applying to the intended environment.


## Implementation record — 11 November 2026

The owner approved staff-only implementation. The full/selected merge, preferred contacts, retained variants, canonical history, new-booking association, audit and guarded undo are implemented. Profile writes share one persisted metadata guard at current venue volume; split it only if contention is measured. Staff choose an existing profile through its New booking action, reusing the directory rather than adding another autocomplete. Shared contacts never choose a profile for new guest/walk-in bookings.

Local verification: clean build, 80 JavaScript checks, 187 non-integration .NET tests and 179 isolated integration tests passed. Browser verification covered comparison, selection/zero-selection, reason confirmation/cancel, actual merge and audited undo using isolated test records. Native saved-password autofill and the broad outage/expired-session interaction matrix were not exercised in the browser. Owner-authorized development migration 018 and preview activation completed on 11 November 2026 after verified backup; no shared customer records have been merged.


Customer detail refinement (11 November 2026): removed the combined-history explanation from the detail page. Reservation history and Contact details use a 70:30 desktop grid and stack on narrow screens. Distinct display methods normalize email casing/whitespace and common local/international Philippine phone formats without modifying saved contact variants. The proposed row-to-row merge interaction is documented separately in `Documentation/plans/2026-11-11-customer-row-merge-ux.md`; it is not implemented.
