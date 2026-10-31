using HoleInHub.Business.Accounts;
using Microsoft.AspNetCore.Identity;

namespace HoleInHub.Web.Accounts;

public sealed class AccountStore(AccountService accounts) : IUserPasswordStore<AccountUser>, IUserEmailStore<AccountUser>, IUserSecurityStampStore<AccountUser>, IUserLockoutStore<AccountUser>, IUserRoleStore<AccountUser>
{
    public void Dispose() { }
    public async Task<IdentityResult> CreateAsync(AccountUser user, CancellationToken ct)
    {
        user.Roles = ["Customer"];
        return await accounts.CreateAsync(user.ToDetails(), ct) ? IdentityResult.Success : IdentityResult.Failed(new IdentityError { Code = "DuplicateEmail", Description = "Unable to create this account. Try signing in or resetting your password." });
    }
    public async Task<IdentityResult> UpdateAsync(AccountUser user, CancellationToken ct)
    {
        var expected = user.Version;
        user.Version = Guid.NewGuid().ToString("N");
        if (await accounts.UpdateAsync(user.ToDetails(), expected, ct)) { return IdentityResult.Success; }
        user.Version = expected;
        return IdentityResult.Failed(new IdentityError { Code = "ConcurrencyFailure", Description = "Your account changed. Please try again." });
    }
    public Task<IdentityResult> DeleteAsync(AccountUser user, CancellationToken ct) => throw new NotSupportedException();
    public async Task<AccountUser?> FindByIdAsync(string id, CancellationToken ct) => AccountUser.From(await accounts.FindByIdAsync(id, ct));
    public async Task<AccountUser?> FindByNameAsync(string name, CancellationToken ct) => AccountUser.From(await accounts.FindByEmailAsync(name, ct));
    public Task<AccountUser?> FindByEmailAsync(string email, CancellationToken ct) => FindByNameAsync(email, ct);
    public Task<string> GetUserIdAsync(AccountUser u, CancellationToken ct) => Task.FromResult(u.Id);
    public Task<string?> GetUserNameAsync(AccountUser u, CancellationToken ct) => Task.FromResult<string?>(u.Email);
    public Task SetUserNameAsync(AccountUser u, string? v, CancellationToken ct) { u.Email = v ?? ""; return Task.CompletedTask; }
    public Task<string?> GetNormalizedUserNameAsync(AccountUser u, CancellationToken ct) => Task.FromResult<string?>(u.NormalizedEmail);
    public Task SetNormalizedUserNameAsync(AccountUser u, string? v, CancellationToken ct) { u.NormalizedEmail = v ?? ""; return Task.CompletedTask; }
    public Task SetPasswordHashAsync(AccountUser u, string? v, CancellationToken ct) { u.PasswordHash = v ?? ""; return Task.CompletedTask; }
    public Task<string?> GetPasswordHashAsync(AccountUser u, CancellationToken ct) => Task.FromResult<string?>(u.PasswordHash);
    public Task<bool> HasPasswordAsync(AccountUser u, CancellationToken ct) => Task.FromResult(u.PasswordHash.Length > 0);
    public Task SetEmailAsync(AccountUser u, string? v, CancellationToken ct) { u.Email = v ?? ""; return Task.CompletedTask; }
    public Task<string?> GetEmailAsync(AccountUser u, CancellationToken ct) => Task.FromResult<string?>(u.Email);
    public Task<bool> GetEmailConfirmedAsync(AccountUser u, CancellationToken ct) => Task.FromResult(u.EmailConfirmed);
    public Task SetEmailConfirmedAsync(AccountUser u, bool v, CancellationToken ct) { u.EmailConfirmed = v; return Task.CompletedTask; }
    public Task<string?> GetNormalizedEmailAsync(AccountUser u, CancellationToken ct) => Task.FromResult<string?>(u.NormalizedEmail);
    public Task SetNormalizedEmailAsync(AccountUser u, string? v, CancellationToken ct) { u.NormalizedEmail = v ?? ""; return Task.CompletedTask; }
    public Task SetSecurityStampAsync(AccountUser u, string v, CancellationToken ct) { u.SecurityStamp = v; return Task.CompletedTask; }
    public Task<string?> GetSecurityStampAsync(AccountUser u, CancellationToken ct) => Task.FromResult<string?>(u.SecurityStamp);
    public Task<DateTimeOffset?> GetLockoutEndDateAsync(AccountUser u, CancellationToken ct) => Task.FromResult(u.LockoutEnd);
    public Task SetLockoutEndDateAsync(AccountUser u, DateTimeOffset? v, CancellationToken ct) { u.LockoutEnd = v; return Task.CompletedTask; }
    public Task<int> IncrementAccessFailedCountAsync(AccountUser u, CancellationToken ct) => Task.FromResult(++u.AccessFailedCount);
    public Task ResetAccessFailedCountAsync(AccountUser u, CancellationToken ct) { u.AccessFailedCount = 0; return Task.CompletedTask; }
    public Task<int> GetAccessFailedCountAsync(AccountUser u, CancellationToken ct) => Task.FromResult(u.AccessFailedCount);
    public Task<bool> GetLockoutEnabledAsync(AccountUser u, CancellationToken ct) => Task.FromResult(true);
    public Task SetLockoutEnabledAsync(AccountUser u, bool v, CancellationToken ct) => Task.CompletedTask;
    public Task AddToRoleAsync(AccountUser u, string role, CancellationToken ct) => throw new NotSupportedException();
    public Task RemoveFromRoleAsync(AccountUser u, string role, CancellationToken ct) => throw new NotSupportedException();
    public Task<IList<string>> GetRolesAsync(AccountUser u, CancellationToken ct) => Task.FromResult<IList<string>>(u.Roles);
    public Task<bool> IsInRoleAsync(AccountUser u, string role, CancellationToken ct) => Task.FromResult(u.Roles.Contains(role, StringComparer.OrdinalIgnoreCase));
    public Task<IList<AccountUser>> GetUsersInRoleAsync(string role, CancellationToken ct) => throw new NotSupportedException();
}
