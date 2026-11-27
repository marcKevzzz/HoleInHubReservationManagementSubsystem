using HoleInHub.Business.Services;
using HoleInHub.Data.Integrations;
using HoleInHub.Tests.Integration.Fixtures;
using Xunit;

namespace HoleInHub.Tests.Integration;

[Trait("Category", "Integration")]
public sealed class IntegrationKeyIssuanceTests(MongoDbFixture fixture) : IClassFixture<MongoDbFixture>
{
    [Fact]
    public async Task ConcurrentSubmissionsIssueOneKeyAndReplayCannotRestoreADeletedKey()
    {
        var access = new IntegrationAccess(new IntegrationStore(fixture.CreateDbContext()), TimeProvider.System);
        var submission = Guid.NewGuid();
        Task<string?> Submit() => access.IssueForSubmissionAsync("admin", submission, "POS", 30,
            [IntegrationAccess.ReadServices], default);
        var results = await Task.WhenAll(Submit(), Submit());
        var secret = Assert.Single(results.OfType<string>());
        var key = Assert.Single(await access.KeysAsync(default));
        Assert.Equal(200, (await access.CheckAsync(secret, IntegrationAccess.ReadServices, default)).Status);
        await access.DeleteAsync(key.Id, default);
        Assert.Null(await Submit());
        Assert.Empty(await access.KeysAsync(default));
        Assert.Equal(401, (await access.CheckAsync(secret, IntegrationAccess.ReadServices, default)).Status);
        Assert.NotNull(await access.IssueForSubmissionAsync("admin", Guid.NewGuid(), "POS", 30,
            [IntegrationAccess.ReadServices], default));
    }
}
