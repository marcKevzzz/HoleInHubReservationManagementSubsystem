using HoleInHub.Data;
using HoleInHub.Data.Accounts;
using HoleInHub.Data.Customers;
using HoleInHub.Data.Reservations;
using HoleInHub.Data.Services;
using HoleInHub.Data.Settings;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using Xunit;

namespace HoleInHub.Tests.Unit;

public sealed class NamedIdentifierSerializationTests
{
    [Fact]
    public void InsertsAndReplacementsWriteNamedIdsForNewAndLegacyRecords()
    {
        Check(new ReservationRecord { Id = "internal-key", Reference = "HIH-123" }, "reservation_id", "HIH-123");
        Check(new CustomerProfileRecord { Id = "cp_123" }, "customer_id", "CUS-029528D9474B9A03");
        Check(new AccountRecord { Id = "account-123" }, "account_id", "ACC-725A2FD11A2B5251");
        Check(new ResourceRecord { Id = "bay-2" }, "resource_id", "bay-2");
        Check(new ServiceCatalogRecord { Id = "Regular" }, "service_id", "SVC-B455784AB5307277");
        Check(new ScheduleBlock { Id = "admin-block:123" }, "block_id", "BLK-55D83B86D79B708A");
        Check(new ServiceImageRecord { Id = "image-123" }, "service_image_id", "IMG-5AE91B282EDFB7D3");
        Check(new SystemSettingsRecord { Id = "venue" }, "settings_id", "SET-E622DA235A569773");
    }

    [Fact]
    public void HashesExactUtf8Bytes() =>
        Assert.Equal("CUS-9317057B44F441DA", BusinessIdentifier.Format("CUS", "café-客户"));

    private static void Check<T>(T record, string field, string expected)
    {
        var document = record.ToBsonDocument();
        Assert.Equal(expected, document[field].AsString);
        Assert.Equal(document, BsonSerializer.Deserialize<T>(document).ToBsonDocument());

        var legacy = document.DeepClone().AsBsonDocument;
        legacy[field] = field == "reservation_id" ? document["Reference"] : document["_id"];
        Assert.Equal(document, BsonSerializer.Deserialize<T>(legacy).ToBsonDocument());
        legacy.Remove(field);
        Assert.Equal(document, BsonSerializer.Deserialize<T>(legacy).ToBsonDocument());
    }
}
