using System.Globalization;
using System.Text.RegularExpressions;

namespace HoleInHubReservationManagementSubsystem.Localization;

/// <summary>Presentation copy only. Never pass names, notes, identifiers or catalog edits to this helper.</summary>
public static partial class CustomerText
{
    private static readonly IReadOnlyDictionary<string, string> Translations = Load();
    public static bool Korean => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ko";
    public static string T(string text) => Korean ? Translations.GetValueOrDefault(text, text) : text;
    public static string F(string text, params object?[] args) => string.Format(CultureInfo.CurrentCulture, T(text), args);
    public static IReadOnlyDictionary<string, string> ClientMessages => Korean ? Translations : Empty;
    private static readonly IReadOnlyDictionary<string, string> Empty = new Dictionary<string, string>();

    private static IReadOnlyDictionary<string, string> Load()
    {
        using var reader = new StreamReader(typeof(CustomerText).Assembly.GetManifestResourceStream("CustomerTranslations")!);
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        while (reader.ReadLine() is { } line)
        {
            var separator = line.IndexOf('|');
            if (separator > 0) { result.Add(line[..separator], line[(separator + 1)..]); }
        }
        return result;
    }

    public static string Error(string text)
    {
        if (!Korean) { return text; }
        var translated = T(text);
        if (translated != text) { return translated; }
        foreach (var (pattern, replacement) in ErrorPatterns)
        {
            var match = Regex.Match(text, pattern, RegexOptions.CultureInvariant);
            if (match.Success)
            {
                return string.Format(CultureInfo.CurrentCulture, replacement,
                    match.Groups.Cast<Group>().Skip(1).Select(x => (object)T(x.Value)).ToArray());
            }
        }
        return text;
    }

    private static readonly (string Pattern, string Replacement)[] ErrorPatterns =
    [
        (@"^The (.+) field is required\.$", "{0} 항목을 입력해 주세요."),
        (@"^The (.+) field is not a valid e-mail address\.$", "올바른 이메일 주소를 입력해 주세요."),
        (@"^The field (.+) must be a string with a maximum length of '(\d+)'\.$", "{0} 항목은 {1}자 이하여야 합니다."),
        (@"^The field (.+) must be a string with a minimum length of '(\d+)' and a maximum length of '(\d+)'\.$", "{0} 항목은 {1}~{2}자여야 합니다."),
        (@"^The field (.+) must be between (.+) and (.+)\.$", "{0} 항목은 {1}~{2} 사이여야 합니다."),
        (@"^'(.+)' and '(.+)' do not match\.$", "{0} 항목과 {1} 항목이 일치하지 않습니다."),
        (@"^The value '.*' is not valid for (.+)\.$", "{0} 항목의 형식을 확인해 주세요."),
        (@"^Choose a start after the full (\d+)-minute review window\. Staff review runs during operating hours\.$", "검토 시간 {0}분 이후의 시작 시간을 선택해 주세요. 직원 검토는 영업시간에 진행됩니다."),
        (@"^Enter between 1 and (\d+) guests, including non-players, for your selected bays\.$", "선택한 베이의 이용 인원은 비플레이어를 포함해 1~{0}명이어야 합니다."),
        (@"^Each bay allows six players; total occupancy is (\d+) including non-players\.$", "베이당 플레이어는 최대 6명이며, 비플레이어를 포함한 총 정원은 {0}명입니다."),
        (@"^Passwords must be at least (\d+) characters\.$", "비밀번호는 {0}자 이상이어야 합니다.")
    ];
}
