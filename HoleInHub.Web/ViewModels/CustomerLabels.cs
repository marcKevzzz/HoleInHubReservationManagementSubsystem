namespace HoleInHubReservationManagementSubsystem.ViewModels;

public static class CustomerLabels
{
    public static string Kind(string kind) => kind switch
    {
        "Account" => "Account customer",
        "Guest" => "Verified guest",
        _ => "Booking contact"
    };
    public static string Name(string first, string last) => string.IsNullOrWhiteSpace(first + last) ? "Name not recorded" : $"{first} {last}".Trim();
    public static string Phone(string value)
    {
        var digits = new string(value.Where(char.IsAsciiDigit).ToArray());
        var national = digits.Length == 12 && digits.StartsWith("63", StringComparison.Ordinal) ? digits[2..]
            : digits.Length == 11 && digits.StartsWith('0') ? digits[1..] : null;
        return national is null ? value : $"+63 {national[..3]} {national[3..6]} {national[6..]}";
    }
    public static string Dial(string value) => new(value.Where(character => char.IsAsciiDigit(character) || character == '+').ToArray());
}
