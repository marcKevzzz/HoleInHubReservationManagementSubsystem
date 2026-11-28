using HoleInHub.Data.Website;
using MongoDB.Bson;
using MongoDB.Driver;

namespace HoleInHub.Data.Migrations.Definitions;

public sealed class Migration_032_WebsiteContent : IMongoMigration
{
    public string MigrationId => "032_WebsiteContent";
    public string Description => "Create the website content collections and seed the existing public content.";
    public string CalculateChecksum() => MongoMigrationChecksum.Compute("032_WebsiteContent:v1:website-content-and-images");

    public async Task UpAsync(IMongoDatabase database, CancellationToken ct)
    {
        var names = await (await database.ListCollectionNamesAsync(cancellationToken: ct)).ToListAsync(ct);
        foreach (var name in new[] { WebsiteContentPersistence.ContentCollection, WebsiteContentPersistence.ImageCollection })
        {
            if (!names.Contains(name))
            {
                await database.CreateCollectionAsync(name, cancellationToken: ct);
            }
        }

        var content = database.GetCollection<WebsiteContentSnapshot>(WebsiteContentPersistence.ContentCollection);
        if (!await content.Find(x => x.Id == "website").AnyAsync(ct))
        {
            await content.InsertOneAsync(new WebsiteContentSnapshot("website", 0, BsonDocument.Parse(WebsiteContentSeed.Json)), cancellationToken: ct);
        }
    }
}
