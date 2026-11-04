using HoleInHub.Data.Reservations;
using MongoDB.Bson;
using MongoDB.Driver;

namespace HoleInHub.Data.Migrations.Definitions;

public sealed class Migration_004_BookingProcess : IMongoMigration
{
    public string MigrationId => "004_BookingProcess";
    public string Description => "Guest verification, booking participants and revised active bay catalog";
    public string CalculateChecksum() => MongoMigrationChecksum.Compute("004_BookingProcess:v1");
    public async Task UpAsync(IMongoDatabase database, CancellationToken ct)
    {
        var names = await (await database.ListCollectionNamesAsync(cancellationToken: ct)).ToListAsync(ct);
        if (!names.Contains("guest_verifications"))
        {
            await database.CreateCollectionAsync("guest_verifications", cancellationToken: ct);
        }

        await database.GetCollection<GuestVerificationRecord>("guest_verifications").Indexes.CreateOneAsync(
            new CreateIndexModel<GuestVerificationRecord>(Builders<GuestVerificationRecord>.IndexKeys.Ascending(x => x.ExpiresUtc),
                new CreateIndexOptions { ExpireAfter = TimeSpan.Zero, Name = "ttl_guest_verification" }), cancellationToken: ct);
        await database.GetCollection<ReservationRecord>("reservations").Indexes.CreateOneAsync(
            new CreateIndexModel<ReservationRecord>(Builders<ReservationRecord>.IndexKeys.Ascending(x => x.AccessTokenHash),
                new CreateIndexOptions { Sparse = true, Name = "ix_booking_access" }), cancellationToken: ct);
        using var session = await database.Client.StartSessionAsync(cancellationToken: ct);
        await session.WithTransactionAsync(async (s, token) =>
        {
            await database.GetCollection<BsonDocument>("allocation_guards").UpdateOneAsync(s, new BsonDocument("_id", "catalog"),
                new BsonDocument("$inc", new BsonDocument("version", 1)), new UpdateOptions { IsUpsert = true }, token);
            var resources = database.GetCollection<ResourceRecord>("resources");
            if (await resources.Find(s, x => x.Id == "bay-1" && x.Active != false).AnyAsync(token) &&
                await database.GetCollection<ReservationRecord>("reservations").Find(s, Builders<ReservationRecord>.Filter.Where(x => (x.Status == "Pending" || x.Status == "Confirmed" || x.Status == "Checked-In") && x.EndUtc > DateTime.UtcNow) & (Builders<ReservationRecord>.Filter.Eq(x => x.Service, "WholeShop") | Builders<ReservationRecord>.Filter.AnyEq(x => x.ResourceIds, "bay-1"))).AnyAsync(token))
            {
                throw new InvalidOperationException("Resolve active whole-shop or retired-bay allocations before changing the catalog.");
            }

            await resources.UpdateOneAsync(s, x => x.Id == "bay-1", Builders<ResourceRecord>.Update.Set(x => x.Active, false), cancellationToken: token);
            for (var i = 2; i <= 6; i++)
            {
                await resources.UpdateOneAsync(s, x => x.Id == $"bay-{i}", Builders<ResourceRecord>.Update.Set(x => x.Name, $"Bay {i - 1}").Set(x => x.Active, true), cancellationToken: token);
            }

            for (var i = 7; i <= 8; i++)
            {
                await resources.UpdateOneAsync(s, x => x.Id == $"vip-{i}", Builders<ResourceRecord>.Update.Set(x => x.Name, $"VIP {i - 6}").Set(x => x.Active, true), cancellationToken: token);
            }

            return true;
        }, new TransactionOptions(readConcern: ReadConcern.Snapshot, writeConcern: WriteConcern.WMajority), ct);
    }
}
