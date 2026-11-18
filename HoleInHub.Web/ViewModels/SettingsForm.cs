using System.ComponentModel.DataAnnotations;
using HoleInHub.Business.Settings;
namespace HoleInHubReservationManagementSubsystem.ViewModels;

public sealed class SettingsForm
{
    internal const string DefaultBookingPolicy = """
        RESERVATIONS AND CONFIRMATION
        Availability browsing does not reserve a bay. Submitting an ordinary online request creates a Pending reservation and holds the requested capacity while staff review it. Current review windows are 30 minutes for Regular requests and 60 minutes for VIP and coaching requests. Review time runs during configured staff coverage within venue operating hours. Your receipt or booking status shows the current deadline.

        If the review deadline passes, the capacity hold is released and the request is marked overdue. Staff may confirm an overdue request before its session starts only if the requested capacity can be reserved again. An unresolved request expires when its session starts. Coaching also needs coach confirmation. A reservation is confirmed only when its status says Confirmed.

        WHOLE-SHOP BOOKINGS AND DEPOSITS
        Whole-shop rental reserves all active bays and requires verification of a 50% deposit before confirmation. The deposit deadline and instructions shown for the request apply. The current demonstration deposit flow is simulated and does not collect real money. A simulated deposit status is not a payment receipt; any live payment flow will present its terms before payment.

        PRICES, CANCELLATIONS, AND CHANGES
        Review the selected service, schedule, guest count, and price shown before submitting. You may cancel an eligible Pending or Confirmed ordinary booking online up to one hour before its scheduled start. Contact the venue for a later cancellation, whole-shop cancellation, or deposit refund. Change requests are reviewed by staff and depend on availability; the current booking remains in place unless a change is approved.

        Keep your account credentials and secure guest booking links private, and provide accurate contact and attendance details. A reservation reference alone does not authorize access to booking information. Contact holeinhub@gmail.com for booking questions.
        """;

    internal const string DefaultPrivacyPolicy = """
        WHO WE ARE
        Hole In Hub Golf & Lounge operates this reservation service and handles personal information collected through it. Contact holeinhub@gmail.com for privacy questions or requests.

        INFORMATION AND PURPOSES
        Depending on your use of the service, we collect your name, email address, contact number, account details, selected service, requested date and time, guest or attendee count, reservation status, and information you provide with a booking or support request. Verification, session, language, and security information is also processed to protect accounts and guest booking access.

        We use this information to create and secure accounts, verify email addresses, provide guest access to bookings, check availability, review and manage reservations, send booking updates, respond to questions, protect the service, and meet legal obligations. Availability and required-field checks may be automated; ordinary booking decisions are reviewed by venue staff. Processing may be necessary to provide or prepare the requested service, comply with law, pursue legitimate operational and security interests, or rely on consent where consent is required.

        SHARING, COOKIES, AND RETENTION
        Information is available to authorized venue staff and may be processed by providers that host the application, store records, maintain security, or deliver transactional email. Information may also be disclosed when required by law or needed for legal claims. If a payment integration is enabled, necessary transaction information may be shared with the relevant provider.

        Essential cookies support core functions. Functional and analytics cookies are optional and remain off unless you enable them. We retain information only as long as needed for the stated purposes, legitimate business operations, legal obligations, or legal claims. Verification challenges expire under their configured limits. There is not yet a single approved deletion period for reservation and account records; retention periods differ by record type.

        YOUR RIGHTS AND SECURITY
        Subject to applicable law, you may request information about, access to, correction of, or objection to the processing of your personal data, or ask for blocking, erasure, or portability. Contact holeinhub@gmail.com to make a request. You may also complain to the National Privacy Commission of the Philippines. We use safeguards intended to protect information, but no online service can guarantee absolute security.
        """;

    internal const string DefaultVenueRules = """
        OPENING HOURS AND CAPACITY
        The venue is open Monday from 11:00 AM to midnight and Tuesday through Sunday from 10:00 AM to midnight, Asia/Manila time. The available times shown online control on holidays, special events, and temporary closures.

        Each bay accommodates up to six players. Total guest capacity is up to eight in a Regular bay and up to ten in a VIP bay. The participant limit shown for your selected service and reservation confirmation controls. Coaching uses a Regular bay. A whole-shop rental reserves all active bays.

        ARRIVAL AND EQUIPMENT
        Arrive ready to use the bay at the confirmed start time. A late arrival does not automatically extend the session. Any extension needs staff approval and available capacity. Staff manage attendance and no-show decisions; the website does not automatically mark a booking as a no-show.

        Use simulators and other venue equipment carefully and follow staff instructions. Keep the bay safe for your group and other guests. Tell staff if equipment is damaged or not working. Staff may pause equipment use for safety, maintenance, or proper operation.

        Online cancellation and change rules are described in the Terms and Conditions and your booking status. Contact holeinhub@gmail.com with questions about venue rules.
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
