using HoleInHub.Data.Context;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;
namespace HoleInHub.Data.Settings;
public sealed class SystemSettingsRecord
{
    [BsonId] public string Id { get; set; } = "venue";
    public long Revision { get; set; }
    public string CommandId { get; set; } = "";
    public string CommandHash { get; set; } = "";
    public bool OnlineEnabled { get; set; }
    public bool DurationsApproved { get; set; }
    public int ReviewStartMinutes { get; set; }
    public int ReviewEndMinutes { get; set; } = 1440;
    public int[] ReviewDays { get; set; } = [];
    public string[] StaffEmails { get; set; } = [];
    public string VenueName { get; set; } = "Hole In Hub Golf & Lounge";
    public string ContactEmail { get; set; } = "";
    public string ContactPhone { get; set; } = "";
    public string BookingPolicy { get; set; } = "";
    public string PrivacyPolicy { get; set; } = "";
    public string VenueRules { get; set; } = "";
    public bool PoliciesPublished { get; set; }
    public string UpdatedBy { get; set; } = "";
    public DateTime? UpdatedUtc { get; set; }
}
public sealed class SystemSettingsPersistence(IMongoDbContext context)
{
    private IMongoCollection<SystemSettingsRecord> Records => context.Database.GetCollection<SystemSettingsRecord>("system_settings");
    public Task<SystemSettingsRecord?> GetAsync(CancellationToken ct) => Records.Find(x => x.Id == "venue").FirstOrDefaultAsync(ct)!;
    public async Task SaveAsync(SystemSettingsRecord record, long expectedRevision, CancellationToken ct)
    {
        using var collections = await context.Database.ListCollectionNamesAsync(cancellationToken: ct);
        if (!(await collections.ToListAsync(ct)).Contains("system_settings")) { throw new InvalidOperationException("Run migration 019_SystemSettings before saving settings."); }
        var prior = await GetAsync(ct);
        if (prior?.CommandId == record.CommandId)
        {
            if (prior.CommandHash != record.CommandHash) { throw new InvalidOperationException("This save was already received with different values. Reload settings."); }
            return;
        }
        record.Revision = expectedRevision + 1;
        if (expectedRevision == -1 && prior is null)
        {
            try { await Records.InsertOneAsync(record, cancellationToken: ct); }
            catch (MongoWriteException error) when (error.WriteError.Category == ServerErrorCategory.DuplicateKey)
            { throw new InvalidOperationException("Settings changed. Reload before saving."); }
        }
        else
        {
            var result = await Records.ReplaceOneAsync(x => x.Id == "venue" && x.Revision == expectedRevision, record, cancellationToken: ct);
            if (result.ModifiedCount != 1) { throw new InvalidOperationException("Settings changed. Reload before saving."); }
        }
    }
}
