using System.Globalization;
using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using HoleInHub.Business.Accounts;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace HoleInHub.Web.Accounts;

public sealed class StaffPinAccess
{
    public const string Scheme = "StaffPin";
    public const string StaffHostClaim = "hih:staff-desktop";
    public const string PinVersionClaim = "hih:pin-version";
    public const string StartedClaim = "hih:staff-started";
    public const string SecurityStampClaim = "hih:staff-security-stamp";

    public bool Enabled { get; init; }
    public string Host { get; init; } = "";
    public string Pepper { get; init; } = "";
    public string[] DesktopFingerprints { get; init; } = [];
    public string PresentationAccount { get; init; } = "";
    public string PresentationManagerAccount { get; init; } = "";
    public string PresentationAdminAccount { get; init; } = "";
    public bool IsPresentation => !string.IsNullOrWhiteSpace(PresentationAccount);
    public bool HasLeadershipPresentation => PresentationManagerAccount.Length > 0 || PresentationAdminAccount.Length > 0;
    public string PresentationEmail(string? workspace) => workspace switch
    {
        null or "" => PresentationAccount,
        "manager" => PresentationManagerAccount,
        "admin" => PresentationAdminAccount,
        _ => ""
    };

    public bool IsStaffHost(HttpRequest request) =>
        Enabled && string.Equals(Host, request.Host.Host, StringComparison.OrdinalIgnoreCase);

    public async Task<string?> GetDesktopAsync(HttpContext context, DateTimeOffset now)
    {
        if (!IsStaffHost(context.Request) || !context.Request.IsHttps)
        {
            return null;
        }
        if (IsPresentation)
        {
            return !HasLeadershipPresentation || (context.Connection.RemoteIpAddress is { } ip && IPAddress.IsLoopback(ip))
                ? "presentation" : null;
        }
        var certificate = await context.Connection.GetClientCertificateAsync(context.RequestAborted);
        if (certificate is null || now.UtcDateTime < certificate.NotBefore.ToUniversalTime()
            || now.UtcDateTime >= certificate.NotAfter.ToUniversalTime())
        {
            return null;
        }
        var fingerprint = certificate.GetCertHashString(HashAlgorithmName.SHA256);
        return DesktopFingerprints.Contains(fingerprint, StringComparer.OrdinalIgnoreCase) ? fingerprint : null;
    }

    public string PinInput(AccountUser employee, string pin) =>
        Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(Pepper),
            Encoding.UTF8.GetBytes($"{employee.Id}:{employee.SecurityStamp}:{pin}")));

    public static IEnumerable<string> GetWorkspaceRoles(AccountUser? employee) =>
        employee is { EmailConfirmed: true }
            ? StaffRoles.Workspace.Intersect(employee.Roles, StringComparer.Ordinal)
            : [];

    public IEnumerable<string> GetSignInRoles(AccountUser? employee)
    {
        var roles = GetWorkspaceRoles(employee);
        if (!IsPresentation) { return roles; }
        return roles.ToArray() switch
        {
            [StaffRoles.Staff] when Matches(PresentationAccount) => roles,
            [StaffRoles.Manager] when Matches(PresentationManagerAccount) => roles,
            [StaffRoles.Admin] when Matches(PresentationAdminAccount) => roles,
            _ => []
        };

        bool Matches(string account) => account.Length > 0 && string.Equals(employee?.Email, account, StringComparison.OrdinalIgnoreCase);
    }

    public static void Register(IServiceCollection services, AuthenticationBuilder authentication, IHostEnvironment environment)
    {
        services.AddOptions<StaffPinAccess>().BindConfiguration("StaffPin")
            .Validate(settings => !settings.Enabled || (Uri.CheckHostName(settings.Host) != UriHostNameType.Unknown
                && settings.Pepper.Length >= 32 && (settings.IsPresentation
                    ? environment.IsDevelopment()
                    : settings.DesktopFingerprints.Length > 0 && settings.DesktopFingerprints.All(
                        fingerprint => fingerprint.Length == 64 && fingerprint.All(Uri.IsHexDigit)))),
                "Staff PIN requires a staff hostname, a secret pepper of at least 32 characters, and an approved desktop or development presentation account.")
            .Validate(settings => !settings.Enabled || !settings.HasLeadershipPresentation ||
                (environment.IsDevelopment() && settings.IsPresentation && settings.Host == "localhost"),
                "Manager and Admin presentation PINs require a localhost development workspace.")
            .ValidateOnStart();
        services.AddSingleton(provider => provider.GetRequiredService<IOptions<StaffPinAccess>>().Value);
        services.AddScoped<StaffPinService>();
        authentication.AddCookie(Scheme, options =>
        {
            options.LoginPath = "/staff/sign-in";
            options.AccessDeniedPath = "/staff/sign-in";
            options.Cookie.Name = "__Host-HihStaffPin";
            options.Cookie.Path = "/";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            options.ExpireTimeSpan = TimeSpan.FromMinutes(15);
            options.SlidingExpiration = true;
            options.Events.OnValidatePrincipal = ValidateSessionAsync;
            options.Events.OnRedirectToAccessDenied = context =>
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return Task.CompletedTask;
            };
        });
        services.AddOptions<CookieAuthenticationOptions>(Scheme).Configure<StaffPinAccess, IConfiguration>((options, access, config) =>
        {
            if (access.HasLeadershipPresentation && config["MongoDb:DatabaseName"]?.StartsWith("hih_role_demo_", StringComparison.Ordinal) == true)
            {
                options.Cookie.Name = "__Host-HihRoleDemoPin";
            }
        });
        services.AddOptions<AntiforgeryOptions>().Configure<StaffPinAccess, IConfiguration>((options, access, config) =>
        {
            if (!access.HasLeadershipPresentation || config["MongoDb:DatabaseName"]?.StartsWith("hih_role_demo_", StringComparison.Ordinal) != true) { return; }
            options.Cookie.Name = "__Host-HihRoleDemoCsrf";
            options.Cookie.Path = "/";
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        });
    }

    private static async Task ValidateSessionAsync(CookieValidatePrincipalContext context)
    {
        var services = context.HttpContext.RequestServices;
        var now = services.GetRequiredService<TimeProvider>().GetUtcNow();
        var access = services.GetRequiredService<StaffPinAccess>();
        var desktop = await access.GetDesktopAsync(context.HttpContext, now);
        var principal = context.Principal;
        var id = principal?.FindFirstValue(ClaimTypes.NameIdentifier);
        if (desktop is not null && principal is not null && id is not null && desktop == principal.FindFirstValue(StaffHostClaim)
            && long.TryParse(principal.FindFirstValue(StartedClaim), NumberStyles.None, CultureInfo.InvariantCulture, out var started)
            && started <= now.ToUnixTimeSeconds() && now.ToUnixTimeSeconds() - started < (long)TimeSpan.FromHours(8).TotalSeconds)
        {
            var employee = await services.GetRequiredService<UserManager<AccountUser>>().FindByIdAsync(id);
            if (employee is not null && (employee.LockoutEnd is null || employee.LockoutEnd <= now)
                && employee.SecurityStamp == principal.FindFirstValue(SecurityStampClaim)
                && access.GetSignInRoles(employee).SequenceEqual(principal.FindAll(ClaimTypes.Role).Select(claim => claim.Value)))
            {
                var credential = await services.GetRequiredService<StaffPinService>().ReadAsync(id, context.HttpContext.RequestAborted);
                if (credential is not null && credential.Version == principal.FindFirstValue(PinVersionClaim))
                {
                    return;
                }
            }
        }
        context.RejectPrincipal();
        await context.HttpContext.SignOutAsync(Scheme);
    }
}
