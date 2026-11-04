# Current MongoDB data model

Based on application records and migrations 001–007, reviewed 2026-11-04. This is the implemented model, not the older proposed schema. See [database dictionary](../DATABASE.md) for full field definitions, validators and indexes.

Types describe application values: date means a BSON UTC date; nullable fields may contain null, while optional fields can be absent in legacy records. Exact requiredness and accepted BSON representations remain in the [schema dictionary](../DATABASE.md) and its JSON references. Property names below use **stored MongoDB names**, including legacy mappings.

## Collection relationships

```mermaid
erDiagram
    ACCOUNTS o|..o{ RESERVATIONS : "CustomerAccountId - optional account link"
    RESERVATIONS }o..|{ RESOURCES : "ResourceIds - selected bays"
    SCHEDULE_BLOCKS }o..o{ RESOURCES : "ResourceIds - empty means all active bays"
    RESERVATIONS o|..o{ OUTBOX : "ReservationId - optional booking link"
    EMAIL_VERIFICATIONS o|..o| RESERVATIONS : "GuestSessionHash - temporary guest proof"

    ACCOUNTS {
        string _id PK
        string Email
        string NormalizedEmail UK
        string FirstName
        string LastName
        string Phone
        string PasswordHash
        string SecurityStamp
        string Version
        boolean EmailConfirmed
        int AccessFailedCount
        date LockoutEndUtc "nullable"
        array Roles
        string GoogleSubject "optional; partial unique index"
    }
    RESERVATIONS {
        string _id PK
        string Reference "display reference; not authorization"
        string AccountId "legacy owner scope: account ID or guest hash"
        string CustomerAccountId "optional actual account ID"
        string Email
        string FirstName
        string LastName
        string Phone
        string Service
        string Status
        date StartUtc
        date EndUtc
        date CreatedUtc
        date ReviewDeadlineUtc
        int PolicyVersion
        array ResourceIds
        object ResourceNames "bay-name snapshot"
        int Attendees
        array Participants "embedded; legacy split counts"
        array History "embedded events"
        string Notes
        boolean PrivacyAccepted
        string GuestSessionHash "nullable; code: EmailVerificationId"
        string AccessTokenHash "nullable guest-link hash"
        string IdempotencyKey "unique with AccountId"
        string PayloadHash "code: SubmissionHash"
        boolean CoachConfirmed
        boolean CancellationRequested
        string PaymentStatus "demo state"
        string PaymentReference "nullable"
        decimal RentalTotal "nullable demo amount"
        decimal DepositAmount "nullable demo amount"
    }
    RESOURCES {
        string _id PK
        string Name
        string Service "Regular or VIP"
        boolean Active
    }
    SCHEDULE_BLOCKS {
        string _id PK
        array ResourceIds
        date StartUtc
        date EndUtc
    }
    EMAIL_VERIFICATIONS {
        string _id PK "hashed verification scope"
        string Email
        string Purpose "GuestBooking, AccountEmail or GuestRecovery; optional legacy"
        string CodeHash
        string ProtectedDraft "encrypted; code: ProtectedContext"
        date SentUtc
        date ExpiresUtc "TTL cleanup"
        int Attempts
        date VerifiedUtc "nullable"
        string ConsumedKey "code: ConsumedBySubmissionKey"
    }
    OUTBOX {
        string _id PK
        string Recipient
        string Subject
        string Body "sensitive payloads encrypted"
        string ReservationId "nullable"
        string RequiredStatus "nullable"
        date DueUtc
        date LeaseUntilUtc "nullable worker claim expiry"
        string LeaseToken "nullable claim ID"
        date SentUtc "nullable"
        int Attempts
        boolean Failed
    }
```

**How to read this diagram:** Accounts can have many bookings; a guest booking has no required account link. Each booking selects one or more bays. A bay can appear in many bookings over time, but protected intervals must not overlap. Outbox messages may relate to one booking or be independent account/verification emails.

The diagram shows logical references checked by application operations, **not MongoDB foreign-key constraints**. PK means document identifier; UK means unique indexed value. The Google subject index applies only to present string values. Reference is not declared unique by the current migrations.

EMAIL_VERIFICATIONS is the friendly diagram name for the physical `guest_verifications` collection. GuestSessionHash links only guest-booking proof, not every account/recovery challenge. A proof is consumed by at most one booking; TTL can later remove it while the booking retains its hash. This is not a permanent required relationship. Email-based recovery lookup is an application rule, not a foreign key or automatic account association.

## Embedded booking records

```mermaid
flowchart LR
    R["reservations document"] -->|"contains History array"| H["History entry<br/>EventType - action<br/>ReservationStatus - resulting lifecycle<br/>Status - legacy display description<br/>AtUtc - action time<br/>Actor - who acted<br/>Reason - explanation"]
    R -->|"contains Participants array"| P["Participant entry - legacy<br/>ResourceId - bay ID<br/>Players - player count<br/>NonPlayers - companion count"]
    R -->|"contains ResourceNames object"| N["Bay-name snapshot<br/>resource ID mapped to display name"]
    R -->|"contains contact fields"| C["Booking contact snapshot<br/>Email, FirstName, LastName, Phone"]
```

These are fields inside a booking, not separate collections. New total-guest bookings use Attendees and leave Participants empty. Legacy history may omit EventType and ReservationStatus; old descriptions remain intact. Profile changes do not rewrite the booking's contact snapshot.

## Internal supporting collections

```mermaid
erDiagram
    ALLOCATION_GUARDS {
        string _id PK "catalog or resource ID plus Manila date"
        number version "transaction contention counter"
    }
    GUEST_RECOVERY_QUOTAS {
        string _id PK "hashed email"
        date_array IssuedUtc "recovery email issuance times"
        date ExpiresUtc "TTL cleanup date"
        long Version "concurrent update counter"
    }
    MIGRATIONS {
        ObjectId _id PK
        string migrationId UK "applied upgrade identifier"
        string description "upgrade purpose"
        string checksum "authored migration fingerprint"
        date appliedAtUtc "successful application time"
        long executionDurationMs "duration in milliseconds"
    }
    MIGRATION_LEASES {
        string _id PK "exclusive migration lease identifier"
        string owner "current runner identifier"
        date acquiredAtUtc "claim acquisition time"
        date expiresAtUtc "claim expiry time"
        date lastHeartbeatAtUtc "last renewal time"
    }
    SYSTEM_HEARTBEATS {
        string _id PK
        date lastPingAtUtc "foundation heartbeat timestamp"
        string environment "foundation initialization label"
    }
```

These five collections have no stored foreign-key relationship to the business collections. Booking transactions update allocation_guards; recovery email issuance updates guest_recovery_quotas; the explicit Migrator uses the other three. Counters use numeric BSON types; recovery Version is a C# long. The migration document `_id` is a BSON ObjectId even though the C# property is a string.

## Physical collection names

| Diagram name | MongoDB collection |
| --- | --- |
| ACCOUNTS | accounts |
| RESERVATIONS | reservations |
| RESOURCES | resources |
| SCHEDULE_BLOCKS | schedule_blocks |
| EMAIL_VERIFICATIONS | guest_verifications |
| OUTBOX | outbox |
| ALLOCATION_GUARDS | allocation_guards |
| GUEST_RECOVERY_QUOTAS | guest_recovery_quotas |
| MIGRATIONS | __migrations |
| MIGRATION_LEASES | __migration_leases |
| SYSTEM_HEARTBEATS | system_heartbeats |

## Important constraints

- All instants are UTC; venue calendar dates use Asia/Manila. Reservation/block StartUtc must precede EndUtc. Intervals use `[start, end)`.
- Unique `(AccountId, IdempotencyKey)` prevents duplicate submission effects; PayloadHash checks that repeated submissions have the same details.
- CustomerAccountId is optional. AccountId remains the legacy guest/account ownership scope, not an unconditional reference to accounts.
- Booking conflicts and all-or-nothing multi-bay allocation are enforced through persisted guards and transactions, not relationship arrows alone.
- The current model has no separate customers, allocations, coaching_sessions, payments or receipts collections. Payment fields represent demonstration state only.
- Retained schema-compatible attendance states do not establish a completed admin attendance workflow.

The diagram is based on source, not a live database inspection. It does not certify production deployment or add planned admin/POS structures.
