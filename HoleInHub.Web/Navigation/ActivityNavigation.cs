using Microsoft.AspNetCore.Http;

namespace HoleInHub.Web.Navigation;

public static class ActivityNavigation
{
    public static string? FromRequest(HttpRequest request) =>
        request.Query.TryGetValue("activityReturn", out var values) && values.Count == 1
            ? SafeReturnUrl(values[0]) : null;

    public static string? SafeReturnUrl(string? value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > 4096
            || value.Any(c => char.IsControl(c) || c is '\\' or '#')) { return null; }
        var path = value.Split('?', 2)[0];
        const string root = "/staff/activity";
        if (path != root)
        {
            if (!path.StartsWith(root + "/", StringComparison.Ordinal)) { return null; }
            var key = Uri.UnescapeDataString(path[(root.Length + 1)..]);
            if (key.Length == 0 || key is "." or ".."
                || key.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '-' and not '_' and not '.' and not '~' and not ':')) { return null; }
        }
        // Reject escaped controls even when another URL layer encoded them first.
        var decoded = value;
        for (var depth = 0; depth < 4; depth++)
        {
            var next = Uri.UnescapeDataString(decoded);
            if (next == decoded) { return value; }
            if (next.Any(c => char.IsControl(c) || c == '\\')) { return null; }
            decoded = next;
        }
        return null;
    }
}
