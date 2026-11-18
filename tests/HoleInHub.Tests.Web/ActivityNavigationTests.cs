using HoleInHub.Web.Navigation;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace HoleInHub.Tests.Web;

public sealed class ActivityNavigationTests
{
    [Theory]
    [InlineData("/staff/activity")]
    [InlineData("/staff/activity?category=reservations&search=Alex%20Santos&cursor=abc_123")]
    [InlineData("/staff/activity/reservation:abc_123-0?category=customers&from=2026-11-01")]
    [InlineData("/staff/activity/reservation%3Aabc_123-0?search=%E2%80%99")]
    public void PreservesActivityListAndEventContext(string value) =>
        Assert.Equal(value, ActivityNavigation.SafeReturnUrl(value));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("https://evil.test/staff/activity")]
    [InlineData("//evil.test/staff/activity")]
    [InlineData("/\\evil.test/staff/activity")]
    [InlineData("/staff/customers")]
    [InlineData("/staff/activity-other")]
    [InlineData("/staff/activity/../customers")]
    [InlineData("/staff/activity/%2e%2e")]
    [InlineData("/staff/activity/%252f%252fevil.test")]
    [InlineData("/staff/activity/%2f%2fevil.test")]
    [InlineData("/staff/activity/event/extra")]
    [InlineData("/staff/activity?search=%0d%0aLocation%3Aevil")]
    [InlineData("/staff/activity?search=%250d%250a")]
    [InlineData("/staff/activity?search=abc\n")]
    [InlineData("/staff/activity#https://evil.test")]
    public void RejectsUnrelatedOrAmbiguousReturnUrls(string? value) =>
        Assert.Null(ActivityNavigation.SafeReturnUrl(value));

    [Theory]
    [InlineData("?activityReturn=%2Fstaff%2Factivity%3Fcategory%3Dcustomers", "/staff/activity?category=customers")]
    [InlineData("?activityReturn=%2Fstaff%2Factivity&activityReturn=%2Fstaff%2Factivity", null)]
    [InlineData("?activityReturn=https%3A%2F%2Fevil.test", null)]
    public void RequestContextRequiresOneSafeReturnValue(string query, string? expected)
    {
        var context = new DefaultHttpContext();
        context.Request.QueryString = new QueryString(query);
        Assert.Equal(expected, ActivityNavigation.FromRequest(context.Request));
    }

    [Fact]
    public void RejectsOversizedNavigationContext() =>
        Assert.Null(ActivityNavigation.SafeReturnUrl("/staff/activity?search=" + new string('a', 4096)));
}
