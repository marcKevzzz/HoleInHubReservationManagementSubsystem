using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace HoleInHub.Data.Website;

internal sealed record WebsiteContentSnapshot([property: BsonId] string Id, long Revision, BsonDocument Content);

public sealed record WebsiteImage([property: BsonId] string Id, string ContentType, byte[] Bytes);
