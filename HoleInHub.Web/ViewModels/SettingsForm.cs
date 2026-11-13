using System.ComponentModel.DataAnnotations;
using HoleInHub.Business.Settings;
namespace HoleInHubReservationManagementSubsystem.ViewModels;

public sealed class SettingsForm
{
    public long Revision { get; set; } = -1;
    [Required] public string CommandId { get; set; } = Guid.NewGuid().ToString("N");
    public bool OnlineEnabled { get; set; }
    public bool DurationsApproved { get; set; }
    [Range(0, 1439)] public int ReviewStartMinutes { get; set; }
    [Range(1, 1440)] public int ReviewEndMinutes { get; set; }
    public int[] ReviewDays { get; set; } = [];
    [StringLength(6000)] public string? StaffEmails { get; set; }
    [Required, StringLength(120)] public string VenueName { get; set; } = "Hole In Hub Golf & Lounge";
    [EmailAddress, StringLength(254)] public string? ContactEmail { get; set; }
    [StringLength(40)] public string? ContactPhone { get; set; }
    [StringLength(12000)] public string? BookingPolicy { get; set; }
    [StringLength(12000)] public string? PrivacyPolicy { get; set; }
    [StringLength(12000)] public string? VenueRules { get; set; }
    public bool PoliciesPublished { get; set; }
    public DateTime? UpdatedUtc { get; set; }
    public SettingsEdit Command() => new()
    {
        Revision = Revision,
        CommandId = CommandId,
        OnlineEnabled = OnlineEnabled,
        DurationsApproved = DurationsApproved,
        ReviewStartMinutes = ReviewStartMinutes,
        ReviewEndMinutes = ReviewEndMinutes,
        ReviewDays = ReviewDays,
        StaffEmails = (StaffEmails ?? "").Split([',', ';', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
        VenueName = VenueName,
        ContactEmail = ContactEmail ?? "",
        ContactPhone = ContactPhone ?? "",
        BookingPolicy = BookingPolicy ?? "",
        PrivacyPolicy = PrivacyPolicy ?? "",
        VenueRules = VenueRules ?? "",
        PoliciesPublished = PoliciesPublished
    };
    public static SettingsForm From(SettingsEdit edit) => new()
    {
        Revision = edit.Revision,
        OnlineEnabled = edit.OnlineEnabled,
        DurationsApproved = edit.DurationsApproved,
        ReviewStartMinutes = edit.ReviewStartMinutes,
        ReviewEndMinutes = edit.ReviewEndMinutes,
        ReviewDays = edit.ReviewDays,
        StaffEmails = string.Join("\n", edit.StaffEmails),
        VenueName = edit.VenueName,
        ContactEmail = edit.ContactEmail,
        ContactPhone = edit.ContactPhone,
        BookingPolicy = edit.BookingPolicy,
        PrivacyPolicy = edit.PrivacyPolicy,
        VenueRules = edit.VenueRules,
        PoliciesPublished = edit.PoliciesPublished,
        UpdatedUtc = edit.UpdatedUtc
    };
}
