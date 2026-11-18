# Admin booking configuration implementation plan

**Goal.** Let admins set bay capacity, service schedules, and PHP pricing from the staff workspace. Use the saved rules to generate the customer calendar, validate reservations, and calculate quotes.

**Architecture.** Use the existing Resources and Services editors. Web handles forms and presentation, Business applies booking rules, and Data stores versioned configuration in MongoDB. The project’s booking, deposit, and allocation requirements are in [PROJECT.md](../PROJECT.md).

## Admin experience

| Screen | Controls | What admins and customers can verify |
| --- | --- | --- |
| **Resources → Edit bay → Capacity** | Maximum players, maximum total guests, optional maximum non-players | Show an example of how many non-players fit with the current player limit. The lower of the remaining total capacity and the optional non-player limit applies. |
| **Services → Edit service → Availability** | Weekly opening windows, fixed session times and durations, and one-off date overrides | To set a coach’s schedule, select **Tuesday** and **Thursday**, enter the working hours for those days, and preview the next available sessions before saving. |
| **Services → Edit service → Pricing** | Hourly baseline and rate bands, package price and included class count, or whole-shop half-day and whole-day prices | Preview a rate with a sample Manila date, time, duration, and bay count when applicable. Display how the total was calculated. |

Use the existing staff styling, controls, and save feedback. Keep **Details**, **Availability**, and **Pricing** easy to reach from the service editor. Show controls for the selected booking format. Use weekday buttons and native date and time inputs with persistent labels. Put the next available sessions and sample quote near their editors. Place validation next to the affected row and link the error summary to that field. On phones, stack rule fields with their visible labels, keep Add and Remove controls reachable, and avoid horizontal scrolling. Support keyboard navigation and reduced motion.

The plan uses each bookable service as the unit of coach availability. Coaches with different schedules can have separate service entries that share the same coaching booking type. If the venue needs to prevent a coach from taking overlapping bookings across different services, that requires a coach resource and allocation rule. An included class count describes the package and its price. The existing first-session booking flow remains the starting scope for lesson packages.

## Booking behavior

- **Availability.** Store weekly windows and explicit date overrides in Asia/Manila. A date override replaces that day's weekly availability and can mark the service closed or open special hours. A requested interval must fit an allowed window, venue operating hours, and resource availability. For fixed sessions, the start and duration must match an offered session. Shared bay blocks apply to every service using those bays.
- **Capacity.** Seed Regular defaults of 6 players and 8 total guests and VIP defaults of 6 players and 10 total guests. Validate configurations and bookings against each bay's actual record. Apply a separate service-wide participant limit where relevant, such as the lesson program's current cap of 8. For a total-only customer booking, enforce the total at submission and verify player and non-player limits when staff confirm the party breakdown. Where customers already enter a breakdown, enforce all saved limits at submission.
- **Hourly rates.** Set a base PHP rate per bay and optional weekday/time bands. Price the portion of a session in each band, including sessions that cross a boundary. Allow explicit date-based rate overrides for holidays or special dates. Reject overlapping bands at the same priority. A dated rate takes precedence over a weekly band.
- **Packages and whole-shop.** A lesson package has one price and an included class count. Whole-shop pricing has separate half-day and whole-day amounts with optional weekday or date overrides. Start with the existing 3–9 PM and opening-to-midnight options, and keep the existing deposit workflow. Put package names, price units, and resolved slot information in the public catalog and booking form.
- **Existing bookings.** Save the accepted price and rule revision with new reservations. Configuration changes apply to new bookings and proposed changes. Keep existing bookings, quotes already attached to them, and allocations intact. If a change would affect future reservations, show the admin which existing bookings fall outside the new hours or capacity before saving. Existing rescheduling and cancellation workflows handle any follow-up.

## Implementation steps

### 1. Make bay capacity editable and enforce it

**Files.** Update `HoleInHub.Data/Reservations/Records.cs`, resource persistence, `HoleInHub.Business/Reservations/ResourceAdministration.cs`, `HoleInHub.Business/Reservations/Models.cs`, the resource controller and `HoleInHub.Web/Views/Leadership/Resources.cshtml`, and `HoleInHub.Web/wwwroot/js/resource-admin.js` as needed.

- [ ] Add optional persisted limits and use bay type defaults for existing records. Validate positive player and total limits, player and non-player limits within the total, and an optional non-player limit of zero or above.
- [ ] Put fields in the existing add/edit bay panel. Display saved values on reopen. Use the current resource edit concurrency controls, or add a revision check if the update path lacks one.
- [ ] Resolve the selected resources from the catalog for guest validation, staff-assisted bookings, and reassignments. Replace capacity inferred from the bay ID. Keep availability and conflict checks in the existing allocation paths.
- [ ] Verify default values, a changed VIP limit, a custom bay identifier, a service cap below physical capacity, and a move to a smaller bay.

### 2. Publish service schedules and connect the booking calendar

**Files.** Extend `HoleInHub.Data/Services/ServiceCatalogPersistence.cs` and related writes, `HoleInHub.Business/Services/ServiceCatalogManagement.cs`, `HoleInHub.Web/ViewModels/ServiceEditorForm.cs`, `HoleInHub.Web/Controllers/ServiceManagementController.cs`, and `HoleInHub.Web/Views/ServiceManagement/Edit.cshtml`. Add schedule evaluation in Business and wire it through `ReservationService.cs`, `StaffBooking.cs`, `ScheduleChanges.cs`, and `StaffBatchMoves.cs` in `HoleInHub.Business/Reservations`.

- [ ] Persist the weekly windows, date overrides, and fixed sessions with the service's existing revision. Build validation for overlapping windows, invalid or duplicate dates, and invalid slot durations. Surface validation on the affected rule.
- [ ] Distinguish the saved service entry ID from its booking type. Read the ID in public catalog links, drafts, booking submissions, and staff forms. Use the existing booking type for allocation and review policy. Resolve legacy type-only links to their canonical service, and keep valid drafts and recorded bookings readable.
- [ ] Replace hard-coded KPGA weekday and fixed-slot rules with seeded defaults matching the current behavior. For an admin to switch KPGA pro coaching to Tuesday and Thursday, unselect Saturday and save. Provide a date preview that shows why a date has eligible sessions or is unavailable.
- [ ] Return eligible dates and sessions to `ReservationsController.cs` and `HoleInHub.Web/wwwroot/js/reservation.js`. Disabled dates and displayed sessions should use those rules. Validate the interval again on preview and submission, after guest verification, and on staff creation or moves. Check the target and selected bays when approving a proposed schedule change.
- [ ] Verify Tuesday and Thursday bookings, Saturday rejection, a special Saturday opening, a dated Thursday closure, a general bay block, and two services sharing the same coaching type with independent calendars.

### 3. Add configured prices and connect quotes

**Files.** Extend the same service editor and service persistence. Put quote calculation in `HoleInHub.Business/Reservations` and connect `ReservationService.cs`, `ReservationsController.cs`, `reservation.js`, the `Views/Reservations` booking and receipt views, and `Views/Home/_ServiceCatalog.cshtml`.

- [ ] Seed explicit structured rates from the documented catalog and current whole-shop demo calculator. Preserve current published values. Validate amounts, time bands, date precedence, and package class counts. Keep PHP decimal money values and define rounding once at the final amount to two decimal places.
- [ ] Show an admin preview and a customer quote from the same Business calculation. Generate the public rate summary from configured prices and retain the separate offer terms field for explanatory copy.
- [ ] Resolve price, deposit, and rule revision at submission. If they changed since the displayed quote, show the updated quote for review before committing. Preserve idempotent retries and the payment workflow. Store quoted amounts with the reservation and reuse them in receipts and existing checkout.
- [ ] Verify a Regular booking across 3 PM with two bays, a dated holiday override, a package with four classes, all four whole-shop price combinations, and a price edit while a customer has the form open.

### 4. Verify rollout and the admin workflow

- [ ] Read the existing MongoDB validators and migration history. Use additive fields and defaults for older records. If new validators, indexes, or backfill are required, use a new explicit Migrator step. Run integration tests against isolated local MongoDB.
- [ ] Confirm that an Admin can save each editor and other roles are denied writes. Exercise stale edits, overlapping windows or rate bands, and a capacity change with an existing future reservation.
- [ ] Walk through setting Tuesday and Thursday for a coach, saving, opening the public form in English and Korean, and confirming that Tuesday and Thursday can be selected and Saturday is unavailable. Check phone and desktop layouts and keyboard entry.
- [ ] Run the solution build, .NET unit and web tests, MongoDB integration tests, Tailwind build, and UI tests. Extend the existing ServiceEditor, ServicesCatalog, BookingProcess, ScheduleChange, and reservation calendar tests for the cases above.
