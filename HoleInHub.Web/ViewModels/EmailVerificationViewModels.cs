namespace HoleInHub.Web.ViewModels;

public class EmailCodeFormModel
{
    public string Email { get; init; } = "";
    public string ActionUrl { get; init; } = "/Reservations/Verify";
    public string? ResendUrl { get; init; }
    public string SubmitLabel { get; init; } = "Verify and submit";
    public string? ReturnUrl { get; init; }
    public DateTime? ExpiresUtc { get; init; }
    public DateTime? ResendAfterUtc { get; init; }
    public DateTime ServerNowUtc { get; init; }
    public bool Expired { get; init; }
    public bool Verified { get; init; }
    public bool ResendWhenExpired { get; init; }
}
public sealed class AccountVerificationViewModel : EmailCodeFormModel
{
    public bool HasContext { get; init; }
    public bool Complete { get; init; }
}
