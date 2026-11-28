using HoleInHub.Data.Context;
using MongoDB.Bson;
using MongoDB.Driver;

namespace HoleInHub.Data.Website;

public sealed class WebsiteContentPersistence(IMongoDbContext context)
{
    public const string ContentCollection = "website_content";
    public const string ImageCollection = "website_content_images";
    private IMongoCollection<WebsiteContentSnapshot> Content => context.Database.GetCollection<WebsiteContentSnapshot>(ContentCollection);
    private IMongoCollection<WebsiteImage> Images => context.Database.GetCollection<WebsiteImage>(ImageCollection);

    public async Task<string?> ReadAsync(CancellationToken ct)
    {
        var snapshot = await Content.Find(x => x.Id == "website").SingleOrDefaultAsync(ct);
        return snapshot?.Content.ToJson();
    }

    public async Task<WebsiteImage?> ReadImageAsync(string id, CancellationToken ct) =>
        await Images.Find(x => x.Id == id).SingleOrDefaultAsync(ct);

    public async Task<bool> SaveAsync(long revision, string content, WebsiteImage? upload, string[] obsoleteImages, CancellationToken ct)
    {
        var next = new WebsiteContentSnapshot("website", revision, BsonDocument.Parse(content));
        using var session = await context.Client.StartSessionAsync(cancellationToken: ct);
        return await session.WithTransactionAsync(async (transaction, token) =>
        {
            var result = await Content.ReplaceOneAsync(transaction,
                x => x.Id == next.Id && x.Revision == next.Revision - 1, next, cancellationToken: token);
            if (result.MatchedCount == 0)
            {
                return false;
            }

            if (upload is not null)
            {
                await Images.InsertOneAsync(transaction, upload, cancellationToken: token);
            }

            if (obsoleteImages.Length != 0)
            {
                await Images.DeleteManyAsync(transaction, Builders<WebsiteImage>.Filter.In(x => x.Id, obsoleteImages),
                    cancellationToken: token);
            }

            return true;
        }, cancellationToken: ct);
    }
}
