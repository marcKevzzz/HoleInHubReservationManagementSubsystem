using System.Security.Cryptography;
using System.Text;

namespace HoleInHub.Data.Migrations;

public static class MongoMigrationChecksum
{
    public static string Compute(string canonicalContent)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(canonicalContent);
        var bytes = Encoding.UTF8.GetBytes(canonicalContent);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexStringLower(hash);
    }
}
