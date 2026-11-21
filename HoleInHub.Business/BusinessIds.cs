using HoleInHub.Data;

namespace HoleInHub.Business;

public static class BusinessIds
{
    public static string Account(string key) => BusinessIdentifier.Format("ACC", key);
    public static string Service(string key) => BusinessIdentifier.Format("SVC", key);
    public static string Block(string key) => BusinessIdentifier.Format("BLK", key);
    public static string VenueSettings => BusinessIdentifier.Format("SET", "venue");

    public static string? ServiceImage(string path)
    {
        const string route = "/media/services/";
        return path.StartsWith(route, StringComparison.Ordinal) && path.Length > route.Length
            ? BusinessIdentifier.Format("IMG", path[route.Length..]) : null;
    }
}
