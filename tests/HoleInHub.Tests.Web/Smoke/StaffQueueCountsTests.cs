using System.Net;
using HoleInHub.Business.Accounts;
using HoleInHub.Data.Accounts;
using HoleInHub.Data.Context;
using HoleInHub.Data.Migrations.Definitions;
using HoleInHub.Data.Reservations;
using HoleInHub.Web.Accounts;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;
using Xunit;

namespace HoleInHub.Tests.Web.Smoke;

public sealed partial class StaffPinAccessTests
{
    [Fact, Trait("Category", "Integration")]
    public async Task QueueBadgesAreRenderedBeforeVisitingAQueuePage()
    {
        using var certificate = DesktopCertificate(DateTimeOffset.UtcNow);
        var database = "test_queue_badges_" + Guid.NewGuid().ToString("N");
        await using var factory = Factory(certificate, database);
        using var browser = Client(factory, true);
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IMongoDbContext>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AccountUser>>();
        try
        {
            await new AccountsMigration().UpAsync(context.Database, default);
            var admin = new AccountUser { Email = "admin@example.test", EmailConfirmed = true };
            Assert.True((await users.CreateAsync(admin, "StaffTestPassword123!")).Succeeded);
            admin.Roles = [StaffRoles.Admin];
            Assert.True((await users.UpdateAsync(admin)).Succeeded);
            await context.Database.GetCollection<ReservationRecord>("reservations").InsertManyAsync(
                Enumerable.Range(0, 2).Select(_ => new ReservationRecord
                {
                    Status = "Confirmed",
                    StartUtc = DateTime.UtcNow.AddDays(2),
                    EndUtc = DateTime.UtcNow.AddDays(2).AddHours(1),
                    ScheduleChange = new() { Status = "Pending" }
                }));
            using var signedIn = await SubmitAsync(browser, "/account/sign-in",
                new() { ["Email"] = admin.Email, ["Password"] = "StaffTestPassword123!" });
            Assert.Equal(HttpStatusCode.Redirect, signedIn.StatusCode);

            var html = await browser.GetStringAsync("/staff/dashboard");

            Assert.Contains("Review queue, 2 awaiting review", html);
            Assert.Matches("data-request-count=\"awaiting\"[^>]*>2</span>", html);
            Assert.Matches("data-request-count=\"changes\"[^>]*>2</span>", html);
            Assert.DoesNotMatch("data-request-count=\"(?:awaiting|changes)\"[^>]*\\shidden(?:=|\\s|>)", html);
        }
        finally { await context.Client.DropDatabaseAsync(database); }
    }
}
