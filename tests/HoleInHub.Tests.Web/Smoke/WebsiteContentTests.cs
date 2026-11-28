using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using HoleInHub.Data.Context;
using HoleInHub.Data.Migrations.Definitions;
using HoleInHub.Business.Website;
using HoleInHub.Web.Accounts;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace HoleInHub.Tests.Web.Smoke;

[Trait("Category", "Integration")]
public sealed class WebsiteContentTests
{
    private const string Editor = "/staff/gallery-content";
    private const string Data = Editor + "/data";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+a93sAAAAASUVORK5CYII=");

    [Fact]
    public async Task AdminChangesSurviveNewHostAndDrivePublicPages()
    {
        var database = "test_hih_website_" + Guid.NewGuid().ToString("N");
        await using var factory = Factory(database);
        var mongo = factory.Services.GetRequiredService<IMongoDbContext>();
        var ct = CancellationToken.None;
        try
        {
            var migration = new Migration_032_WebsiteContent();
            await migration.UpAsync(mongo.Database, ct);
            using var admin = Client(factory, "Admin");
            using var manager = Client(factory, "Manager");
            using var visitor = Client(factory);
            Assert.Equal(HttpStatusCode.Forbidden, (await manager.GetAsync(Data, ct)).StatusCode);
            Assert.NotEqual(HttpStatusCode.OK, (await visitor.GetAsync(Data, ct)).StatusCode);

            var page = await admin.GetStringAsync(Editor, ct);
            var token = Regex.Match(page, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
            Assert.NotEmpty(token);
            using var response = await admin.GetAsync(Data, ct);
            response.EnsureSuccessStatusCode();
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            var initial = json.RootElement.GetProperty("state").Deserialize<WebsiteContentRecord>(Json)!;
            Assert.Equal(18, initial.Gallery.Length);
            Assert.Equal(3, initial.Offers.Length);
            Assert.Contains("golf-experience-hq.mp4", await visitor.GetStringAsync("/gallery", ct));

            var galleryItem = new WebsiteItem
            {
                Id = Guid.NewGuid().ToString("D"), Title = "Integration gallery item", Category = "New category",
                Description = "Integration gallery description", Visibility = "Published"
            };
            var added = initial with
            {
                GalleryCategories = [.. initial.GalleryCategories, galleryItem.Category],
                Gallery = [galleryItem, .. initial.Gallery]
            };

            using var csrf = await Put(admin, null, added);
            Assert.Equal(HttpStatusCode.BadRequest, csrf.StatusCode);
            using var invalidImage = await Put(admin, token, added, galleryItem.Id, [1, 2, 3]);
            Assert.Equal(HttpStatusCode.BadRequest, invalidImage.StatusCode);
            var saved = await Save(admin, token, added, galleryItem.Id, Png);
            var firstImage = saved.Gallery[0].Image;
            Assert.Equal(Png, await visitor.GetByteArrayAsync(firstImage, ct));
            var gallery = await visitor.GetStringAsync("/gallery", ct);
            Assert.Contains(galleryItem.Title, gallery);
            Assert.Contains(firstImage, gallery);
            using var stale = await Put(admin, token, added);
            Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);

            var renamed = saved.Gallery[0] with { Category = "Renamed category", Title = "Renamed gallery item" };
            saved = await Save(admin, token, saved with
            {
                GalleryCategories = [.. initial.GalleryCategories, renamed.Category],
                Gallery = [.. saved.Gallery.Skip(1), renamed]
            }, renamed.Id, Png);
            var newImage = saved.Gallery[^1].Image;
            Assert.NotEqual(firstImage, newImage);
            Assert.Equal(HttpStatusCode.NotFound, (await visitor.GetAsync(firstImage, ct)).StatusCode);
            gallery = await visitor.GetStringAsync("/gallery", ct);
            Assert.DoesNotContain(galleryItem.Title, gallery);
            Assert.Contains(renamed.Title, gallery);
            Assert.True(gallery.IndexOf(initial.Gallery[0].Title.Replace("&", "&amp;"), StringComparison.Ordinal) <
                gallery.IndexOf(renamed.Title, StringComparison.Ordinal));

            var offer = initial.Offers[0] with
            {
                Id = Guid.NewGuid().ToString("D"), Title = "Integration offer", Image = "", ModalTitle = "New details",
                ButtonText = "Book now", ButtonDestination = "/reservations/create"
            };
            saved = await Save(admin, token, saved with
            {
                Gallery = [.. saved.Gallery.SkipLast(1), saved.Gallery[^1] with { Visibility = "Hidden" }],
                Offers = [offer, .. saved.Offers],
                Section = saved.Section with { Heading = "Integration section" }
            });
            Assert.DoesNotContain(renamed.Title, await visitor.GetStringAsync("/gallery", ct));
            Assert.Equal(HttpStatusCode.NotFound, (await visitor.GetAsync(newImage, ct)).StatusCode);
            Assert.Equal(Png, await admin.GetByteArrayAsync(newImage, ct));

            await migration.UpAsync(mongo.Database, ct);
            await using var restarted = Factory(database);
            using var publicAfterRestart = Client(restarted);
            var home = await publicAfterRestart.GetStringAsync("/", ct);
            Assert.Contains("INTEGRATION SECTION", home);
            Assert.Contains("Integration offer", home);
            Assert.Contains("New details", home);
            Assert.True(home.IndexOf("Integration offer", StringComparison.Ordinal) <
                home.IndexOf(initial.Offers[0].Title, StringComparison.Ordinal));
            Assert.DoesNotContain(renamed.Title, await publicAfterRestart.GetStringAsync("/gallery", ct));

            saved = await Save(admin, token, saved with
            {
                Gallery = [.. saved.Gallery.SkipLast(1), saved.Gallery[^1] with { Image = "", Visibility = "Published" }],
                Offers = [saved.Offers[0] with { Title = "Edited offer", Visibility = "Hidden" }, .. saved.Offers.Skip(1)]
            });
            Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync(newImage, ct)).StatusCode);
            Assert.Contains(renamed.Title, await visitor.GetStringAsync("/gallery", ct));
            home = await visitor.GetStringAsync("/", ct);
            Assert.DoesNotContain("Integration offer", home);
            Assert.DoesNotContain("Edited offer", home);

            await Save(admin, token, saved with
            {
                GalleryCategories = initial.GalleryCategories,
                Gallery = [.. saved.Gallery.SkipLast(1)],
                Offers = [.. saved.Offers.Skip(1)],
                Section = saved.Section with { Visibility = "Hidden" }
            });
            Assert.DoesNotContain(renamed.Title, await visitor.GetStringAsync("/gallery", ct));
            Assert.DoesNotContain("home-promotions-title", await visitor.GetStringAsync("/", ct));
        }
        finally
        {
            await mongo.Client.DropDatabaseAsync(database, ct);
        }
    }

    private static async Task<WebsiteContentRecord> Save(HttpClient client, string token, WebsiteContentRecord state,
        string? imageTarget = null, byte[]? image = null)
    {
        using var response = await Put(client, token, state, imageTarget, image);
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, body);
        return JsonSerializer.Deserialize<WebsiteContentRecord>(body, Json)!;
    }

    private static async Task<HttpResponseMessage> Put(HttpClient client, string? token, WebsiteContentRecord state,
        string? imageTarget = null, byte[]? image = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, Data);
        using var body = new MultipartFormDataContent();
        body.Add(new StringContent(JsonSerializer.Serialize(state, Json)), "state");
        if (image is not null)
        {
            body.Add(new StringContent(imageTarget!), "imageTarget");
            body.Add(new ByteArrayContent(image), "image", "upload.png");
        }
        if (token is not null)
        {
            request.Headers.Add("RequestVerificationToken", token);
        }
        request.Content = body;
        return await client.SendAsync(request);
    }

    private static WebApplicationFactory<Program> Factory(string database) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder.UseEnvironment("Testing")
            .ConfigureLogging(logging => logging.ClearProviders())
            .ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MongoDb:ConnectionString"] = "mongodb://localhost:27017/?replicaSet=rs0",
                ["MongoDb:DatabaseName"] = database,
                ["MongoDb:ReplicaSetName"] = "rs0",
                ["Smtp:Host"] = ""
            }))
            .ConfigureTestServices(services =>
            {
                services.AddDataProtection().UseEphemeralDataProtectionProvider();
                services.AddAuthentication().AddScheme<AuthenticationSchemeOptions, TestAuth>("WebsiteTest", _ => { });
                services.Configure<CookieAuthenticationOptions>(StaffPinAccess.Scheme, options => options.ForwardDefault = "WebsiteTest");
            }));

    private static HttpClient Client(WebApplicationFactory<Program> factory, string? role = null)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false
        });
        if (role is not null)
        {
            client.DefaultRequestHeaders.Add("X-Test-Role", role);
        }
        return client;
    }

    private sealed class TestAuth(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var role = Request.Headers["X-Test-Role"].ToString();
            return Task.FromResult(role.Length == 0 ? AuthenticateResult.NoResult() : AuthenticateResult.Success(
                new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity([
                    new Claim(ClaimTypes.NameIdentifier, "website-content-test"), new Claim(ClaimTypes.Role, role)
                ], Scheme.Name)), Scheme.Name)));
        }
    }
}
