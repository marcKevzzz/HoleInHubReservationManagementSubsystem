using MongoDB.Bson;
using MongoDB.Driver;
namespace HoleInHub.Data.Migrations.Definitions;

public sealed class Migration_019_SystemSettings : IMongoMigration
{
    public string MigrationId => "019_SystemSettings";
    public string Description => "Add versioned operational settings without changing deployed defaults";
    public string CalculateChecksum() => MongoMigrationChecksum.Compute(MigrationId + ":v1:singleton:versioned:policy-publication");
    public async Task UpAsync(IMongoDatabase database, CancellationToken ct)
    {
        var schema = BsonDocument.Parse("{bsonType:'object',required:['_id','Revision','CommandId','CommandHash','OnlineEnabled','DurationsApproved','ReviewStartMinutes','ReviewEndMinutes','ReviewDays','StaffEmails','VenueName','PoliciesPublished','UpdatedBy','UpdatedUtc'],properties:{_id:{enum:['venue']},Revision:{bsonType:'long',minimum:0},CommandId:{bsonType:'string'},CommandHash:{bsonType:'string'},OnlineEnabled:{bsonType:'bool'},DurationsApproved:{bsonType:'bool'},ReviewStartMinutes:{bsonType:'int',minimum:0,maximum:1439},ReviewEndMinutes:{bsonType:'int',minimum:1,maximum:1440},ReviewDays:{bsonType:'array',maxItems:7,uniqueItems:true,items:{bsonType:'int',minimum:0,maximum:6}},StaffEmails:{bsonType:'array',maxItems:20,items:{bsonType:'string',maxLength:254}},VenueName:{bsonType:'string',minLength:1,maxLength:120},ContactEmail:{bsonType:'string',maxLength:254},ContactPhone:{bsonType:'string',maxLength:40},BookingPolicy:{bsonType:'string',maxLength:12000},PrivacyPolicy:{bsonType:'string',maxLength:12000},VenueRules:{bsonType:'string',maxLength:12000},PoliciesPublished:{bsonType:'bool'},UpdatedBy:{bsonType:'string'},UpdatedUtc:{bsonType:'date'}}}");
        var validator = new BsonDocument("$jsonSchema", schema);
        using var cursor = await database.ListCollectionNamesAsync(cancellationToken: ct);
        if (!(await cursor.ToListAsync(ct)).Contains("system_settings"))
        { await database.CreateCollectionAsync("system_settings", new CreateCollectionOptions<BsonDocument> { Validator = validator }, ct); }
        else
        { await database.RunCommandAsync<BsonDocument>(new BsonDocument { { "collMod", "system_settings" }, { "validator", validator }, { "validationLevel", "strict" } }, cancellationToken: ct); }
    }
}
