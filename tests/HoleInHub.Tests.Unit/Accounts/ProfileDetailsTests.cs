using HoleInHub.Business.Accounts;
using HoleInHub.Data.Accounts;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using Xunit;

namespace HoleInHub.Tests.Unit.Accounts;

public sealed class ProfileDetailsTests
{
    [Theory]
    [InlineData(null, null, true)]
    [InlineData(1, "123 Main Street", true)]
    [InlineData(120, "서울시 강남구", true)]
    [InlineData(0, "123 Main Street", false)]
    [InlineData(121, "123 Main Street", false)]
    [InlineData(25, "AB", false)]
    [InlineData(25, "A\0B", false)]
    public void OptionalDetailsHaveBoundedValidation(int? age, string? address, bool expected) =>
        Assert.Equal(expected, ContactRules.IsProfileDetails(age, address));

    [Fact]
    public void NewDetailsRoundTripAndLegacyAccountsNeedNoInventedValues()
    {
        var record = new AccountRecord { Age = 25, Address = "50 Holy Spirit Drive" };
        var restored = BsonSerializer.Deserialize<AccountRecord>(record.ToBson());
        Assert.Equal(record.Age, restored.Age);
        Assert.Equal(record.Address, restored.Address);
        var legacy = new AccountRecord().ToBsonDocument();
        Assert.False(legacy.Contains("Age"));
        Assert.False(legacy.Contains("Address"));
        Assert.Null(BsonSerializer.Deserialize<AccountRecord>(legacy).Age);
    }
}
