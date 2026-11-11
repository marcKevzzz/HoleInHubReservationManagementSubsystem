using HoleInHub.Business.Customers;
using Xunit;
namespace HoleInHub.Tests.Unit;

public sealed class CustomerMergeMatchTests
{
    private static CustomerProfile Profile(string first = "", string last = "", string email = "", string phone = "") => new("id", "booking", first, last, email, phone, "", [], [], 1, 1, 1);
    [Fact]
    public void EmptyOrUnrelatedDetailsAreNotMatches()
    {
        Assert.False(CustomerProfileService.HaveMatchingDetails(Profile(), Profile()));
        Assert.False(CustomerProfileService.HaveMatchingDetails(Profile("Alex", "Santos", "a@test", "09175550123"), Profile("Alex", "Reyes", "b@test", "09995550123")));
    }
    [Fact]
    public void EachSharedDetailCanMatchIndependently()
    {
        Assert.True(CustomerProfileService.HaveMatchingDetails(Profile("Alex", "Santos"), Profile("Santos", "Alex")));
        Assert.True(CustomerProfileService.HaveMatchingDetails(Profile(email: " A@test "), Profile(email: "a@TEST")));
        Assert.True(CustomerProfileService.HaveMatchingDetails(Profile(phone: "09175550123"), Profile(phone: "+63 917 555 0123")));
    }
    [Fact]
    public void TwoDistinctNameWordsMatchAcrossOrderAndPunctuation()
    {
        Assert.True(CustomerProfileService.HaveMatchingDetails(Profile("Marc Kevin", "Del Mundo"), Profile("Mundo, Marc", "F.")));
        Assert.False(CustomerProfileService.HaveMatchingDetails(Profile("Alex Alex", "Santos"), Profile("Alex Alex", "Reyes")));
        Assert.False(CustomerProfileService.HaveMatchingDetails(Profile("Alex"), Profile("Alex")));
        var a = Profile("Alex", "Santos") with { Variants = [new("Jaime", "Reyes", "", "", "booking", default)] };
        Assert.False(CustomerProfileService.HaveMatchingDetails(a, Profile("Alex", "Reyes")));
    }

    [Fact]
    public void RetainedContactCanMatchAnotherProfile()
    {
        var a = Profile() with { Variants = [new("", "", "old@test", "", "booking", default)] };
        Assert.True(CustomerProfileService.HaveMatchingDetails(a, Profile(email: "old@test")));
    }
}
