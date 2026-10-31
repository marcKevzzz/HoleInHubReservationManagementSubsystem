using System.Threading.RateLimiting;
using HoleInHub.Business.Accounts;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;

namespace HoleInHub.Web.Accounts;

public static class AccountExtensions
{
    public static IServiceCollection AddCustomerAccounts(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        services.AddScoped<AccountService>();
        services.AddScoped<NotificationService>();
        services.AddIdentityCore<AccountUser>(options =>
        {
            options.User.RequireUniqueEmail = true;
            options.Password.RequiredLength = 12;
            options.SignIn.RequireConfirmedEmail = false;
            options.Lockout.MaxFailedAccessAttempts = 5;
            options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
        }).AddUserStore<AccountStore>().AddSignInManager().AddDefaultTokenProviders();
        services.Configure<DataProtectionTokenProviderOptions>(options => options.TokenLifespan = TimeSpan.FromHours(2));
        services.AddAuthentication(IdentityConstants.ApplicationScheme).AddIdentityCookies();
        services.ConfigureApplicationCookie(options =>
        {
            options.LoginPath = "/account/sign-in";
            options.AccessDeniedPath = "/account/sign-in";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.Cookie.SecurePolicy = environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
            options.ExpireTimeSpan = TimeSpan.FromHours(8);
        });
        services.AddAuthorization(options => options.AddPolicy("VerifiedCustomer", policy => policy.RequireAuthenticatedUser().RequireClaim("email_verified", "true")));
        services.AddScoped<IUserClaimsPrincipalFactory<AccountUser>, AccountClaimsFactory>();
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy("accounts", context => RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
        });
        var keys = configuration["DataProtection:KeyDirectory"];
        if (!environment.IsDevelopment() && !environment.IsEnvironment("Testing") && string.IsNullOrWhiteSpace(keys))
        {
            throw new InvalidOperationException("Configure DataProtection:KeyDirectory for persistent production authentication keys.");
        }
        var protection = services.AddDataProtection().SetApplicationName("HoleInHub");
        if (!string.IsNullOrWhiteSpace(keys)) { protection.PersistKeysToFileSystem(new DirectoryInfo(keys)); }
        services.Configure<SmtpOptions>(configuration.GetSection("Smtp"));
        services.Configure<AccountEmailOptions>(configuration.GetSection("AccountEmail"));
        services.AddScoped<SmtpEmailSender>();
        services.AddHostedService<NotificationWorker>();
        return services;
    }
}
