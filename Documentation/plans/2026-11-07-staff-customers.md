# Staff Customers implementation plan

**Goal** — Staff and Admin can find reservation contacts and inspect their booking history through the existing staff workspace.

**Architecture** — Reservation-derived, read-only customer directory. Web handles authorization, forms and rendering. Business defines search normalization and customer DTOs. Data performs bounded MongoDB queries and identity grouping. Existing booking review remains the action destination.

**Stack** — ASP.NET Core MVC, MongoDB aggregation, Tailwind v4 semantic tokens and the existing staff layout.

## Experience and scope

Use the existing white, black and green workspace with the homepage typography. The Customers landing page has a title, one search form and compact contact rows. Each row shows the saved name, contact details, identity type and matching reservation reference. Profiles group contact information separately from visit history and link to protected booking review.

Search accepts names, email, phone or an exact reservation reference. Query results and histories use 25-item pages, deterministic ordering, a query timeout and small projections. Show explicit empty, invalid-search and pagination states. Reflow rows on mobile with visible keyboard focus and 44-pixel controls.

Identity rules are conservative. Customer account IDs group account-owned records, with the existing legacy AccountId fallback. Verified guest records group by case-insensitive verified email. Staff-assisted and unverified legacy contacts remain individual booking contacts. Shared names, phone numbers or email addresses never merge accounts, guest records and walk-ins. Contact values retain their recorded spelling. Profile headers use the latest linked reservation contact.

This first page covers customers with reservations. Account provisioning and contact editing belong to their existing or future dedicated workflows.

## Tasks

- [x] Add narrow Data directory and profile queries, including identity boundaries, escaped searches, pagination, timeouts and reservation-only projections.
- [x] Add Business DTOs, validation and readable identity labels. Reuse ContactRules for phone normalization.
- [x] Add Staff/Admin Customers controller and routes, sidebar navigation, responsive directory and profile views with existing booking links.
- [x] Add MongoDB regression coverage for identity separation, older records, search, paging and profile counts. Add HTTP authorization coverage and inspect desktop/mobile layouts.
- [x] Build the solution and CSS, run appropriate tests, update the implementation inventory and report actual results.

## Acceptance

Staff can search a known name, email, formatted phone or reference and open the matching contact. Profiles distinguish upcoming reservations, recorded completed visits, cancellations and other booking states. Account and guest reservations with the same email remain separate. A shared walk-in phone never silently creates a combined profile. Customers and anonymous users cannot read either staff route.

Manual no-show actions and attendance corrections remain deferred.

## Verification evidence

Verified on 7 November 2026 using separate build outputs to preserve the configured running app.

- Solution build succeeded with zero warnings and errors. CSS build succeeded.
- 115 unit tests and 60 JavaScript tests passed.
- All 96 Data/Business integration tests passed against isolated local MongoDB databases. The three new customer query cases cover identity separation, legacy account records, literal and phone searches, paging and profile totals.
- Four new HTTP cases passed for Staff, Admin, Customer and anonymous access. They check protected directory/profile routes and no-store responses.
- Desktop and 360-pixel browser fixture checks covered directory rows, long email values, customer profile, keyboard search submission and the empty state. The narrow label column and mobile row grouping were corrected.
- The broader web runs reported failures outside the new Customers cases, including account identity, SMTP receipt and public-page smoke checks. The full web suite is not green.
- Query performance at large customer volumes has not been benchmarked. Queries currently have a five-second MongoDB budget.
