# Reminders, Delivery Tracking, and Customer Languages Implementation Plan

> **For agentic workers:** Use superpowers:subagent-driven-development for the independent tasks, then review the combined changes. Preserve the current workspace and its existing changes; no reports, commits, or deployment are requested.

**Goal:** Complete pre-start reminders, safe admin notification tracking, and English/Korean customer journeys, then verify the connected flows.

**Architecture:** Reuse the durable MongoDB outbox and existing notification worker. Business owns reminder rules and admin delivery DTOs; Data owns narrow queries and atomic updates. Web owns authorization, localization and UI. Customer language preferences must not change reservation data, UTC storage or Manila schedules.

**Tech Stack:** .NET 10 MVC, MongoDB, locally bundled Tailwind v4/GSAP, existing SMTP/Mailpit.

**Spec:** `Documentation/PROJECT.md`, communication and browser language requirements; owner request on 14 November 2026 excludes daily reports.

## Global Constraints

- Keep `Web → Business → Data`; no direct MongoDB calls in controllers.
- Async I/O accepts `CancellationToken ct`; never migrate on Web startup.
- Retain all existing uncommitted work and the configured Atlas preview on port 5275.
- Integration tests use an isolated local MongoDB replica set, never Atlas.
- Admin delivery views never return email bodies, verification codes, status-link tokens or credentials.
- Customer language choices are `en` and `ko`; first visit follows supported browser preference, unsupported languages fall back to English, explicit cookie choice takes precedence. Staff and outgoing emails stay English.
- Match flat system UI, mobile density, keyboard focus, reduced motion, and visible initial markup.

## Review Focus

1. Changed schedules, cancellation, check-in, stale retries and competing workers must not send an obsolete reminder.
2. SMTP is at-least-once: a crash after acceptance may still duplicate delivery; use a stable Message-ID and do not claim exactly-once SMTP.
3. Admin retry must reject sent/in-flight/ineligible messages and expired verification emails; never disclose protected message content.
4. Korean must cover server/client validation, dynamic availability, receipt, change/cancellation and authentication flows, not only navigation.
5. Language switches must stay local, preserve safe page context, keep the same booking rules, and work without JavaScript.

## Task 1 — Reservation reminders

**Files:** `HoleInHub.Business/Reservations/ReservationService.cs`, a focused reminder policy file, `HoleInHub.Data/Reservations/OutboxPersistence.cs`, a narrow reminder query in ReservationPersistence, `HoleInHub.Web/Accounts/EmailDelivery.cs` only as needed, and reservation/outbox tests.

**Interfaces:** Continue `ProcessPendingAsync(CancellationToken)` as the worker entry point. Reminder records use stable IDs containing reservation ID and the UTC start ticks, with `RequiredStatus = "Confirmed"`. Eligibility must compare the stored reminder start to the latest reservation start, not merely its status. Coordinate shared Outbox files with Task 2.

- [ ] Test the exact 15-minute boundary, tomorrow at the same time, late confirmation, no email, changed time, cancelled/checked-in booking, concurrent enqueue and retries.
- [ ] Queue confirmed reservations with a valid recipient when `now < StartUtc <= now + 15 minutes`; use the same saved ID for repeat scans. Do not queue already-started sessions.
- [ ] Format reminder date/time in Manila. No status link or new access grant is needed in the reminder.
- [ ] Recheck eligibility immediately before sending. Obsolete messages become suppressed, not delivered.
- [ ] Keep original verification and event notifications working; record actual test results.

## Task 2 — Admin notification delivery tracking

**Files:** new `HoleInHub.Data/Reservations/NotificationDeliveryQueries.cs`, new `HoleInHub.Business/Accounts/NotificationDeliveryService.cs`, new Web controller/ViewModel/views, sidebar link, shared styles, and authorization/retry tests. Amend `OutboxRecord`, `OutboxPersistence`, `NotificationService` and worker only through a coordinated small contract.

**Interfaces:** Admin-only `/admin/notifications`, status filter (`all`, `queued`, `sending`, `sent`, `failed`, `suppressed`), page size 20. GET list returns safe metadata: recipient, subject, due time, attempt count, state, completion/failure timestamp and generic failure category. POST retry takes message ID and antiforgery token; rechecks state/eligibility and atomically resets a terminal failed item. No message-body endpoint.

- [ ] Test non-admin rejection, invalid filters/page clamping, metadata safety, sent/in-flight rejection, stale and concurrent retries, expired verification suppression.
- [ ] Extend outbox completion to distinguish sent and suppressed, and record generic failure type/time without SMTP exception text. Existing rows remain readable; optional fields require no destructive data rewrite.
- [ ] Keep worker leasing and maximum attempts intact; retries re-use the message and stable Message-ID.
- [ ] Add compact, paginated admin UI with a confirmation action and shared toasts. Do not change the daily report.
- [ ] Document SMTP delivery meaning and operational retry limits.

## Task 3 — English/Korean customer interface

**Files:** Web localization registration/middleware, focused localization resources/helpers and language controller, customer layouts/views/controllers/ViewModels, customer scripts and tests. Do not edit staff views except shared layout pieces that also serve customers; staff pages stay English.

**Interfaces:** ASP.NET culture negotiation; only `en` and `ko`; persistent explicit culture cookie; same-origin validated return URL; `<html lang>` matches rendered culture. Translate customer-facing static copy and dynamic feedback using reusable server/client resources, preserving Razor encoding and user-entered content.

- [ ] Test Korean/English Accept-Language, unsupported fallback, cookie precedence, unsafe return URL rejection and switching without JS.
- [ ] Translate navigation, service/availability, guest/account booking, verification, account pages, receipt/status/history, change/cancellation, validation and error states.
- [ ] Localize dates/numbers for presentation while retaining Manila time and machine-readable ISO values; never translate references, email addresses, names, bay IDs or persisted service/status identifiers.
- [ ] Keep customer scripts, accessible labels and client errors consistent with server language; preserve English smoke-test compatibility.
- [ ] Document that Korean wording still needs a fluent human review.

## Task 4 — Verification and integration

- [ ] Review all three task diffs and fix interface conflicts; check no secret/body leaks or scope creep.
- [ ] Run solution build, non-integration .NET suite, Tailwind build, and UI tests.
- [ ] Locate or provision an isolated local replica set and run targeted plus full integration checks if possible. Never use Atlas as the integration test target.
- [ ] Check available browser surfaces for desktop/mobile journeys; record unavailable browser access honestly.
- [ ] Preserve preview environment and verify `/health/ready` before and after any restart. SMTP preview delivery must remain in the configured local Mailpit environment.
- [ ] Update current-system documentation and this ledger with implemented boundaries, actual verification and remaining limits.

## Execution ledger

- Planning: inspected existing outbox leasing, retries, worker polling, project requirements and prior work. Default decisions above are implementation choices under the owner's authorization.
- Work stays in the current workspace because the active preview and accumulated owner-directed changes are here.
- Implemented pre-start reminders for eligible confirmed reservations, with a schedule-keyed outbox ID and a final eligibility check before SMTP delivery.
- Added an admin-only delivery list with status filters and confirmed retries. Sent means SMTP accepted the message. A crash after acceptance can still cause a duplicate. Suppressed and stale protected messages cannot be retried.
- Added English and Korean customer pages. Supported browser preferences apply on first visit, and an explicit choice persists in a cookie. Staff pages and outgoing emails use English. Korean copy still needs fluent review.
- Verified the solution build, Tailwind build, 177 unit tests, 156 integration tests on a local MongoDB replica set, 196 Web tests, and 133 UI tests. The Web suite included local SMTP acceptance, role and antiforgery enforcement for retries, and language negotiation.
- Refreshed the Atlas preview at port 5275 using its existing environment and local Mailpit configuration. Readiness and live HTTP rendering for the English and Korean sign-in pages passed. Manual desktop and mobile browser walkthroughs are pending.
