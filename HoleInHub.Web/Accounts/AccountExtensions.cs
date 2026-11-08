using System.Threading.RateLimiting;
using HoleInHub.Business.Accounts;
using Microsoft.AspNetCore.Authentication;
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
        var authentication = services.AddAuthentication(IdentityConstants.ApplicationScheme);
        authentication.AddIdentityCookies();
        StaffPinAccess.Register(services, authentication, environment);
        if (configuration.GetValue<bool>("Authentication:Google:Enabled"))
        {
            var clientId = configuration["Authentication:Google:ClientId"];
            var clientSecret = configuration["Authentication:Google:ClientSecret"];
            if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
            {
                throw new InvalidOperationException("Google authentication requires both ClientId and ClientSecret when enabled.");
            }
            authentication.AddGoogle(options =>
            {
                options.ClientId = clientId; options.ClientSecret = clientSecret;
                options.SignInScheme = IdentityConstants.ExternalScheme;
                options.SaveTokens = false;
                options.ClaimActions.MapJsonKey("google:email_verified", "email_verified");
                options.ClaimActions.MapJsonKey("google:hosted_domain", "hd");
                options.Events.OnRemoteFailure = context =>
                {
                    context.HandleResponse(); context.Response.Redirect("/account/google-failed"); return Task.CompletedTask;
                };
                options.Events.OnAccessDenied = context =>
                {
                    context.HandleResponse(); context.Response.Redirect("/account/google-failed"); return Task.CompletedTask;
                };
            });
        }
        services.ConfigureApplicationCookie(options =>
        {
            options.LoginPath = "/account/sign-in";
            options.AccessDeniedPath = "/account/sign-in";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.Cookie.SecurePolicy = environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
            options.ExpireTimeSpan = TimeSpan.FromHours(8);
        });
        services.AddAuthorization(options =>
        {
            options.AddPolicy("VerifiedCustomer",
                policy => policy.RequireAuthenticatedUser().RequireClaim("email_verified", "true"));
            options.AddPolicy(StaffAuthorization.Workspace, policy => policy
                .AddAuthenticationSchemes(StaffPinAccess.Scheme)
                .RequireAuthenticatedUser()
                .RequireRole(StaffRoles.Workspace));
        });
        services.AddScoped<IUserClaimsPrincipalFactory<AccountUser>, AccountClaimsFactory>();
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = async (context, ct) =>
            {
                if (context.HttpContext.Request.Path.StartsWithSegments("/guest/reservations"))
                {
                    context.HttpContext.Response.ContentType = "text/plain; charset=utf-8";
                    context.HttpContext.Response.Headers.CacheControl = "no-store";
                    await context.HttpContext.Response.WriteAsync("Too many requests. Please wait a few minutes before requesting or verifying another code.", ct);
                }
            };
            options.AddPolicy("accounts", context => RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
            options.AddPolicy("guest-recovery-send", context => RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(10), QueueLimit = 0 }));
            options.AddPolicy("guest-recovery-verify", context => RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions { PermitLimit = 30, Window = TimeSpan.FromMinutes(10), QueueLimit = 0 }));
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
