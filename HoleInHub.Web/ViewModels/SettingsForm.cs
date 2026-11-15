using System.ComponentModel.DataAnnotations;
using HoleInHub.Business.Settings;
namespace HoleInHubReservationManagementSubsystem.ViewModels;

public sealed class SettingsForm
{
    internal const string DefaultBookingPolicy = """
        Reservations depend on availability. Staff review regular bay, VIP, and coaching requests. Coaching also requires coach confirmation. Whole-shop rentals require a verified 50% deposit. Check your reservation status for the decision and any deadline.

        You can cancel an eligible pending or confirmed booking online up to one hour before the session. Staff review whole-shop cancellation requests. Contact the venue about changes or deposit refunds.
        """;

    internal const string DefaultPrivacyPolicy = """
        Account and reservation forms collect your name, email, contact number, selected services, attendance count, and booking details to manage your reservation request.

        Your account-linked reservations are available to you and authorized venue staff. Email messages support account verification, password recovery, and reservation updates. For questions about your information, contact holeinhub@gmail.com.
        """;

    internal const string DefaultVenueRules = """
        Opening hours are Monday 11 AM to midnight and Tuesday through Sunday 10 AM to midnight (Manila time).

        Each bay can accommodate up to six players. Total capacity is eight guests in a Regular bay and ten guests in a VIP bay. Please follow staff instructions when using the simulators and equipment.
        """;

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
    [StringLength(16), RegularExpression(HoleInHub.Business.Accounts.ContactRules.PhonePattern, ErrorMessage = HoleInHub.Business.Accounts.ContactRules.PhoneError)] public string? ContactPhone { get; set; }
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
        BookingPolicy = string.IsNullOrWhiteSpace(edit.BookingPolicy) ? DefaultBookingPolicy : edit.BookingPolicy,
        PrivacyPolicy = string.IsNullOrWhiteSpace(edit.PrivacyPolicy) ? DefaultPrivacyPolicy : edit.PrivacyPolicy,
        VenueRules = string.IsNullOrWhiteSpace(edit.VenueRules) ? DefaultVenueRules : edit.VenueRules,
        PoliciesPublished = edit.PoliciesPublished,
        UpdatedUtc = edit.UpdatedUtc
    };
}
