using System.Security.Cryptography;
using Microsoft.Extensions.Caching.Memory;

namespace HoleInHub.Web.Accounts;

public sealed record SharedContact(string AccountId, string FirstName, string LastName, string? Email, string? Phone)
{
    public static SharedContact FromAccount(AccountUser user) =>
        new(HoleInHub.Business.BusinessIds.Account(user.Id), user.FirstName, user.LastName, user.Email, user.Phone);
}
public sealed class CustomerShareCode(IMemoryCache cache)
{
    private sealed class Approval(SharedContact contact)
    {
        private SharedContact? value = contact;
        public SharedContact? Take() => Interlocked.Exchange(ref value, null);
    }

    public string Issue(string recipient, SharedContact contact)
    {
        var code = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        cache.Set(("share-contact", recipient, code), new Approval(contact), TimeSpan.FromMinutes(5));
        return code;
    }

    public SharedContact? Redeem(string recipient, string code)
    {
        if (code.Length != 32) { return null; }
        var key = ("share-contact", recipient, code);
        var contact = cache.Get<Approval>(key)?.Take();
        if (contact is not null) { cache.Remove(key); }
        return contact;
    }
}
