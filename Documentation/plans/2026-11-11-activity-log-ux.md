# Activity logs: UX and implementation plan

**Status:** Implemented in the feature checkout and running on the local port 5275 preview. Staff visibility was confirmed by the owner: operational actions by everyone.

**Job:** Help employees answer who changed a reservation/customer/schedule, what happened, and where to continue. Reuse the established staff workspace, input underline focus, disclosure controls, icon weight and restrained motion. This is an operational screen, not a monitoring dashboard.

## Visibility

| Audience | Operational activity | Access / security activity | Customer-facing history |
| --- | --- | --- | --- |
| Staff | Everyone's permitted operational actions, including Manager/Admin actions | No | Existing protected staff booking/customer routes |
| Manager | Same operational feed as Staff | No | Existing protected staff routes |
| Admin | All operational activity | Persisted role/access changes in v1 | Existing protected staff routes |
| Customer / guest | No staff Activity access | No | Own existing reservation history only |

Everyone means all actors in operational records, not all data in all collections. Label customer and System events clearly. A shared feed grants no new ability to modify bookings, merge customers, edit roles or access customer-owned routes. Enforce source visibility before querying/serializing rows, event details, counts, search, filter options and pagination. A forged Access filter/event URL must not reveal an entry to Staff or Managers. Recheck current authorization on every request and destination.

## Existing truth and initial coverage

| Source | Show in Activity | Important limit |
| --- | --- | --- |
| `reservations.History` / `HistoryRecord` | Recorded booking creation/review, schedule changes, cancellation, attendance and expiry events | Some legacy entries have Status/Actor but no event type, event ID or actor-role snapshot. Display only facts actually recorded. |
| `schedule_blocks` | Created/released block events where timestamp and actor exist | Current block snapshots are not a full edit ledger. Do not invent unrecorded intermediate changes. |
| Customer profile operation records | Merge and undo events, actor, reason, related profiles/reservations | Show operation-time names/details; links resolve today's canonical profile. Preserve original operation identity. |
| `staff_role_audit` | Admin-only access changes, before/after roles and reason | Already persisted; retain its protection. |
| Structured authentication logs | Excluded from the initial feed | Sign-in/switch messages are not currently a persisted searchable audit history. Do not scrape runtime logs or label an empty tab as comprehensive security coverage. |

V1 category labels: **All, Reservations, Customers, Schedule**, plus **Access** for Admin. All includes only categories the viewer may read. No role filter based on today's account role: that would misrepresent historical actions. Use an employee/actor filter instead. Legacy unidentified actors appear as Recorded actor / Unknown as appropriate, never a guessed employee.

Authentication audit persistence is a separate follow-up. Before enabling it, decide event delivery failure policy, retention and metadata requirements; use the append-only security stream specified in the RBAC design. Manager visibility into security data stays disabled. Never store passwords, PINs, codes, reset links, cookie contents or request bodies in an activity record.

## Page layout

Use the same main-content width and padding as Customers. Place **Activity** under **Overview**, visible to Staff, Manager and Admin. Remove the duplicate Admin sidebar entry once its access events are represented here.

```text
Activity                                      Refresh [icon]

[ Search activity________________ ] [Last 7 days v] [Everyone v]
All    Reservations    Customers    Schedule    Access (Admin)
-----------------------------------------------------------------
When           Activity                      By              arrow
Today 14:32    Booking moved                  Marc Kevin        ->
               HIH-... / Bay 2 -> Bay 3
Today 14:20    Customers merged               Jane              ->
               Alex Santos / 2 reservations
Yesterday     Time blocked                   Manager name      ->
               Bay 4 / 12:00-13:00
-----------------------------------------------------------------
                                          Previous     Next
```

- Newest first; default last seven Manila calendar days including today. Date presets Today, Last 7 days, Last 30 days, and custom inclusive calendar range, capped at 90 days per request. Search/filter permits older history through an explicit date range; no retention deletion is implied.
- Four columns: When, Activity, By, action arrow. Reference/target appears as the secondary line of Activity. Avoid a separate column for every technical field.
- Day grouping is visual, with exact Manila date/time available in accessible markup. Do not replace precise timestamps with only relative labels.
- Search debounces 350ms with Enter fallback; filter/category changes apply immediately. Search only approved reference, actor label and target display-name fields; no search through free-text reasons or private payloads. Search and filters stay in the URL.
- Everyone and My activity are quick actor choices; searchable employee selection uses stable account IDs where available. A legacy label cannot be silently assigned to an employee with the same name. Deleted employees remain readable using recorded labels and IDs.
- Show 50 entries per page with stable cursor pagination. No expensive global event counts or total-pages calculation. No infinite scroll.
- Whole row opens the event; the arrow is the equivalent keyboard/touch link. Preserve modified-click/new-tab behavior through a real event URL.
- No KPI cards, pie charts, severity colors for ordinary actions, exports, delete/clear controls or automatic replay/undo from this feed.

## Event detail and onward navigation

Desktop: an approximately 480px right-side detail panel, preserving the list behind it. Mobile: full-width detail view. The same server-rendered event route works without JavaScript and for copied links.

Structure:
1. **Booking moved** (or equivalent concise event title), close/back control.
2. Target reference/name; recorded actor and exact Manila time.
3. Only fields actually changed, with a simple Before / After comparison. A successful status change is not a new editable form.
4. **Reason** only when recorded; never show a blank Reason section. Long text wraps and can expand. Exclude protected administrative reasons from operational responses.
5. One contextual primary link such as **Open booking**, **View customer**, **View merge history**, **View schedule**, or **View team member**, with a right/north-east arrow. Prefer merge history for merge/undo events because that is where the recorded operation is explained.

Keep technical event reference collapsed under Details when useful for support. Do not expose command hashes, security stamps, raw BSON, ownership identifiers or secret-bearing payloads. Display historical snapshots as recorded, with current target state separately if available; do not rewrite the past using current contact details.

Navigation contract:
- Canonical list: `/staff/activity`; detail: `/staff/activity/{eventKey}`. Include current list filters/cursor and selected event in safe local navigation context.
- JavaScript opens detail in the panel and updates the URL. Browser Back closes it and restores the same list position. Direct detail links render a complete page with Back to activity; refresh must retain selected event/filter state.
- From Activity to booking/customer/merge-history/team/schedule, show **Back to activity** at the destination. Returning restores filters, cursor, scroll and focused event. Preserve existing Customers -> booking and Queue -> booking return paths when they are the entry point instead.
- Validate any return destination with local URL validation and an allowlist for the Activity path. Reject external, scheme-relative and unrelated local paths. UI return context is never authorization.
- Schedule links use the relevant Manila date and highlight an existing booking/block when it is still on that day. If moved/deleted, explain that briefly and offer the current target; never silently show an unrelated day.
- Existing `/admin/activity` redirects to the canonical Activity page with Access selected, retaining Admin authorization. `/manager/activity` redirects to operational Activity instead of Schedule. Unauthorized direct Access requests are denied rather than redirected to an unrelated page.
- A target that was deleted, merged or became inaccessible leaves the audit event readable if allowed. Explain target unavailable and omit/disable its onward link; no broken navigation or unauthorized content preview.

## Feedback and interaction

- First load: immediately show the page title, filters and a labelled Loading activity indicator. Keep all initial server-rendered content usable without JavaScript.
- Filtering: retain the current list while showing a small loading indicator beside the toolbar; temporarily mark it busy. Abort stale requests and ignore obsolete responses. Keep old results on error, label them as previous results, and provide Retry.
- Background changes never insert rows under a pointer or reset a keyboard selection. Use an explicit Refresh control; retain the selected event if still present. No polling/WebSockets in v1.
- Detail load: open the panel immediately with target/title when known and a visible loading indicator. Cancel fetch on close. Access expiry shows a safe sign-in return link; do not render a sign-in document inside the detail panel.
- Empty states: No activity in this date range versus No matching activity, with Clear filters when relevant. Invalid range highlights its field, without clearing entered filters.
- Grouped panel actions, short labels, subtle arrow movement/row background on hover. Reduced-motion users receive final states without entrance/slide animations. Activity rows represent evidence, so no shake or celebratory animations here.
- Visible keyboard focus, accessible table/list and date controls, Escape to close, focus returned to the launching event. Detail loading/error messages are announced without moving focus repeatedly.

## Smallest complete architecture

Keep the existing audit sources authoritative. V1 is a read model over them, not a second event-writing subsystem.

- Data: narrow Activity query methods project approved event fields, filter by permitted source/date/actor/target and return at most 51 sorted candidates per source. Merge bounded pages in a deterministic global order. Never load all reservation histories/accounts and paginate in memory.
- Embedded histories need an indexed parent date match followed by unwind and a second exact event-date match; verify the query plan with representative data. Use a narrow aggregation projection, no N+1 lookup per row. Fetch visible actor labels/target metadata in batches where needed.
- Stable sort `(occurredUtc, source, parentId, eventKey)`. Prefer real operation/command IDs; legacy append-only history entries can use a namespaced parent ID plus array index. Confirm append-only stability before using that fallback. Sign/validate cursor context so changing filters resets pagination; cursor content never grants access.
- Carry an as-of timestamp with each paging session. Refresh starts a new session; simultaneous new entries cannot shift already-read pages. Keep event details anchored to their stable source identity.
- Business: normalize source events into an allowlisted activity DTO with title, category, recorded actor, target, time, available changes and reason. Resolve employee/system/customer labels without guessing roles. Apply the role/source scope explicitly.
- Web: routing, HTTP/return-context validation, role checks, view models and Razor components. URLs to target controllers are composed in Web, not Data/Business.
- No migration assumed automatically: inspect existing indexes and add only measured required ones through the explicit Migrator with the next available migration number. No migrations at Web startup, no automatic live backfill, no retention TTL without an approved policy.
- Do not backfill guessed actors, roles, event types or timestamps. If complete historical filtering cannot be supported, identify the missing source coverage rather than fabricate it.

## Implementation sequence and proof

### 1. Read model and access boundary
- [ ] Add Data Activity query/projection and Business DTO/service. Keep queries bounded and cancellable.
- [ ] Test source mapping, same-time sorting, duplicate source IDs, missing legacy fields, Manila midnight ranges, actor filtering, cursor stability, merged/deleted targets and maximum-range validation.
- [ ] Test Staff/Manager exclusion of Access entries from list, detail, search, filter options and pagination; prove Admin visibility and customer/anonymous denial. Test forged IDs/filters and revoked sessions.

### 2. Activity list and detail
- [ ] Add Activity controller, list, reusable rows and event-detail partial/page, using existing staff shell and icon components.
- [ ] Add scoped debounced search, filter state, explicit refresh, cancellable panel loading and browser-history behavior. Keep real server-rendered links/forms as fallback.
- [ ] Verify empty/loading/error/expired-session states, keyboard/focus, long reasons/names, reduced motion, small-screen containment, and stable rows during refresh.

### 3. Connected navigation and rollout
- [ ] Update sidebar and protected legacy redirects. Add Activity return context to booking/customer/merge-history/team/schedule destinations through a shared small Web helper, preserving existing entry-point behavior.
- [ ] Prove malicious return URLs are rejected; test list -> event -> target -> Back to activity, browser Back/Forward, direct links, reload and changed/deleted targets.
- [ ] Inspect indexes/query plan; add a migration only if required. Update RBAC/onboarding guidance and document exact covered event types.
- [ ] Run solution build, non-integration tests, isolated local MongoDB integration tests, relevant JS tests and CSS build. Inspect desktop/mobile once, fix the observed issues in one batch, then confirm once.
- [ ] Preserve the configured 5275 launch environment and check readiness after deployment. Inspect Atlas read-only; use isolated local fixtures for new audit/security scenarios.

## Deliberately deferred

Persistent sign-in/security-event ingestion, exports, notifications, live streaming, charts, retention changes and log deletion are outside v1. Adding authentication logs later requires its own write/reliability policy and approval; it must not be implied by an Access tab that initially contains role changes only.


## Implementation notes

- Search accepts a booking reference, recorded actor or target name. Select an actor in the feed to apply the exact actor filter. Everyone and My activity are in the filter bar.
- The authenticated event route also renders a standalone page for copied links and navigation from related records. Enhanced paging remembers previous pages within the tab. Direct cursor links offer Newest and browser history remains available.
- Managers and Admins can open block details. Staff see the related active block highlighted on the schedule for its Manila date.
- Feed queries cap each source at 51 rows and apply a date filter before unwinding reservation histories. Existing indexes were inspected. There is no new activity-wide date index, and representative Atlas query performance remains to be measured before a broader rollout.
- Build and CSS compilation succeeded. All 414 .NET tests and 85 existing JavaScript checks passed. Local preview readiness passed. Browser click-through and responsive inspection were unavailable because local browser use is disabled in Work settings.
