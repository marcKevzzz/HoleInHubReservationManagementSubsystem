using System.Security.Cryptography;
using System.Text;

namespace HoleInHub.Data;

public static class BusinessIdentifier
{
    public static string Format(string prefix, string id) =>
        prefix + "-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(id)), 0, 8);
}
