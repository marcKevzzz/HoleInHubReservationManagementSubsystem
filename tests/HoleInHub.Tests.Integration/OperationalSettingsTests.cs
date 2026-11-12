using HoleInHub.Business.Settings;
using HoleInHub.Business.Reservations;
using HoleInHub.Data.Settings;
using HoleInHub.Data.Migrations.Definitions;
using HoleInHub.Tests.Integration.Fixtures;
using Microsoft.Extensions.Options;
using Xunit;
namespace HoleInHub.Tests.Integration.Settings;
[Trait("Category","Integration")]
public sealed class OperationalSettingsTests(MongoDbFixture fixture) : IClassFixture<MongoDbFixture>
{
    [Fact] public async Task SettingsPersistAcrossScopesAndRejectStaleEdits()
    {
        Assert.True(fixture.IsServerAvailable,"Start the isolated local MongoDB replica set.");
        var context=fixture.CreateDbContext();var migration=new Migration_019_SystemSettings();await migration.UpAsync(context.Database,default);await migration.UpAsync(context.Database,default);
        var defaults=Options.Create(new ReservationPolicy { Approved=true,DurationsApproved=true,ReviewStart=TimeSpan.FromHours(10),ReviewEnd=TimeSpan.FromDays(1),ReviewDays=[0,1,2,3,4,5,6],StaffEmails=["staff@example.test"] });
        OperationalSettings Scope()=>new(new SystemSettingsPersistence(context),defaults,TimeProvider.System);
        var settings=Scope();var edit=await settings.GetAsync(default);Assert.Equal(-1,edit.Revision);edit.OnlineEnabled=false;
        await settings.SaveAsync(edit,"admin",default);await settings.SaveAsync(edit,"admin",default);
        Assert.False((await Scope().PolicyAsync(default)).Approved);
        var saved=await Scope().GetAsync(default);Assert.Equal(0,saved.Revision);
        edit.CommandId=Guid.NewGuid().ToString("N");await Assert.ThrowsAsync<InvalidOperationException>(()=>Scope().SaveAsync(edit,"admin",default));
    }
}
