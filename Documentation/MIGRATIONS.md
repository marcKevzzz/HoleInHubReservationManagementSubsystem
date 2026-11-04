# MongoDB Migration & Schema Evolution Guide

In NoSQL systems, controlled schema evolution is critical for preventing data corruption and runtime errors. Hole In Hub implements a custom, deterministic, lease-guarded migration engine.

---

## 1. Architecture & Execution Model

- **Decoupled from Web Startup**: Migrations NEVER run during ASP.NET Core startup. This prevents race conditions, multi-pod lease storms, and startup timeouts.
- **Distributed Lease Locking**: The runner atomically claims an exclusive lock in the `__migration_leases` collection via MongoDB's `FindOneAndUpdate` with lease heartbeats.
- **SHA-256 Tamper Detection**: Each migration exposes a deterministic checksum calculated from its authored checksum input. If an existing record has a different checksum, execution aborts. The current helper hashes explicit strings, not the complete C# source; unchanged checksum input cannot detect arbitrary source edits. Applied migrations must remain immutable.
- **Expand → Migrate → Contract**: Zero-downtime deployment philosophy. New fields must be optional or backward compatible.

---

## 2. Running Migrations

Execute the standalone console tool:
```powershell
dotnet run --project HoleInHub.Migrator -- migrate
```

### Command Flags and Configuration
Only the exact `migrate` command changes the database. No arguments, `--help`, and `-h` print usage and exit successfully without loading database configuration. Unknown commands or extra arguments print usage and exit with code 2. Migration failures exit with code 1. Configure overrides through environment variables, not CLI flags.

Migration services are registered by `AddHoleInHubMigrations()` in the CLI only; Web's data registration does not expose a migration runner.

The runner renews its lease every third of the lease duration throughout execution and bounds each background renewal to that interval. Renewal failure cancels the migration token and fails the run. Ownership is rechecked before recording an applied migration. Migrations must pass the cancellation token to every database operation and stop promptly when cancelled. Cancellation cannot undo an already accepted server operation (such as an index build); migrations must remain idempotent, and an operator must investigate an interrupted run before retrying.

Configuration is loaded from `HoleInHub.Migrator/appsettings.json` or overridden via environment variables:
- `MongoDb__ConnectionString`: Connection URI (local replica set includes `replicaSet=rs0`; Atlas uses its SRV URI without a local override)
- `MongoDb__DatabaseName`: Target database name

### Launch Environment

Follow [Developer Onboarding](ONBOARDING.md#configuration-scope-and-secrets) when selecting local MongoDB or Atlas. Migrator loads `appsettings.json` and environment variables; `.env` and Web user secrets are not loaded. Set the target database and separate migration credential in the terminal running the CLI, or use an appropriate deployment secret store. Confirm the intended target before running migrations; persistent Atlas variables override local JSON defaults. Keep integration tests on their isolated local databases.

---

## 3. How to Author a New Migration

1. Create a new class in `HoleInHub.Data/Migrations/Definitions/`:
   ```csharp
   namespace HoleInHub.Data.Migrations.Definitions;

   public sealed class Migration_006_ExampleCustomers : IMongoMigration
   {
       public string MigrationId => "006_ExampleCustomers";
       public string Description => "Create customers collection with email/phone indexes";

       public string CalculateChecksum()
       {
           return MongoMigrationChecksum.Compute("006_ExampleCustomers:v1:indexes(phone_1,email_1)");
       }

       public async Task UpAsync(IMongoDatabase database, CancellationToken ct)
       {
           var collection = database.GetCollection<BsonDocument>("customers");
           var phoneIndex = new CreateIndexModel<BsonDocument>(
               Builders<BsonDocument>.IndexKeys.Ascending("phoneNumber"),
               new CreateIndexOptions { Unique = true, Sparse = true });

           await collection.Indexes.CreateOneAsync(phoneIndex, cancellationToken: ct);
       }
   }
   ```
2. Register the class inside `AddHoleInHubMigrations` in `HoleInHub.Data/Extensions/ServiceCollectionExtensions.cs`:
   ```csharp
   services.AddSingleton<IMongoMigration, Migrations.Definitions.Migration_006_ExampleCustomers>();
   ```
3. Run the integration tests:
   ```powershell
   $env:MongoDb__ConnectionString = 'mongodb://localhost:27017/?replicaSet=rs0'
   dotnet test tests/HoleInHub.Tests.Integration --filter "FullyQualifiedName~MongoMigrationRunnerTests"
   ```

## Current migration inventory

As of 2026-11-04 the CLI registers 001_InitialFoundation, 002_Reservations, 003_Accounts, 004_BookingProcess, 005_GoogleAccounts, 006_SchemaHardening, 007_GuestReservationAccess and 008_ServiceCatalog. `/health/ready` requires all eight IDs even when Google is disabled. See [DATABASE.md](DATABASE.md) for collections, indexes and the typed validators installed by 006 and the historical proposed schema.

Migration 004 adds verification cleanup/access indexes and revises the active catalog while preserving resource IDs/history. It refuses catalog changes when future active original Bay 1 or whole-shop allocations would make the change unsafe. Resolve those bookings through an authorized operational process before retrying; do not delete history or edit an applied migration. New total-count bookings do not invent per-bay player/non-player splits for legacy records.

## 005_GoogleAccounts

Adds optional `FirstName`, `LastName`, `Phone` and `GoogleSubject` account fields through document defaults, with no invented/backfilled contact values. Creates `ux_accounts_google_subject`, a partial unique index covering string Google subjects only. Legacy accounts without a Google identity remain outside the index. The existing unique normalized email index remains in force; callbacks never automatically link matching-email accounts.

Apply only through `dotnet run --project HoleInHub.Migrator -- migrate` after selecting the intended database and migration credentials. Do not enable Google before this migration succeeds. Earlier migrations are unchanged. Rollback: disable `Authentication:Google:Enabled`; preserve account documents/index and re-enable after resolving configuration. Passwordless Google customers require Google or verified email password recovery to regain access.


## 006_SchemaHardening: existing-feature validation

This migration adds typed strict/error validators to reservations, accounts, resources, schedule_blocks, guest_verifications and outbox, checks reservation/block start-before-end, adds the owner/creation-time history index, and backfills CustomerAccountId only for non-guest AccountId scopes where the new field is missing. It does not rename collections, alter migrations 001–005, rewrite legacy history, invent guest accounts, enable payments or implement admin features.

All collections are preflighted before applying validators or backfilling documents. Incompatible legacy data causes an actionable failure naming the collection, without printing customer values or changing that data. Repair the identified data through an authorized maintenance process after reviewing a backup. Do not relax the migration, delete bookings or fabricate contact/participant values merely to pass validation.

Use a coordinated maintenance deployment: stop old Web instances and their background workers, take a backup, choose the intended database/credentials, run the new explicit Migrator, then start the matching new Web build and check readiness. Old binaries may reject additive CustomerAccountId/Purpose/history fields; mixed old/new writers and an immediate binary-only rollback are unsupported. Test any rollback against a restored backup; preserve historical records and migration checksums.

The migration is idempotent, but its multiple commands are not one all-or-nothing transaction. A permission/index failure or interruption can leave additive changes installed before its history record is written. Resolve the cause and rerun the unchanged CLI under its lease. Do not run old applications while repairing/retrying. Readiness checks the presence of 006, not a complete revalidation of every stored document.

Published JSON files under [schemas](schemas/) are checked against the actual installed validators by integration tests. The reservation and schedule-block schema bodies have companion full validator files containing their interval expressions. Other cross-document/business rules remain in controlled application operations. The richer historical reservation proposal remains explicitly separate.


## Separate 007_GuestReservationAccess extension

The shared checkout also registers a guest-recovery migration after 006. It adds GuestRecovery as an email-challenge purpose, a case-insensitive email/history index and guest_recovery_quotas with TTL cleanup. This is separate feature work. Current readiness requires 001–008, and the normal CLI applies all pending registered migrations. See DATABASE.md for the current versus 006-baseline schema references. Keep the coordinated rollout and never re-execute older UpAsync implementations manually after newer migrations.

## 008_ServiceCatalog: public service offerings

Creates the typed `service_catalog` collection, its active/order/ID index and six offerings copied from the existing booking wizard's indicative rates and schedule descriptions. Seeds use setOnInsert: reruns never overwrite edited offerings. Whole-shop action remains contact-only. This migration leaves bays, allocation guards, account/reservation contact values and earlier migrations unchanged. Execute through the CLI under the normal distributed lease; readiness requires all eight registered migrations. For interrupted or permission-failed runs, resolve the cause and rerun the unchanged CLI.


## 009_RecoveryQuotaValidation

Installs the existing recovery-quota schema on collections that predate 007. Migration 007 creates its validator only when creating the collection; this corrective migration preflights existing records and explicitly applies strict/error validation with collMod. It does not delete or rewrite records. Applied migrations 001–008 remain unchanged. Discovered and verified during the Atlas synchronization on 2026-11-04.
