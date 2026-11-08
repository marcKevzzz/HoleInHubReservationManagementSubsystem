using System.Net;
using System.Text.RegularExpressions;
using HoleInHub.Data.Context;
using HoleInHub.Data.Migrations.Definitions;
using HoleInHub.Data.Reservations;
using HoleInHub.Web.Accounts;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;
using Xunit;

namespace HoleInHub.Tests.Web.Smoke;

public sealed class AccountHubAccessTests
{
    [Fact]
    public void PaymentHistoryUsesCustomerLabelsWithoutChangingStoredAuditValues()
    {
        Assert.Equal("Payment: Deposit received", HoleInHubReservationManagementSubsystem.ViewModels.ReservationLabels.History("Demo payment: DepositReceived"));
        Assert.Equal("Confirmed", HoleInHubReservationManagementSubsystem.ViewModels.ReservationLabels.History("Confirmed"));
    }
    [Theory]
    [InlineData("/my-profile")]
    [InlineData("/transactions")]
    public async Task AccountHubRequiresSignIn(string path)
    {
        await using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/account/sign-in", response.Headers.Location!.ToString());
    }
}

[Trait("Category", "Integration")]
public sealed class AccountHubIdentityTests
{
    [Fact]
    public async Task ProfileUpdatesOnlyCurrentContactAndTransactionsContainOnlyOwnedDemoBookings()
    {
        var database = $"holeinhub_hub_test_{Guid.NewGuid():N}";
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.UseEnvironment("Testing").ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MongoDb:ConnectionString"] = "mongodb://localhost:27017/?replicaSet=rs0",
                ["MongoDb:DatabaseName"] = database,
                ["MongoDb:ReplicaSetName"] = "rs0"
            })));
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IMongoDbContext>();
        try
        {
            await new AccountsMigration().UpAsync(context.Database, CancellationToken.None);
            await new Migration_002_Reservations().UpAsync(context.Database, CancellationToken.None);
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AccountUser>>();
            var owner = new AccountUser { Email = "owner@example.com", FirstName = "Original", LastName = "Owner", Phone = "09123456789", EmailConfirmed = true };
            var other = new AccountUser { Email = "other@example.com", FirstName = "Other", LastName = "Player", Phone = "09123456789" };
            Assert.True((await users.CreateAsync(owner, "GoodPassword123!")).Succeeded);
            Assert.True((await users.CreateAsync(other, "GoodPassword123!")).Succeeded);
            using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
            var signIn = await client.GetStringAsync("/account/sign-in");
            Assert.Equal(HttpStatusCode.Redirect, (await client.PostAsync("/account/sign-in", Form(signIn, new() { ["Email"] = owner.Email, ["Password"] = "GoodPassword123!" }))).StatusCode);
            var profile = await client.GetStringAsync("/my-profile");
            Assert.Contains("value=\"Original\"", profile);
            Assert.Contains(owner.Email, profile);
            Assert.DoesNotContain("name=\"Email\"", profile);
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/my-profile", new FormUrlEncodedContent(new Dictionary<string, string>()))).StatusCode);
            var invalid = await client.PostAsync("/my-profile", Form(profile, new() { ["FirstName"] = "Player2", ["LastName"] = "Changed", ["Phone"] = "bad" }));
            Assert.Equal(HttpStatusCode.OK, invalid.StatusCode);
            Assert.Contains("Use letters for names", await invalid.Content.ReadAsStringAsync());
            Assert.Equal("Original", (await users.FindByIdAsync(owner.Id))!.FirstName);
            var saved = await client.PostAsync("/my-profile", Form(profile, new()
            {
                ["FirstName"] = "Updated",
                ["LastName"] = "Player",
                ["Phone"] = "09987654321",
                ["Email"] = "attacker@example.com",
                ["Id"] = other.Id,
                ["Roles"] = "Admin"
            }));
            Assert.Equal(HttpStatusCode.Redirect, saved.StatusCode);
            var current = (await users.FindByIdAsync(owner.Id))!;
            Assert.Equal("Updated", current.FirstName);
            Assert.Equal(owner.Email, current.Email);
            Assert.Equal(new[] { "Customer" }, current.Roles);
            Assert.Equal("Other", (await users.FindByIdAsync(other.Id))!.FirstName);
            var booking = await client.GetStringAsync("/Reservations/Create");
            Assert.Contains("value=\"Updated\"", booking);
            Assert.Contains("value=\"09987654321\"", booking);
            var now = DateTime.UtcNow;
            await context.Database.GetCollection<ReservationRecord>("reservations").InsertManyAsync(new[]
            {
                Record(owner.Id, "OWN-DEMO", "WholeShop", "DepositReceived", now),
                Record(other.Id, "OTHER-DEMO", "WholeShop", "DepositReceived", now),
                Record(owner.Id, "ORDINARY-BOOKING", "Regular", "NotRequired", now)
            });
            var transactions = await client.GetStringAsync("/transactions");
            Assert.Contains("OWN-DEMO", transactions);
            Assert.Contains("DEMO-PAYMENT-REF", transactions);
            Assert.Contains("₱40,000.00", WebUtility.HtmlDecode(transactions));
            Assert.Contains("₱20,000.00", WebUtility.HtmlDecode(transactions));
            Assert.DoesNotContain("OTHER-DEMO", transactions);
            Assert.DoesNotContain("ORDINARY-BOOKING", transactions);
            Assert.Contains("No money is collected", transactions);
        }
        finally { await context.Client.DropDatabaseAsync(database); }
    }

    private static ReservationRecord Record(string owner, string reference, string service, string payment, DateTime now) => new()
    {
        AccountId = owner,
        Reference = reference,
        Service = service,
        Status = "Completed",
        PaymentStatus = payment,
        PaymentReference = "DEMO-PAYMENT-REF",
        RentalTotal = 40000m,
        DepositAmount = 20000m,
        CreatedUtc = now,
        StartUtc = now.AddDays(-2),
        EndUtc = now.AddDays(-1),
        ReviewDeadlineUtc = now.AddDays(-3),
        IdempotencyKey = reference
    };

    private static FormUrlEncodedContent Form(string html, Dictionary<string, string> fields)
    {
        fields["__RequestVerificationToken"] = WebUtility.HtmlDecode(Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
        Assert.NotEmpty(fields["__RequestVerificationToken"]);
        return new FormUrlEncodedContent(fields);
    }
}
