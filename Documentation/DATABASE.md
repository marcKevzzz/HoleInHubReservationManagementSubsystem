# Database schema and data dictionary

Schema-hardening contract: **006_SchemaHardening**, reviewed 2026-11-04. The shared checkout also contains the separate **007_GuestReservationAccess** extension described below. This page explains the existing customer/booking system; it does not introduce admin workflows, live payments or operational reporting. A schema present in source is not proof it has been deployed. MongoDB replica-set transactions remain required.

[View the current data model diagram](diagrams/current-data-model.md) for collection relationships, embedded booking records and internal support collections.

## Start here: business records and internal records

| Physical collection | Plain-language name | What it stores |
| --- | --- | --- |
| `accounts` | Customer login accounts | Identity, contact defaults, roles and security state |
| `resources` | Reservable bays | Bay names, Regular/VIP category and active flag |
| `reservations` | Bookings | Owner scope, contact snapshot, interval, bays, saved deadline, history and demo payment state |
| `schedule_blocks` | Unavailable periods | Closures/maintenance for selected bays; an empty resource list blocks all active bays |
| `guest_verifications` | Email verification challenges | Both guest and account email verification; historical name retained for compatibility |
| `guest_recovery_quotas` | Recovery email delivery limits (007) | Per-email issuance timestamps and expiry for the separate recovery feature |
| `outbox` | Notification delivery queue | Messages, worker claims, retry counts and delivery outcome |
| `allocation_guards` | Booking concurrency controls | Counters competing transactions update to prevent incompatible allocations |
| `__migrations` | Database upgrade history | Applied migration IDs, checksums and execution metadata |
| `__migration_leases` | Database upgrade ownership | Temporary exclusive claim preventing concurrent migration runners |
| `system_heartbeats` | Foundation diagnostic record | Baseline heartbeat written by migration 001; not live user activity |

Internal records support correctness and recovery; they are not additional customer features. Do not delete guard/lease/upgrade records as routine cleanup. The application does not require a separate customer or allocation collection: contact snapshots and resource IDs are embedded in bookings.

## Naming and compatibility

Stored names are case-sensitive. Most application fields use PascalCase; `Id` maps to MongoDB `_id`. Migration metadata has explicit camelCase mappings; guard `version` is lower-case. Case differences alone do not make a schema invalid. Existing BSON names are preserved so applied migrations and existing data retain their meaning.

| Code name / stored name | Meaning |
| --- | --- |
| `AccountId` / `AccountId` | Legacy booking ownership and duplicate-submission scope. Contains an account ID OR `guest:` plus a session hash; it is not always a login-account reference |
| `CustomerAccountId` / `CustomerAccountId` | Actual account ID for account-owned bookings; omitted for guest bookings |
| `EmailVerificationId` / `GuestSessionHash` | Hashed identifier of the email proof consumed by a guest submission |
| `SubmissionHash` / `PayloadHash` | Fingerprint of booking details; detects reuse of a submission key with different details |
| `AccessTokenHash` / `AccessTokenHash` | Hash used to check a guest's secret status link, not the secret itself |
| `ProtectedContext` / `ProtectedDraft` | Data-Protection-encrypted booking draft or account confirmation context |
| `ConsumedBySubmissionKey` / `ConsumedKey` | Submission key that already used a guest verification proof |
| `EmailVerificationPersistence` | Shared persistence service for account and guest challenges; historical record class remains GuestVerificationRecord because migration 004 references it |

No collection is renamed in this release. `Purpose` explicitly identifies new verification challenges as `GuestBooking` or `AccountEmail` in 006; 007 additionally permits `GuestRecovery`. Purpose is absent on legacy challenges because their hashed scope cannot safely be reverse-inferred. Existing scope hashes and authorization checks remain unchanged.

## Booking document: reservations

| Stored field | Type | Purpose |
| --- | --- | --- |
| `_id` | string | Internal immutable booking ID |
| `Reference` | string | Customer-facing booking reference; never authorizes access |
| `AccountId` | string | Legacy ownership/idempotency scope described above |
| `CustomerAccountId` | optional string | Actual login-account relationship; no guest account is fabricated |
| `Email` | string | Booking contact email captured at submission |
| `FirstName`, `LastName`, `Phone` | strings | Booking contact snapshot; subsequent profile edits do not rewrite it |
| `Service` | string | Regular, VIP, PhilippineCoach, KpgaPro, KpgaGroup or WholeShop |
| `Status` | string | Reservation lifecycle; see accepted values below |
| `StartUtc`, `EndUtc` | BSON dates | Protected half-open interval `[start, end)`; start must precede end |
| `CreatedUtc` | BSON date | Creation time |
| `ReviewDeadlineUtc` | BSON date | Saved expiry deadline; not recalculated from current policy |
| `PolicyVersion` | non-negative int | Legacy 0, per-bay participant flow 1, total-guest flow 2 |
| `ResourceIds` | nonempty unique string array | Selected bay IDs; all succeed together |
| `ResourceNames` | object | Bay-name snapshot keyed by resource ID |
| `Attendees` | positive int | Total guests including non-players |
| `Participants` | array | Legacy per-bay ResourceId, Players and NonPlayers; empty for current total-count flow |
| `Notes` | string, maximum 1000 characters | Customer notes |
| `PrivacyAccepted` | bool | Consent flag captured at submission |
| `GuestSessionHash` | string or null | Guest proof identifier; code name EmailVerificationId |
| `AccessTokenHash` | string or null | Guest status-access token hash |
| `IdempotencyKey` | nonempty string | Caller submission identifier, unique together with AccountId |
| `PayloadHash` | nonempty string | Submission content fingerprint; code name SubmissionHash |
| `CoachConfirmed` | bool | Existing staff-recorded coaching prerequisite; not a coach availability calendar |
| `CancellationRequested` | bool | Customer request awaiting staff decision; alone does not release capacity |
| `PaymentStatus` | string | Separate demo deposit state |
| `PaymentReference` | string or null | Demonstration payment reference |
| `RentalTotal`, `DepositAmount` | nullable numbers | Demo amounts; current C# models use decimal. No live financial ledger is claimed |
| `History` | embedded array | Recorded actions and resulting states |

Core required fields and exact optional/type rules are defined by [reservation.schema.json](schemas/reservation.schema.json). The complete database rule, including start-before-end, is [reservation.validator.json](schemas/reservation.validator.json). Compatibility means some newer fields remain optional for old documents; application validation still requires complete current submissions.

Accepted stored reservation states are Pending, Confirmed, Checked-In, Completed, Cancelled, Rejected, Expired and No-show. Checked-In, Completed and No-show are retained legacy/target-compatible values, not newly implemented attendance actions. Do not substitute CheckedIn or NoShow without a deliberate migration.

Payment states are NotRequired, AwaitingDeposit, DepositReceived, LatePayment, Failed and CheckoutCancelled. Payment status does not imply arrival, completion or an actual POS transaction.

### History entries

| Field | Meaning |
| --- | --- |
| `Status` | Legacy display description such as Pending, Cancellation requested or Demo payment: DepositReceived; it is not reliably a lifecycle enum |
| `EventType` | New machine-readable action: SubmissionCreated, ReservationConfirmed, ReservationRejected, CoachConfirmed, CancellationRequested, ReservationCancelled, CancellationDeclined, ReservationExpired or DemoPaymentRecorded |
| `ReservationStatus` | Reservation lifecycle immediately after that action |
| `AtUtc` | Action time |
| `Actor` | Existing actor identifier/label |
| `Reason` | Applicable explanation |

For example, a cancellation request has EventType CancellationRequested and ReservationStatus Pending while its old Status text remains Cancellation requested. Demo payment history records the final state after confirmation/expiry is decided. Existing history entries are preserved; missing structured fields are not guessed or overwritten. Histories are expected to remain small per booking; monitor growth before introducing repeated/high-volume events. Never silently truncate audit history.

## Account document: accounts

Fields: `_id`, Email, NormalizedEmail, PasswordHash, SecurityStamp, Version, EmailConfirmed, AccessFailedCount, LockoutEndUtc, Roles, FirstName, LastName, Phone and optional GoogleSubject. See [account.schema.json](schemas/account.schema.json).

- NormalizedEmail is the normalized lookup value, uniquely indexed.
- PasswordHash stores password verification data, not plaintext; Google-only accounts can have an empty password hash.
- SecurityStamp is the authentication security-change marker; Version is the concurrency token preventing stale account replacement.
- GoogleSubject is Google's stable user identifier, not their display name/email. A partial unique index covers present string subjects.
- AccessFailedCount and LockoutEndUtc support account lockout. Roles are server-controlled; public registration creates Customer only.

## Resource and unavailability documents

`resources`: `_id`, Name, Service (Regular/VIP), Active. See [resource.schema.json](schemas/resource.schema.json). Five active regular bays and two VIP bays follow migration 004. Stored bay IDs remain unchanged even where display names were revised.

`schedule_blocks`: `_id`, ResourceIds, StartUtc, EndUtc. The list has unique resource IDs and can be empty to mean all active bays. The interval must have start before end. See [schedule-block.schema.json](schemas/schedule-block.schema.json) and [schedule-block.validator.json](schemas/schedule-block.validator.json). Admins can create single-bay blocks from Staff → Reservations → Block time. Creation requires a reason, uses the reservation allocation guards, and rejects overlaps with active reservations or existing blocks. Optional fields `Reason` (1–500 characters), `CreatedBy`, `CreatedUtc` (UTC), and `SubmissionHash` record audit and retry information. Legacy closures remain valid without these fields. New block IDs derive from the authenticated actor and submission key; retries return the original block, while changed payloads are rejected. Apply migration `011_AdminScheduleBlocks` explicitly through Migrator before rollout. Editing, recurring blocks, and venue-wide creation remain outside this release.

## Email verification challenges

Stored fields: `_id` (hashed scope), Email, CodeHash, SentUtc, ExpiresUtc, Attempts, VerifiedUtc, ProtectedDraft, ConsumedKey and optional Purpose. See [email-verification.schema.json](schemas/email-verification.schema.json).

Codes expire after ten minutes, permit five attempts and have a one-minute resend cooldown. These application rules are separate from TTL cleanup. ProtectedDraft contains encrypted context; CodeHash contains a verification fingerprint. Never display or log codes, encrypted payloads or bearer links. The ExpiresUtc TTL removes expired challenges asynchronously. Account challenges never allocate a bay; guest proof consumption occurs in the same transaction as reservation creation.

## Notification delivery queue

`outbox` fields: `_id`, Recipient, Subject, Body, ReservationId, RequiredStatus, DueUtc, LeaseUntilUtc, LeaseToken, SentUtc, Attempts and Failed. See [notification.schema.json](schemas/notification.schema.json).

DueUtc is when a message becomes eligible. LeaseUntilUtc is the expiry of one worker's temporary claim; LeaseToken identifies that claim so a stale worker cannot complete a newer claim. Attempts counts deliveries and Failed marks exhausted attempts. Sensitive verification/status email bodies use Data Protection. Delivery is at least once; a crash after sending can result in a duplicate. See [OPERATIONS.md](OPERATIONS.md) for individual failed-message replay.

## Validation, indexes and concurrency

Migration 006 installs strict/error validators for the six application collections. It first checks all existing documents and refuses incompatible data before changing validators or backfilling fields. It never deletes malformed bookings, invents contacts or converts ambiguous history. Repair requires an explicit decision against a backup of the intended database. Unknown additional fields remain allowed for forward expansion.

Validators check BSON types, allowed state/service values, non-negative amounts/attempts, positive present guest counts, unique resource arrays, string limits and reservation/block interval ordering. They do not establish resource existence, account existence, contact deliverability, cross-document availability, current policy, coach availability, or real payment verification. Those require controlled application operations. Not every legacy-optional field becomes required.

| Index | Purpose |
| --- | --- |
| Unique reservations `(AccountId, IdempotencyKey)` | One result per owner/submission key |
| Reservations `(Status, StartUtc, EndUtc)` | Active interval queries |
| Reservations `(Status, ReviewDeadlineUtc)` | Pending expiry |
| New reservations `(AccountId, CreatedUtc descending)` | Owned history and latest-contact ordering |
| Sparse reservations `AccessTokenHash` | Guest access lookup |
| Unique accounts `NormalizedEmail` | Prevent duplicate account email |
| Partial unique accounts `GoogleSubject` | Prevent duplicate Google identities |
| TTL verification `ExpiresUtc` | Asynchronous challenge cleanup |
| Outbox `(SentUtc, DueUtc, LeaseUntilUtc)` | Existing delivery lookup |

The new history index matches an existing filter/sort; no production latency improvement is claimed without query-plan/workload measurement. Additional availability/outbox indexing remains measurement-driven.

The catalog guard and resource/Manila-date guards are incremented within transactions before conflicting booking mutations. AccountId remains the authoritative ownership/idempotency scope during this compatibility release; CustomerAccountId is an explicit relationship, not an alternative guest authorization mechanism.

## Migration and rollout

Migrations 001–005 remain unchanged. New migration 006 adds validators, backfills CustomerAccountId only for non-guest ownership scopes where it is missing, and creates the history index. Historical guest challenges and history entries remain untouched. The current shared checkout requires migration IDs 001–007 for /health/ready; migration 007 belongs to the separate guest-recovery change.

Use a coordinated maintenance deployment: stop the old Web instance and its worker, back up the intended database, run the new explicit Migrator, then start the matching new Web build. Old binaries are not guaranteed to understand additive fields; do not mix old writers with new ones. Do not roll back to an old binary after new fields are written without a reviewed compatibility procedure. An interrupted migration can leave some additive changes installed; rerun the same migration after resolving the cause rather than altering migrations or deleting data. See [MIGRATIONS.md](MIGRATIONS.md).

## Retention and production limits

Schema hardening adds no destructive retention job. Email challenges retain their existing TTL; the separate 007 recovery quota also has expiry cleanup. Keep bookings/history and unresolved/failed notification records until an approved retention policy exists. Define sent-email retention separately; sensitive message bodies must not be copied into analytics or public logs. Preserve Data Protection keys for encrypted queued payloads and backup restoration. Guard cleanup needs an explicit correctness review before implementation.

This is stronger existing-feature storage, not a production certification. Live POS, full admin workflows, retention approval, backup/restore rehearsal, load/query-plan testing and deployment security remain outside this change. [reservation.proposed.schema.json](schemas/reservation.proposed.schema.json) is the retained historical design and must not be applied as the current schema.


## Separate guest-recovery extension in the shared checkout (007)

Migration 007 extends only the email-challenge Purpose enum with GuestRecovery, adds an email/start/ID reservation history index with case-insensitive collation, and creates guest_recovery_quotas with expiry cleanup. This is separate existing work, not an admin feature or a change to migration 006.

The current [email-verification.schema.json](schemas/email-verification.schema.json) includes the 007 purpose. The exact 006 baseline is preserved as [email-verification.006.schema.json](schemas/email-verification.006.schema.json). [guest-recovery-quota.schema.json](schemas/guest-recovery-quota.schema.json) describes the additional internal collection: hashed `_id`, IssuedUtc delivery timestamps, ExpiresUtc cleanup time and Version concurrency counter. It limits repeated recovery-message issuance. Its TTL is separate from booking/history retention.

The schema-reference integration test checks the 006 baseline before 007, then the current references after 007. Deploy the matching code/migration sequence; do not manually broaden 006 or run its UpAsync directly after later migrations. The normal runner skips already-applied migrations.

## Public service catalog (008)

`service_catalog` stores `_id`, `Title`, `ImagePath`, `Summary`, `RateText`, `Details`, `BookingService`, `Action`, `ActionLabel`, `Order` and `Active`. The index `ix_service_catalog_active_order_id` supports current visible offerings in stable display order. All fields are typed and required by the new collection validator; BookingService uses the existing six identifiers and Action is Book or Contact. Images are restricted to local media at the Business boundary and actions are constructed as local application links.

RateText is indicative presentation copy, not a payment price source. Editing catalog content does not alter booking durations, coach eligibility, operating hours, allocations, deposit calculations or resource IDs. Active controls the public catalog display; it is not a service scheduling switch. Migration 008 seeds missing rows only and leaves all previous validators/migrations unchanged.


### Releasing schedule blocks

Admins open an active block from the reservation timeline or list, enter a release reason (1–500 characters), and explicitly confirm unblocking. The guarded transition preserves the original block and writes `ReleasedUtc` (UTC), `ReleasedBy`, and `ReleaseReason` together. Repeated or concurrent submissions preserve the first release audit. Released blocks no longer hold capacity in availability, transactional allocation checks, or the active schedule; existing reservations and other blocks remain unaffected. The detail URL continues to show creation and release history. Original creation-form retries cannot reactivate a released block. Legacy closures, including all-bay closures, can be released without inventing missing creation audit.

Apply `012_ReleaseScheduleBlocks` explicitly through Migrator before using this workflow. Its validator permits legacy documents without release fields and requires all three release fields together when present. No block documents are deleted. Partial-interval release, editing and recurring blocks are outside this change.


### Staff attendance and reservation revisions

Migration `013_ReservationAttendance` adds revision 0 to legacy reservations and optional UTC `CheckedInUtc`/`CheckedOutUtc` fields. Existing scheduled `StartUtc`/`EndUtc` remain unchanged. Staff/Admin can check Confirmed bookings in during `[StartUtc, EndUtc)` and check Checked-In bookings out. Early check-out requires a reason and releases remaining capacity by changing status to Completed. Legacy arrivals without a timestamp remain unrecorded; no attendance times are invented.

Attendance events carry command ID/hash and previous/current reservation status. A guarded transaction checks replay before revision, then validates current eligibility and writes status, actual time, revision and one history event atomically. Original-command retries never reopen Completed bookings. All existing reservation writers increment revision for committed changes; staff review forms also require the loaded revision. Confirmed arrivals require resolved cancellation, coaching/deposit prerequisites and a free active allocation excluding their own hold. Follow-up at the earlier of start plus one hour and scheduled end never changes state automatically.

Completed sessions remain visible in the staff day view but do not count as held capacity, block drag ranges or shade whole-shop coverage. Apply migration 013 explicitly through Migrator and restart updated writers together; do not mix old writers that do not maintain revisions. No attendance emails, automatic no-show/checkout, schedule extensions or partial-bay departures are introduced.

### Operating-hours review timestamps

Migration 014 adds optional `ReviewStartsUtc` (UTC date) and `ReviewAlertsUtc` (two UTC dates) to new ordinary online reservations. They freeze the approved review schedule at submission. `ReviewDeadlineUtc` remains the expiry authority. Legacy records and whole-shop deposit timing retain their saved deadlines and legacy alert rules.

### Schedule change proposals

Migration 015 adds an optional `ScheduleChange` object to a reservation and its relevant history events. It stores one current proposal, base revision, command identity/hash, original and proposed intervals/resources, reason and decision state. Completed proposals remain in append-only history when a new proposal replaces the current object. Proposal submission allocates nothing. Schedule approval commits updated allocation, revision, history and notification together.
