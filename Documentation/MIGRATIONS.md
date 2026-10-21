# MongoDB Migration & Schema Evolution Guide

In NoSQL systems, controlled schema evolution is critical for preventing data corruption and runtime errors. Hole In Hub implements a custom, deterministic, lease-guarded migration engine.

---

## 1. Architecture & Execution Model

- **Decoupled from Web Startup**: Migrations NEVER run during ASP.NET Core startup. This prevents race conditions, multi-pod lease storms, and startup timeouts.
- **Distributed Lease Locking**: The runner atomically claims an exclusive lock in the `__migration_leases` collection via MongoDB's `FindOneAndUpdate` with lease heartbeats.
- **SHA-256 Tamper Detection**: Each migration is hashed at authoring time. If an existing migration record in `__migrations` has a different checksum from the code, execution immediately aborts.
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
- `MongoDb__ConnectionString`: Connection URI (must include `?replicaSet=...`)
- `MongoDb__DatabaseName`: Target database name

---

## 3. How to Author a New Migration

1. Create a new class in `HoleInHub.Data/Migrations/Definitions/`:
   ```csharp
   namespace HoleInHub.Data.Migrations.Definitions;

   public sealed class Migration_002_AddCustomersCollection : IMongoMigration
   {
       public string MigrationId => "002_AddCustomersCollection";
       public string Description => "Create customers collection with email/phone indexes";

       public string CalculateChecksum()
       {
           return MongoMigrationChecksum.Compute("002_AddCustomersCollection:v1:indexes(phone_1,email_1)");
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
   services.AddSingleton<IMongoMigration, Migrations.Definitions.Migration_002_AddCustomersCollection>();
   ```
3. Run the integration tests:
   ```powershell
   dotnet test HoleInHub.Tests.Integration --filter "FullyQualifiedName~MongoMigrationRunnerTests"
   ```
