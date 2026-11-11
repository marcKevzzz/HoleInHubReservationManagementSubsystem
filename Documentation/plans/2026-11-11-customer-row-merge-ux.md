# Customer row-to-row merge — UX and implementation plan

**Status:** Implemented after owner approval on 11 November 2026. The configured port 5275 preview has drag-to-review and the explicit merge action; dropping never saves a merge.

**Audience and job:** Staff, Manager and Admin who recognize two records as the same person while scanning Customers. Start their comparison without opening and closing several detail pages, and keep the final identity decision explicit.

**Authority:** Keep the existing Hole In Hub workspace, tokens, typography, icons and staff-only merge boundary. Reuse the installed profile memberships, preview, revision checks, idempotency, audit and undo. No migration or new dependency is required.

## Recommended journey

1. **Choose the pair.** Dragging the row lifts a complete visual copy while retaining its place in the table; the grip signals the gesture. Normal row clicks still open customer details; links, text selection and scrolling keep their current behavior. Dragging A onto B selects A as the source and B as the main record. Releasing outside a valid row cancels.
2. **Review in one large dialog.** The drop opens the comparison; it never merges immediately. Fetch current server data once on drop. Label both records, show their contact details, booking counts/date ranges, and give each an Inspect history link that preserves the comparison.
3. **Choose the main details.** Default to keeping the drop target. Staff can switch the main profile and select the primary name, email and phone from the two records. Show identical normalized values once. When the main value is missing and the other is populated, select the populated value visibly by default. All other contact values remain retained; label choices “Use as primary,” not “Delete the other value.”
4. **Review included bookings.** Default to all bookings with a distinct combined total. Keep the existing selected-booking option for shared-contact groups. Moving only selected reservations leaves the remaining source record active.
5. **Confirm within the same dialog.** Review advances to a short final summary and required reason. A Back action preserves choices. Only “Merge records” sends the existing protected POST. Do not stack the existing reason dialog inside the large dialog.
6. **Return to the list.** On success, refresh the current directory search/page and show the main profile’s updated row. A full merge removes the source row; a partial merge updates both. Restore focus to the resulting main record and expose View customer / Merge history. Cancel closes without changes and returns focus to the initiating handle/action.

## Desktop composition

Use a centered wide dialog (maximum approximately 1,120px), contained within the viewport. A fixed header identifies the action and close control; the content scrolls; the action footer remains reachable.

- A clear two-column comparison leads. One column is visibly marked Main profile; the other is Record to combine. Both have equal reading structure rather than different card layouts.
- Primary field choices appear directly alongside the two source values. Retained additional emails/phones are grouped distinctly and deduplicated using the same display rules as the detail page.
- Name, primary email and primary phone are independent choices. Switching the main profile must not silently reset choices the employee already reviewed; preserve selections that still belong to the pair.
- The reservation scope and resulting count remain visible. Detailed bookings expand only when needed.
- Final confirmation shows the chosen main identity, primary contact, moved-booking count and reason. Keep the staff-only/account-access explanation here once; it is not repeated on the customer detail page.
- Use the existing green selected state and primary/tertiary action styles. Pending/loading/error messages stay in the dialog; no blank modal or page-wide spinner.

## Drag affordance and alternatives

| Input / situation | Behavior |
| --- | --- |
| Mouse | Start from the row handle after a small movement threshold. Show one lightweight row preview, a clear eligible target outline and “Review merge with [name]”. Do not move the table layout. |
| Same row / same canonical profile | Ignore the drop; explain Already the same customer record when relevant. Server canonical resolution is authoritative. |
| Drop outside table / Escape | Cancel the gesture, clear the preview/highlight and leave data unchanged. |
| Keyboard / touch | An explicit per-row “Merge customer” action opens the same dialog with a record picker. Selecting the second record enters the identical comparison. Dragging is never the only route. |
| Small screens | Use a full-width dialog with stacked, labelled records and reachable actions. Use the explicit merge action rather than requiring a long press that conflicts with page scrolling. |
| JavaScript unavailable / unsupported dialog | Existing full-page merge links remain functional. |

Mouse dragging starts from the row surface, excluding links and form controls. Touch continues to scroll normally. Preserve row Enter-to-open and the existing arrow action. Keep the drag preview clipped to the viewport; it must not create horizontal page overflow. Scroll gently near list edges; respect reduced motion and cancel safely on navigation.

## Safeguards and recovery

| State | Required result |
| --- | --- |
| Preview loading | Show both selected row names immediately, then current server fields/counts. Abort the request if the dialog closes; ignore obsolete responses. |
| Shared phone/email or near-identical name | Show as evidence, never identity proof or a made-up confidence score. Staff still review the records and booking scope. |
| Missing / identical details | Explicit Not recorded state; do not offer duplicate radio options or erase populated contacts silently. No free-form identity edits in this merge flow. |
| Search/filter/page changes during drag | Cancel the drag before results are replaced. Abort stale search work and release pointer capture; do not leave a target attached to a removed row. Preserve the directory context for the modal’s return. |
| Record already merged / new booking after preview | Resolve current canonical profiles and revalidate both revisions at commit. Require renewed review on a conflict; never silently include unseen bookings. |
| Double click / unknown save result | Disable duplicate submission. Retry the same operation ID and intent to retrieve the existing receipt if it committed. A renewed comparison gets a fresh operation ID. |
| Network unavailable | Keep the dialog and choices. Give Retry and an ordinary full-comparison link. Do not guess that the merge saved. |
| Access expires | Use current staff authorization on POST; show the existing sign-in/forbidden path without exposing data to an unauthorized session. |
| Saving | Keep a visible saving state and guard repeated actions. Close/Back cannot discard an in-flight result silently. On a definite failure, restore the editable review state. |
| Mistaken confirmed merge | Existing audited Undo merge remains the recovery route, subject to semantic-state/operation-lineage checks. No automatic rollback over later work. |

The operation still changes staff profiles/history only. Login accounts, booking ownership/contact snapshots, guest access, capacity, payments and reservation status remain unchanged. No fuzzy or contact-only automatic merging is introduced.

## Smallest implementation

1. **Extract the current comparison markup into a Razor partial.** Reuse it for the full page and dialog so contact choices and booking scope cannot drift. Keep the existing server-created operation ID, field validation, anti-forgery token and revision fields.
2. **Add a read-only partial response to the existing merge GET.** The intended pair maps to `/staff/customers/{drop-target-key}/merge?target={dragged-source-key}` because the current controller treats its route record as the main profile. Directory keys may be booking IDs; let the server resolve their canonical profile IDs. Leave existing Business/Data merge and undo APIs intact.
3. **Enhance Customers only.** Add the drag affordance in `Views/Customers/Index.cshtml`; add one scoped interaction script and dialog styles. Integrate with the existing directory search/row lifecycle, including `staff:rows-updated`. Ignore buttons/links/text selections in the generic row navigation handler. Keep the customer-profile-link class as the directory row navigation target; adding a merge link must not change ordinary row clicks from Details to Merge. Do not make unrelated staff tables draggable.
4. **Keep review and confirmation in one dialog.** Use the existing reason field rules without opening a nested `_ReasonConfirmation` dialog. Prefer ordinary form semantics and real links for fallback. On success, refresh the directory through its existing search mechanism and restore focus/status.
5. **Ship only after interaction checks pass.** No Atlas customer merge is required for testing this UI. Use isolated test fixtures for mutation/races; use existing Atlas demo records only for read-only inspection unless another merge is explicitly authorized.

## Acceptance checks

- Mouse drag, short click, text selection, modified link clicks, outside drop, Escape and pointer cancellation do not trigger unintended navigation or writes.
- Keyboard/touch can choose the same pair and complete the same review without dragging; focus stays contained and returns correctly.
- Preferred fields, duplicate phones in local/international format, repeated emails with casing differences, absent values, long names/emails and selected-booking counts render correctly.
- Directory filtering, pagination and replacement during a gesture cannot leave stale highlights or merge the wrong pair.
- Server-rendered preview, hidden revisions, role authorization, anti-forgery, stale conflicts, idempotent retry, membership integrity and undo remain covered.
- Desktop/narrow layouts, reduced motion, scroll containment, slow/error responses and an expired staff session are checked in one inspection batch and at most one correction batch.
- A full merge produces one canonical row/history; a partial merge retains the remaining source. Original account-owned/guest booking access is unchanged.

## Review decision

Recommended v1: **row-to-row drop opens review**, with Merge customer retained on customer details for keyboard/touch; one wide dialog contains both review and final reason/confirmation. The drop target is the default main profile, with explicit independent primary-field choices. This interaction is enabled. Existing full-page comparison remains the fallback.


## Owner refinement implemented

The directory merge icon was removed at the owner's request. Dragging anywhere on a non-interactive row surface lifts the entire row with a shadow and a gentle lift/landing transition; the source keeps its place and the destination highlights. Reduced motion skips these transitions. Keyboard and touch users open customer details and choose Merge customer. This supersedes the earlier per-row action recommendation. The modal uses compact record headers and primary-field choices, with repeated captions/history links removed; final reason and protected confirmation stay intact. Contact details retain 20px padding and internal dividers with no background or outer border.


## Compact review and matching guard

The owner requested a further compact review: inline field choices, Reservation history disclosure, grouped footer actions, explicit loading spinner, reduced copy, and a reduced-motion-aware invalid shake. Main-profile switches no longer show an informational message. Pair comparisons skip the unused directory-candidate query. A merge requires two distinct words shared by a pair of recorded names, a case-insensitive email match, or a normalized phone match. The comparison checks primary and retained contact details. This is enforced in Business before the revision-guarded write; committed retries continue through the existing idempotency path. A shared value permits staff review, never automatic identity consolidation.

Verification: build and CSS successful; 372 .NET tests and 85 JS checks pass. Browser checked unmatched rejection, matching review, quiet main switch and grouped actions. No Atlas customer merge was saved. Narrow browser layout and artificial slow-network failures were not rechecked.
