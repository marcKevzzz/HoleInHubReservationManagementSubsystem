using System.Text.RegularExpressions;

namespace HoleInHub.Business.Accounts;

public static class ContactRules
{
    public const string NamePattern = @" *[\p{L}\p{M}][\p{L}\p{M} .,’'\-]*";
    // Keep known PH/KR prefixes out of the general international-number fallback.
    public const string PhonePattern = @"(?:09[0-9]{9}|\+?63(?:9[0-9]{9}|[2-8][0-9]{8})|010[0-9]{8}|02[0-9]{7,8}|\+?82(?:10[0-9]{8}|2[0-9]{7,8}|[3-6][1-5][0-9]{7,8}|70[0-9]{8})|(?!(?:09|010|02|\+?63|\+?82))\+?[0-9]{7,15})";
    public const string NameError = "Use letters for names; spaces, commas, apostrophes, periods and hyphens are allowed.";
    public const string PhoneError = "Check the contact number's prefix and digit count. Mobile numbers starting with 09 or 010 need 11 digits; 639 or 8210 need 12. Drop the leading 0 after a country code.";
    public static bool IsName(string? value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 100 && Regex.IsMatch(value.Trim(), @"\A" + NamePattern + @"\z", RegexOptions.CultureInvariant);
    public static bool IsPhone(string? value) => value is not null && Regex.IsMatch(value, @"\A" + PhonePattern + @"\z", RegexOptions.CultureInvariant);
    public static string PhoneDigits(string? value) => new((value ?? "").Where(char.IsAsciiDigit).ToArray());
    public static bool IsCountryPhone(string country, string? value)
    {
        var (code, maximum) = country switch
        {
            "PH" => ("63", 10), "KR" => ("82", 10), "CN" => ("86", 11),
            "US" => ("1", 10), "JP" => ("81", 10), _ => ("", 0)
        };
        var digits = PhoneDigits(value);
        return maximum > 0 && IsPhone(value) && digits.StartsWith(code, StringComparison.Ordinal) && digits.Length <= code.Length + maximum;
    }
}
