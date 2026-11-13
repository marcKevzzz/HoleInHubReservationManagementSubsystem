# Operational dashboard implementation plan

Date: 13 November 2026
Status: Implemented on 13 November 2026; local database integration verification remains pending.
Owner request: Plan a functional, interactive dashboard with appropriate graphs, differentiated colors, and equal attention to UI and UX.

## 1. Direction and scope

Replace the navigation-link dashboard with an operational overview: what needs attention, what happens next, and how booking demand is distributed. Preserve the current staff shell, Oswald headings, Inter body text, green primary actions, quiet surfaces, and thin dividers. Avoid adding introductory paragraphs and decorative metric cards; the Resources feedback favors a concise workspace.

This owner-requested plan brings dashboard analysis into scope despite the older roadmap placing utilization/reporting in Sprint 3. It does not authorize live POS collection, a billing ledger, automatic no-show decisions, or a new booking policy.

Roles:
- Staff: selected-day operations, actionable queue summaries, upcoming arrivals, and selected-day demand/service charts. Use existing workspace authorization.
- Manager and Admin: the same operational overview plus a Trends view with historical ranges and comparative analysis. Enforce the existing venue-management permission on the server, including JSON and drill-down endpoints.
- Admin-only administration remains in the sidebar. Do not turn dashboard navigation into a duplicate administration menu.

## 2. Layout and hierarchy

Today view, in reading and keyboard order:
1. Dashboard heading; day picker, Today shortcut, Refresh, and Open schedule action. Manila date/time and last-updated time appear as compact context.
2. Needs attention: actionable counts for Pending reviews, Overdue, Changes, Cancellations, and Deposit checks. Each links to its existing queue. Show the most urgent items first. These counts are a clearly labeled live, all-dates section and do not change with the selected session date.
3. A compact selected-day summary strip: scheduled reservations, arrivals still due, checked-in sessions, and completed sessions. Labels define counts; no percentage changes against an arbitrary comparison period.
4. Main column: hourly scheduled bay-hours chart. Secondary column: next arrivals list, showing time, reference, customer display name, service, bays, and current status. Limit initial list to six with View all; avoid contact information here.
5. Service usage chart below, followed by a compact current bay-state summary when Today is selected. Historical days do not show a misleading live resource state.

Trends view, for Manager/Admin:
1. Date range controls: Last 7 days, Last 30 days, and Custom (maximum 90 days per request). Full Manila dates are visible.
2. Daily scheduled reservations chart across the full width.
3. Service usage and hourly scheduled bay-hours below in two columns on wide screens, stacked on mobile.
4. One context panel for the selected chart slice: exact totals and a paginated matching reservation list.

Use open sections and shared dividers. Do not wrap every count, chart, and label in a separate card. On mobile, retain attention -> summary -> arrivals -> charts order, shorten tick density, and use horizontal service bars. Avoid page-level horizontal scrolling.

## 3. Graphs and their interactions

| Chart | Form and reason | Default measure | Interaction |
| --- | --- | --- | --- |
| Daily scheduled reservations | Stacked vertical bars; discrete daily totals and composition are easier to compare than a smoothed curve | One reservation per Manila start date, grouped into booked, awaiting decision, and other outcomes | Hover/tap shows exact status breakdown; select a date/segment to populate the matching list; legend toggles series |
| Service usage | Sorted horizontal bars; labels remain readable and comparisons share a zero baseline | Distinct booked reservations by stored service ID; show Whole shop separately from Regular/VIP/coaching | Select a service to filter the context list; optional Counts / Scheduled bay-hours toggle changes the unit and axis explicitly |
| Hourly scheduled demand | Vertical bars with a zero baseline; totals represent allocated resource-time rather than the number of booking starts | Scheduled bay-hours overlapping each Manila hourly bucket, for booked reservations | Select an hour to inspect overlapping bookings; distinguish Regular/VIP bay classes when available; show per-date totals rather than averaging silently |

Use three chart families/variants at most in the initial release. No pie/donut, dual-axis combination, 3D effects, decorative gauges, or extra heatmap initially. A weekday/hour heatmap can be added later only if a demonstrated planning question remains unanswered by the hourly chart.

Every chart includes a concise summary, visible units, a nearby legend when multiple series are present, and a Show data table control. Tables provide keyboard-operable equivalents to mark selection and legend actions. Canvas hover alone is not an accessible interaction.

Selection remains visible until cleared. A contextual View matching reservations action opens a shared modal/drawer with the matching paginated list, not an automatic navigation on first tap. Links from that list reuse protected booking details and preserve the dashboard URL/filter context for return navigation. Chart selection must never approve, cancel, retire, or mark attendance.

## 4. Exact metric definitions

Use one TimeProvider instant per response, Asia/Manila day boundaries converted to UTC, and half-open intervals [start, end).

- Scheduled reservations: distinct reservation IDs overlapping the selected day; statuses Pending, Confirmed, Checked-In, or Completed. Expose the status breakdown. Cancelled, Rejected, Expired, and No-show are separate outcomes and not included in this summary total.
- Arrivals still due: selected-day Confirmed sessions not checked in, not ended, and not under a cancellation request. Sort by start. Flag a follow-up only according to existing AttendanceRules; never infer or automatically assign No-show.
- Checked in / completed: distinct selected-day overlapping sessions with the respective stored status. Checked-In is a recorded staff state, not sensed occupancy.
- Needs attention: reuse the queue selection predicates and distinct reservation IDs. Counts overlap between categories; do not sum category counts into a purported unique total. The current PendingReviewCountAsync does not cover every attention category.
- Booked, in chart terminology: Confirmed + Checked-In + Completed. Awaiting decision: Pending, with active-hold versus overdue/released detail. Other outcomes: Cancelled, Rejected, Expired, and No-show, with exact status labels in the breakdown. Unknown legacy statuses remain visible as Other/unknown, never silently discarded.
- Daily trends: bucket by scheduled StartUtc converted to Manila, not CreatedUtc. Label as scheduled sessions. If submission demand is added later, it must have a separate created-date measure.
- Service counts: count a multi-bay reservation once. Do not count an embedded change proposal as a second booking. Use the stored service key and stable fallback names so inactive/deleted catalog entries retain their historical grouping.
- Scheduled bay-hours: for each included booked reservation, sum overlap minutes for each distinct allocated resource ID, then divide by 60. One two-hour reservation on three bays contributes six bay-hours; one whole-shop reservation counts once as a reservation but contributes time for every saved allocation. Use recorded ResourceIds, not today's active-bay count.
- This initial historical measure describes the current record's scheduled duration for booked-status sessions, not reconstructed historical hold duration or actual play time. Early checkout, rescheduling, and later cancellations must not be presented as an immutable as-of booking ledger. Tooltip/help explains the measure; actual attendance duration is a different future metric.
- Pending holds: only a still-effective hold contributes to live held capacity. Overdue Pending can remain a review task after releasing capacity. Reuse the business/data capacity predicates and saved deadlines rather than status-only checks or hardcoded 30-minute rules.
- Current bay state: use the active catalog and current intervals; show Booked, Pending hold, Blocked, Available with explicit state text. Use effective allocation state, not the schedule DTO's status-only HoldsCapacity helper alone. Surface inconsistencies rather than guessing an available state.
- Cancellations and no-shows: expose counts by the scheduled-session cohort in the selected range. Do not show rates until eligible denominators and event-date versus session-date definitions are agreed.

Do not ship historical utilization percentages yet. ResourceRecord stores Active but no activation/retirement interval, and released blocks do not by themselves establish a full historical capacity ledger. Historical percentage denominators would require verified operating-hours, resource-lifecycle and block-effective history. Display scheduled bay-hours, not occupancy or revenue, until those definitions and records exist. Missing denominators display Not available, never 0%.

## 5. Color, accessibility, and interaction rules

Preserve the existing green brand. Define any new chart colors as semantic @theme tokens in Styles/site.css, with corresponding labels and consistent meaning:
- Green: booked/confirmed.
- Amber: Pending review and effective Pending holds.
- Teal: Checked-In / recorded arrivals, consistent with the timeline.
- Muted neutral: Completed and unavailable/blocked, distinguished with text and hatch/dash patterns.
- Existing error color: overdue/action-required and unsuccessful outcomes; use distinct labels and patterns for each outcome rather than several similar reds.
- Service comparisons: categorical green, blue, purple, amber, teal and neutral, assigned by stable service ID. Keep service legend meaning separate from status legend meaning; do not imply a purple service is a lifecycle state.

Choose final token values through rendered contrast checks: 4.5:1 normal text, 3:1 meaningful marks/focus indicators. Color never carries meaning alone. Use direct values, labels, shape/pattern differences, visible keyboard focus, and equivalent tables. Do not claim WCAG compliance before verification.

Reuse the shared date/select controls, buttons, status pills, empty-state partial, toast stack, and modal/drawer behavior. Native dialog must support Escape, focus trapping, accessible names, and focus return. Use a modal only for inspecting a selected slice; normal filters remain inline. No confirmation modal is needed for read-only filtering.

On filter changes, update charts, tables and summary together; store filter state in the URL. Abort obsolete requests, ignore late responses, keep the previous valid view while loading, and show an inline retry if refresh fails. Do not replace failure with empty charts or fabricated zeroes. Empty data and a zero-valued bucket have distinct messages.

Refresh live operational data every 60 seconds while visible, with manual Refresh. Keep historical charts manual/filter-driven. Pause refresh while the document is hidden or a details modal is open. Show last successful refresh time; avoid announcing every update or emitting routine polling toasts. Use shared toasts for explicit action feedback and retain inline failure recovery. No realtime socket infrastructure in the first release.

Keep initial Razor summaries, lists and data tables visible without JavaScript. Enhance them with charts and in-page updates. Respect prefers-reduced-motion and disable chart transitions entirely when requested; otherwise use short interrupted-safe updates with no animated counters or looping decoration.

## 6. Implementation boundaries and files

Preferred chart implementation: Chart.js installed as an exact locked npm dependency and copied/bundled locally, extending the existing npm vendor-build workflow. Use its native bar/tooltip/legend behavior; provide HTML controls and equivalent data tables for keyboard and screen-reader access. No runtime CDN. Final package version is chosen and recorded during implementation, not assumed by this plan.

- Web: update LeadershipController.Dashboard with a typed DashboardQuery/ViewModel and add protected, no-store read-only update/drill-down endpoints. Proposed files: ViewModels/DashboardViewModel.cs, Views/Leadership/Dashboard.cshtml and only genuinely reused partials, Styles/dashboard.css, wwwroot/js/dashboard.js, plus npm package/lock/vendor-copy entries and staff navigation skeleton updates.
- Business: a narrow dashboard query method and DTOs beside the existing reservation services. Business defines status inclusion, time clipping, grouping, permissions-independent metrics, and empty/unknown semantics. It receives one clock instant and range. No MVC/HttpContext types.
- Data: narrow bounded projections/aggregation operations in the reservation persistence tier. Do not calculate analytics from the existing capped review list or repeat an entire day query for every day in a range. No generic repository, alternate database, or background materialization system initially.
- All I/O takes CancellationToken ct and returns Task. Validate ranges, service keys, sort/page size and allowed filter combinations on the server. Read only required fields and paginate drill-downs; avoid exposing email/phone in aggregate responses.
- Include GeneratedAtUtc and an explicit measure/filter description in the response. For live counts and detail, tolerate genuine concurrent changes and show refreshed results; do not claim a transactionally frozen snapshot unless implemented.
- Inspect MongoDB explain plans with representative records before adding indexes. If needed, add a new explicit migration via the CLI and update readiness requirements; never alter an applied migration or run it on Web startup. Historical capacity schema work is a separately defined later task.

## 7. Delivery sequence and verification

1. Lock metric definitions with executable fixtures: empty day, statuses, overdue released holds, multibay and whole-shop allocations, midnight, early checkout, cancellation/change proposals, unknown service/status, and missing resource history.
2. Implement bounded Business/Data read models; prove aggregates reconcile with independently computed expected fixtures. Add role, date-validation, filter and no-PII response tests. Use isolated local MongoDB integration databases only.
3. Build the complete server-rendered dashboard with the current admin components, semantic summaries, exact-value tables, selected-day controls and attention/arrival links.
4. Add the locally bundled chart enhancement, stable color tokens, accessible legend/selection controls, slice inspection, URL persistence, refresh cancellation and failure handling.
5. Verify desktop and mobile together in one inspection pass; fix defects in one batch and confirm once. Test keyboard-only navigation, Escape/focus restoration, touch tooltips, 200% zoom, reduced motion, JavaScript failure, no data, slow/error responses, stale responses, hidden-tab polling and authorization.
6. Run solution build, non-integration tests, JS UI tests, local MongoDB integration suite and Tailwind/vendor build. Preserve preview configuration, check /health/ready after restart, and report actual outputs. Docker/local MongoDB is currently unavailable in this workspace; this remains a known prerequisite for database integration evidence.

Acceptance: counts reconcile with fixtures and matching lists; multibay/whole-shop never inflate reservation counts; overdue released Pending never inflates live capacity; charts have correct labels/units and accessible alternatives; role restrictions hold on every endpoint; filters are shareable and back navigation restores them; no runtime CDN, fabricated revenue, implied sensor occupancy, or unsupported historical utilization claim.

## References

- Business truth: Documentation/PROJECT.md, especially reservation policies, reporting/evaluation, and POS boundary.
- Architecture and verification: Documentation/specs/2026-10-18-production-foundation.md, Documentation/ONBOARDING.md, AGENTS.md.
- Existing precedents: Leadership dashboard/report views, StaffSchedule, StaffReview, AttendanceRules, BookingPersistence.ReviewSelection, shared staff layout/toast/modal components.
- Chart.js accessibility: https://www.chartjs.org/docs/latest/general/accessibility.html (canvas requires an accessible name and meaningful alternatives).
- Chart.js interactions: https://www.chartjs.org/docs/latest/configuration/interactions.html (pointer/touch interactions; keyboard equivalents remain application work).

## Implementation evidence (13 November 2026)

- Implemented typed dashboard queries and bounded projections in the existing Web -> Business -> Data dependency direction; no migration or data writes were introduced.
- Chart.js 4.6.1 is locked through npm and copied locally. Today/Trends, exact-value tables, series toggles, service-unit switching, chart selection, booking-list dialog/pagination, server-rendered fallback, URL state, and cancellable refresh are implemented.
- Reservation ranges are limited to 90 days and 20,000 projected records; oversized ranges fail visibly with a request to shorten the range rather than displaying truncated totals. No large-volume performance claim is made.
- Release solution build passed. Non-integration tests: 160 unit + 112 web smoke passed. JavaScript UI suite: 109 passed. Tailwind/vendor build passed.
- Configured preview readiness returned HTTP 200 Healthy. Browser checks used existing records and verified Today/Trends, chart selection and legend toggles, exact-value table drill-down (two chart-count reservations matched two returned records), modal Escape/focus restoration, and narrow-screen layout with no horizontal page overflow. No reservation mutation was performed.
- Docker and a local replica-set test server are unavailable. Isolated MongoDB integration tests were added but could not run. Full WCAG conformance, historical utilization denominators, and production-scale performance remain unclaimed.

## Owner-directed simplification (13 November 2026)

This refinement supersedes the original multi-chart layout and visible filter toolbar above.

- Consolidated Today, 7/30-day presets, and custom dates into one period menu. Only the active single-day or range fields are shown; server-rendered forms remain usable without JavaScript.
- Reduced the main view to three essential totals and one interactive line chart: booked sessions for ranges, or combined scheduled bay-hours for a day. Removed stacked bar charts, redundant legends, the service chart, and its measure switch.
- Processed service usage into a top-three ranking with counts and shares of booked sessions. Exact service values remain available in a disclosure and linked booking lists.
- Condensed live reviews to nonzero actionable queues. Bay availability is a single available/total readout with optional bay-status details. Limited the arrival preview to three entries, retaining access to the full schedule.
- Removed descriptions, repeated date context, counting essays, empty queue counts, and success toasts for routine filter changes. Kept essential units, accessible chart/table alternatives, loading/error recovery, role controls, and metric definitions unchanged.
- Added tests for aggregate hourly line values and period-field visibility/Escape behavior. Release build passed; 160 unit + 112 smoke tests passed; the JavaScript UI suite passed 111 tests, with all seven focused dashboard tests passing after the final focus-return adjustment.
- Signed-in visual verification completed after session renewal: desktop and narrow-screen hierarchy, quick-period changes, single-day/range field visibility, Escape dismissal, chart selection, matching-booking dialog, and modal focus return checked against existing records. Narrow viewport content width matched viewport width (no page overflow).
- Removed inherited global FAQ border/padding from dashboard disclosures locally; rebuilt Tailwind successfully after this CSS correction.

## Live dashboard refinement (13 November 2026)

- Added four KPI surfaces with semantic background colors, smooth monotone line interpolation, a service-mix pie with labeled counts/shares, and a compact five-event latest-activity table. Removed raw chart/service dropdowns; keyboard-accessible chart links and service legends retain access to booking details.
- Added a role-filtered latest-five activity query across the existing activity sources; Admin-only access events remain excluded for other roles.
- Removed Dashboard and Activity refresh controls. Authorized server-sent events publish invalidation signals after successful HTTP actions. A lazy MongoDB change-stream watcher covers other writers/instances; clients reconcile every 15 seconds to recover missed events or work without change-stream permissions.
- Signals carry no record data; each client reloads its existing authorized view. Changes are coalesced, hidden pages do not update, and open modals/active filters defer both request dispatch and response replacement. Historical Activity pagination retains its pinned snapshot; its newest page updates automatically.
- Release build passed. Non-integration tests: 160 unit + 116 web smoke passed, including SSE authorization and actual signal delivery. Browser checks confirmed four KPIs, five latest rows, smooth line/pie rendering, mobile layout without page overflow, and no Refresh controls.
- Isolated MongoDB integration verification remains pending because local Docker/replica-set tooling is unavailable. No production-scale performance or full accessibility conformance claim is made.

- Final browser verification observed the Activity snapshot advance automatically without any Refresh action. The dashboard rendered exactly four KPI cards and five activity rows; the mobile page had no horizontal overflow. Added and passed a regression test preventing an in-flight automatic response from dismissing an open modal/filter.

- Owner appearance update: renamed Service mix to Service, changed the chart to a 68% cutout donut, and made all four KPI backgrounds white. Replaced the inline dashboard fetch-error/Retry banner with shared warning toasts, deduplicated per background outage; automatic reconciliation continues. Browser verification confirmed white computed backgrounds and toast delivery from a validation failure. Release build, 276 non-integration .NET tests, and 116 JavaScript UI tests passed.

## Dashboard interaction refinement (13 November 2026)

- Added subtle hover/focus transitions to service rows, live review links, and section links. Tightened service rows and added safe backdrop dismissal to the matching-reservations dialog (both pointer press and release must be outside).
- Latest activity uses five linked table rows with an animated right arrow and no activity dot or underline. Dashboard links open the Activity index with its side panel selected through `open`; closing, reloading, and browser history retain the list context.
- Replaced the bay disclosure with an explicit unavailable-only list. The arrivals empty state uses a miniature inbox treatment based on the owner's reference. The donut shows its total centrally and percentages inside slices of at least 10%; all slices retain exact values in the linked legend.
- Comparable KPIs use the adjacent equal-length Manila calendar period, zero baselines display “New,” and decreases in pending work use the favorable color. Comparisons use the latest persisted reservation statuses, not reconstructed historical states. Live arrivals/check-ins show follow-up and attendance context rather than invented historical live counts. Oversized prior periods omit comparisons rather than using truncated data.
- Release build passed with zero warnings/errors; 161 unit tests, 122 web tests, and 119 JavaScript UI tests passed. Configured preview readiness is Healthy. Signed-in browser checks verified service backdrop dismissal/focus return, whole-row navigation from a time cell, Activity panel close/Forward, period changes, donut labels, four KPIs/five rows, and narrow-screen layout without page overflow. Local MongoDB integration tests remain unrun because Docker/local replica-set tooling is unavailable.

- Owner consistency correction: replaced the arrivals inbox with a person-and-clock icon; restored the available/total bay count while keeping the conditional unavailable-bay list. Dashboard toolbar controls now share `--staff-control-size`. Review and section links reuse `staff-hover-link`, including its directional underline and reduced-motion handling. Release/CSS build and all 283 non-integration .NET tests passed; configured readiness is Healthy. Browser inspection confirmed identical desktop control heights and the live 7/7 summary.
