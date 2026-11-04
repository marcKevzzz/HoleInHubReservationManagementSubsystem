using System.Text.RegularExpressions;

namespace HoleInHub.Business.Accounts;

public static class ContactRules
{
    public const string NamePattern = @" *[\p{L}\p{M}][\p{L}\p{M} .’'\-]*";
    public const string PhonePattern = "[0-9]{7,15}";
    public const string NameError = "Use letters for names; spaces, apostrophes, periods and hyphens are allowed.";
    public const string PhoneError = "Enter a valid contact number using 7–15 digits only.";
    public static bool IsName(string? value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 100 && Regex.IsMatch(value.Trim(), @"\A" + NamePattern + @"\z", RegexOptions.CultureInvariant);
    public static bool IsPhone(string? value) => value is not null && Regex.IsMatch(value, @"\A" + PhonePattern + @"\z", RegexOptions.CultureInvariant);
    public static string PhoneDigits(string? value) => new((value ?? "").Where(char.IsAsciiDigit).ToArray());
}
