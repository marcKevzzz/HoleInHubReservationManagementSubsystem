using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using HoleInHub.Business.Accounts;
using HoleInHub.Data.Accounts;
using HoleInHub.Data.Configuration;
using HoleInHub.Data.Context;
using HoleInHub.Data.Reservations;
using HoleInHub.Web.Accounts;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

const string uri = "mongodb://localhost:27017/?replicaSet=rs0";
var port = int.Parse(Environment.GetEnvironmentVariable("HIH_BROWSER_PORT") ?? "54893");
var origin = $"https://localhost:{port}";
if (!Guid.TryParseExact(Environment.GetEnvironmentVariable("HIH_BROWSER_RUN_ID"), "D", out var runId)) { throw new InvalidOperationException("A browser fixture run ID is required."); }
var database = "test_browser_" + runId.ToString("N");
var pin = Environment.GetEnvironmentVariable("HIH_BROWSER_MANAGER_PIN");
if (pin?.Length != 6 || !pin.All(char.IsAsciiDigit)) { throw new InvalidOperationException("A generated six-digit browser fixture PIN is required."); }
var web = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../HoleInHub.Web"));
var context = new MongoDbContext(Options.Create(new MongoDbOptions { ConnectionString = uri, DatabaseName = database }));
var directory = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "hih-browser-" + runId.ToString("D")));
var pepper = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
var certificatePath = Path.Combine(directory.FullName, "localhost.pfx");
using var key = RSA.Create(2048);
var request = new CertificateRequest("CN=localhost", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
var names = new SubjectAlternativeNameBuilder();
names.AddDnsName("localhost");
names.AddIpAddress(IPAddress.Loopback);
request.CertificateExtensions.Add(names.Build());
using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(1));
File.WriteAllBytes(certificatePath, certificate.Export(X509ContentType.Pfx, pepper));
using var stop = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.Cancel(); };
using var terminate = OperatingSystem.IsWindows() ? null : PosixSignalRegistration.Create(PosixSignal.SIGTERM, signal => { signal.Cancel = true; stop.Cancel(); });
using var shutdownWatch = new FileSystemWatcher(directory.FullName, "shutdown");
shutdownWatch.Created += (_, _) => stop.Cancel();
shutdownWatch.EnableRaisingEvents = true;
try
{
    await Run("HoleInHub.Migrator", ["migrate"]);
    var account = new AccountUser
    {
        Email = "manager@browser.example.test",
        NormalizedEmail = "MANAGER@BROWSER.EXAMPLE.TEST",
        EmailConfirmed = true,
        Roles = [StaffRoles.Manager],
        FirstName = "Browser",
        LastName = "Manager"
    };
    var accounts = new AccountService(new AccountPersistence(context), TimeProvider.System);
    var access = new StaffPinAccess { Pepper = pepper };
    var hashed = new PasswordHasher<AccountUser>().HashPassword(account, access.PinInput(account, pin));
    var pins = new StaffPinService(new StaffPinPersistence(context), TimeProvider.System);
    if (!await accounts.CreateAsync(account.ToDetails(), default) || !await pins.TrySaveAsync(account.Id, hashed, account.SecurityStamp, default))
    {
        throw new InvalidOperationException("Could not seed browser manager.");
    }
    var store = new ReservationPersistence(context);
    var today = DateOnly.FromDateTime(HoleInHub.Business.Reservations.DashboardMetrics.Local(DateTime.UtcNow));
    if (Environment.GetEnvironmentVariable("HIH_BROWSER_RENTAL") == "1")
    {
        var services = context.Database.GetCollection<HoleInHub.Data.Services.ServiceCatalogRecord>("service_catalog");
        await services.UpdateOneAsync(x => x.Id == HoleInHub.Data.Services.SeasonalRental.Vip,
            Builders<HoleInHub.Data.Services.ServiceCatalogRecord>.Update.Set(x => x.SeasonStart, today)
                .Set(x => x.SeasonEnd, today.AddMonths(2)).Set(x => x.Active, true));
    }
    var sample = new List<(string Customer, string Status, string Service, int Offset)> {
        ("Repeat", "Completed", "Regular", -12), ("Repeat", "Completed", "Regular", -1), ("New", "Cancelled", "VIP", -2) };
    if (Environment.GetEnvironmentVariable("HIH_REPORT_BENCHMARK") == "1")
    {
        sample.Add((runId.ToString("N"), "Pending", "Regular", -91));
        for (var day = 0; day < 90; day++)
        {
            for (var slot = 0; slot < 4; slot++)
            {
                sample.Add(("Sample" + (day % 25), slot == 3 ? "Cancelled" : "Completed", slot % 2 == 0 ? "Regular" : "VIP", -day));
            }
        }
    }
    var catalog = await store.CatalogAsync(default);
    foreach (var (customer, status, service, offset) in sample)
    {
        var start = HoleInHub.Business.Reservations.DashboardMetrics.Utc(today.AddDays(offset)).AddHours(14);
        var bay = catalog.First(resource => resource.Service == service);
        await store.InsertAsync(new ReservationRecord
        {
            AccountId = customer,
            FirstName = customer,
            LastName = "Guest",
            Email = customer.ToLowerInvariant() + "@browser.example.test",
            Phone = "09123456789",
            Status = status,
            Service = service,
            Reference = "HIH-" + Guid.NewGuid().ToString("N")[..10].ToUpperInvariant(),
            StartUtc = start,
            EndUtc = start.AddHours(1),
            CreatedUtc = start.AddDays(-1),
            ReviewDeadlineUtc = start.AddHours(-1),
            ResourceIds = [bay.Id],
            Attendees = 1,
            IdempotencyKey = Guid.NewGuid().ToString("N"),
            SubmissionHash = "browser-fixture"
        }, default);
    }
    if (Environment.GetEnvironmentVariable("HIH_REPORT_BENCHMARK") == "1")
    {
        // Historical seed notifications would otherwise queue ahead of the live verification email.
        await context.Database.GetCollection<OutboxRecord>("outbox").DeleteManyAsync(FilterDefinition<OutboxRecord>.Empty);
    }
    await Run("HoleInHub.Web", []);
}
catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
finally
{
    await context.Client.DropDatabaseAsync(database);
    shutdownWatch.Dispose();
    File.Delete(Path.Combine(directory.FullName, "shutdown"));
    File.Delete(certificatePath);
    directory.Delete();
}

async Task Run(string project, string[] arguments)
{
    var root = Directory.GetParent(web)!.FullName;
    var start = new ProcessStartInfo("dotnet") { WorkingDirectory = web, UseShellExecute = false };
    foreach (var argument in new[] { "run", "-c", new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name, "--no-build", "--no-launch-profile", "--project", Path.Combine(root, project), "--" }.Concat(arguments))
    {
        start.ArgumentList.Add(argument);
    }
    // Launch only with platform variables and explicit fixture configuration, irrespective of local deployment overrides.
    foreach (var name in start.Environment.Keys.Where(name => name.Contains("__") || name.StartsWith("ASPNETCORE_", StringComparison.OrdinalIgnoreCase)).ToArray())
    {
        start.Environment.Remove(name);
    }
    var settings = new Dictionary<string, string>
    {
        ["MongoDb__ConnectionString"] = uri,
        ["MongoDb__DatabaseName"] = database,
        ["MongoDb__ReplicaSetName"] = "rs0",
        ["DOTNET_ENVIRONMENT"] = "Testing",
        ["ASPNETCORE_ENVIRONMENT"] = "Testing",
        ["ASPNETCORE_URLS"] = origin,
        ["Kestrel__Certificates__Default__Path"] = certificatePath,
        ["Kestrel__Certificates__Default__Password"] = pepper,
        ["StaffPin__Enabled"] = "true",
        ["StaffPin__Host"] = "localhost",
        ["StaffPin__RequireDesktopCertificate"] = "false",
        ["StaffPin__Pepper"] = pepper,
        ["AccountEmail__PublicBaseUrl"] = origin,
        ["Smtp__Host"] = "127.0.0.1",
        ["Smtp__Port"] = "1025",
        ["Smtp__UseStartTls"] = "false",
        ["Smtp__From"] = "fixture@browser.example.test",
        ["ReservationPolicy__SharedClassesEnabled"] = "true",
        ["ReservationPolicy__PhilippineCoachSchedulesEnabled"] = "true",
        ["ReservationPolicy__Approved"] = "true",
        ["ReservationPolicy__DurationsApproved"] = "true",
        ["ReservationPolicy__ReviewStart"] = "00:00:00",
        ["ReservationPolicy__ReviewEnd"] = "1.00:00:00",
        ["ReservationPolicy__StaffEmails__0"] = "staff@browser.example.test"
    };
    if (Environment.GetEnvironmentVariable("HIH_BROWSER_RENTAL") == "1")
    {
        settings["DOTNET_ENVIRONMENT"] = settings["ASPNETCORE_ENVIRONMENT"] = "Development";
        settings["ReservationPolicy__DemoPaymentsEnabled"] = "true";
    }
    for (var day = 0; day < 7; day++) { settings["ReservationPolicy__ReviewDays__" + day] = day.ToString(); }
    foreach (var (name, value) in settings) { start.Environment[name] = value; }
    using var child = Process.Start(start)!;
    try { await child.WaitForExitAsync(stop.Token); }
    finally { if (!child.HasExited) { child.Kill(entireProcessTree: true); await child.WaitForExitAsync(); } }
    if (child.ExitCode != 0) { throw new InvalidOperationException(project + " exited with code " + child.ExitCode); }
}
