---
name: db-migration
description: Authoring, validating, and applying MongoDB migrations for Hole In Hub Reservation Management Subsystem
---

# Hole In Hub Database Migration Skill

This skill guides engineers and agents in creating, validating, and executing schema evolutions and index updates for MongoDB in the Hole In Hub Reservation Management Subsystem.

## 1. Core Principles

1. **Explicit Execution Only**: Migrations must NEVER execute automatically during ASP.NET Core web startup. They run exclusively through the CLI:
   ```powershell
   dotnet run --project HoleInHub.Migrator -- migrate
   ```
2. **Distributed Exclusive Lease**: A migration run requires acquiring an exclusive lease in the `__migration_leases` collection. Concurrent runners will detect the active lease and terminate with exit code 1.
3. **Immutability & Checksums**: Each migration has a unique, ordered identifier (e.g., `001_InitialFoundation`) and a deterministic SHA-256 checksum. If an already applied migration has its code modified, the runner aborts to prevent corrupting database history.
4. **Expand → Migrate → Contract**: Avoid breaking existing running application versions. Introduce new nullable fields/collections first, migrate data, and contract in a subsequent release.

## 2. Migration File Structure

All migrations live in `HoleInHub.Data/Migrations/Definitions/` and implement `IMongoMigration`:

```csharp
namespace HoleInHub.Data.Migrations.Definitions;

public sealed class Migration_002_AddCustomerIndices : IMongoMigration
{
    public string MigrationId => "002_AddCustomerIndices";
    public string Description => "Create unique phone/email indexes on customers collection";

    public string CalculateChecksum()
    {
        return MongoMigrationChecksum.Compute("002_AddCustomerIndices:v1:indexes(phone_1,email_1)");
    }

    public async Task UpAsync(IMongoDatabase database, CancellationToken ct)
    {
        var collection = database.GetCollection<BsonDocument>("customers");
        
        var phoneIndex = new CreateIndexModel<BsonDocument>(
            Builders<BsonDocument>.IndexKeys.Ascending("phoneNumber"),
            new CreateIndexOptions { Unique = true, Sparse = true, Name = "ux_customers_phoneNumber" });

        await collection.Indexes.CreateOneAsync(phoneIndex, cancellationToken: ct);
    }
}
```

## 3. Creating a New Migration: Step-by-Step

1. **Determine the next identifier**: Check existing migrations in `HoleInHub.Data/Migrations/Definitions/` and use the next sequential number.
2. **Implement `IMongoMigration`**: Ensure `UpAsync` is idempotent.
3. **Register Migration**: Add to `HoleInHub.Data/Extensions/ServiceCollectionExtensions.cs`.
4. **Test Migration**: Run integration tests to verify sequential execution and idempotency:
   ```powershell
   dotnet test HoleInHub.Tests.Integration --filter "FullyQualifiedName~MongoMigrationRunnerTests"
   ```
5. **Execute**:
   ```powershell
   dotnet run --project HoleInHub.Migrator -- migrate
   ```
