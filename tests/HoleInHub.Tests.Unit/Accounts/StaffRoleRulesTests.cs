
using HoleInHub.Business.Accounts;
using Xunit;

namespace HoleInHub.Tests.Unit.Accounts;

public sealed class StaffRoleRulesTests
{
    [Theory]
    [InlineData("Owner", "Reason")]
    [InlineData("admin", "Reason")]
    [InlineData("Staff", "   ")]
    public async Task InvalidInputIsRejectedBeforeReadingTheDatabase(string role, string reason)
    {
        var service = new AccountService(null!, TimeProvider.System);
        Assert.Equal(StaffRoleChangeResult.Invalid, await service.ChangeStaffRoleAsync(
            new("actor", "stamp", "target", "version", role, reason, "request"), default));
    }

    [Fact]
    public async Task RejectsOversizedReasonsAndSelfEditBeforeReadingTheDatabase()
    {
        var service = new AccountService(null!, TimeProvider.System);
        var command = new StaffRoleChange("actor", "stamp", "target", "version", null, new string('x', 501), "request");
        Assert.Equal(StaffRoleChangeResult.Invalid, await service.ChangeStaffRoleAsync(command, default));
        Assert.Equal(StaffRoleChangeResult.Forbidden, await service.ChangeStaffRoleAsync(
            command with { TargetId = command.ActorId, Reason = "Reason" }, default));
    }
}
