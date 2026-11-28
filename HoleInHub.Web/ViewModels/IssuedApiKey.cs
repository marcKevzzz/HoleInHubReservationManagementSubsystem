using HoleInHub.Business.Services;

namespace HoleInHub.Web.ViewModels;

public sealed record IssuedApiKey(string Name, string Key, int Days, IntegrationPermission[] Permissions);

public sealed class PendingApiKey(IssuedApiKey issued)
{
    private IssuedApiKey? value = issued;
    public IssuedApiKey? Take() => Interlocked.Exchange(ref value, null);
}
