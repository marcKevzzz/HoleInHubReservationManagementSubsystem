using System.ComponentModel.DataAnnotations;
using HoleInHub.Business.Services;
using HoleInHub.Business.Reservations;
using HoleInHub.Data.Services;
namespace HoleInHubReservationManagementSubsystem.ViewModels;

public sealed class ServiceAvailabilityRow
{
    public int? Weekday { get; set; }
    public string? Date { get; set; }
    public string? Start { get; set; } = "10:00";
    public string? End { get; set; } = "24:00";
    public string Mode { get; set; } = "window";

    public ServiceAvailabilityRule Rule()
    {
        if (Mode is not ("window" or "fixed" or "closed"))
        { throw new ArgumentException("Choose a valid booking format for each availability rule."); }
        return new(Weekday, string.IsNullOrWhiteSpace(Date) ? null : Date,
            Mode == "closed" ? 0 : Minutes(Start), Mode == "closed" ? 0 : Minutes(End), Mode == "fixed", Mode == "closed");
    }

    private static int Minutes(string? value) => value == "24:00" ? 1440 :
        TimeOnly.TryParseExact(value, "HH:mm", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var time)
            ? time.Hour * 60 + time.Minute : -1;

    public static ServiceAvailabilityRow From(ServiceAvailabilityRule rule) => new()
    {
        Weekday = rule.Weekday, Date = rule.Date,
        Start = $"{rule.StartMinute / 60:00}:{rule.StartMinute % 60:00}",
        End = $"{rule.EndMinute / 60:00}:{rule.EndMinute % 60:00}",
        Mode = rule.Closed ? "closed" : rule.FixedSession ? "fixed" : "window"
    };
}

public sealed partial class ServiceEditorForm
{
    [Required, StringLength(80)] public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public long Revision { get; set; }
    [Required] public string CommandId { get; set; } = Guid.NewGuid().ToString("N");
    public bool Create { get; set; } = true;
    [Required, StringLength(120)] public string Title { get; set; } = "";
    [Required, StringLength(500)] public string Summary { get; set; } = "";
    [DisplayFormat(ConvertEmptyStringToNull = false), StringLength(160)] public string RateText { get; set; } = "";
    [DisplayFormat(ConvertEmptyStringToNull = false), StringLength(12000)] public string Details { get; set; } = "";
    [Required] public string BookingService { get; set; } = "Regular";
    [DisplayFormat(ConvertEmptyStringToNull = false), StringLength(80)] public string BookingTypeName { get; set; } = "";
    [DisplayFormat(ConvertEmptyStringToNull = false), StringLength(80)] public string NewBookingTypeName { get; set; } = "";
    [Required] public string Action { get; set; } = "Book";
    [Required, StringLength(60)] public string ActionLabel { get; set; } = "Request a booking";
    [Range(0, 9999)] public int Order { get; set; }
    public bool Active { get; set; }
    [Required] public string DepositType { get; set; } = "None";
    public decimal DepositValue { get; set; }
    [Range(1, 80)] public int? MaxPax { get; set; }
    [Range(typeof(decimal), "0.01", "1000000")] public decimal? RateAmount { get; set; }
    public string RateUnit { get; set; } = "Peso";
    public string[] ImageOrder { get; set; } = [];
    public bool EditAvailability { get; set; }
    public string? ImpactAcknowledgement { get; set; }
    public List<ServiceAvailabilityRow> Availability { get; set; } = [];
    public string[] Images { get; set; } = [];
    public List<IFormFile> Uploads { get; set; } = [];
    public ServiceEdit Command()
    {
        var pricing = EditPricing ? PricingRulesFromForm() : null;
        if (RateAmount is null || RateAmount is <= 0 or > 1000000 || RateUnit != "Peso")
        {
            throw new ArgumentException("Enter a PHP rate above zero, up to ₱1,000,000.");
        }
        var original = ParseRate(RateText);
        var rate = pricing is not null ? PricingRules.Summary(BookingService, pricing)
            : !Create && original == RateAmount && !RateText.Contains('%')
                ? RateText : ServiceCatalog.FormatDeposit(RateUnit, RateAmount.Value).Replace(" deposit", "");
        var typeName = BookingTypeName == "__new" ? NewBookingTypeName.Trim() : BookingTypeName;
        if (BookingTypeName == "__new" && string.IsNullOrWhiteSpace(typeName)) { throw new ArgumentException("Enter the new booking type name."); }
        return new(Id, Revision, CommandId, Create, Title, Summary, rate, string.IsNullOrWhiteSpace(Details) ? Summary : Details,
        BookingService, "Book", "Request a booking", Order, Active, DepositType, DepositValue, Images)
        { MaxPax = MaxPax, Pricing = pricing, ImageOrder = ImageOrder, BookingTypeName = typeName,
            AvailabilityRules = EditAvailability ? Availability.Select(row => row.Rule()).ToArray() : null,
            ImpactAcknowledgement = ImpactAcknowledgement };
    }
    private static decimal? ParseRate(string text) => decimal.TryParse(System.Text.RegularExpressions.Regex.Match(text, @"[0-9][0-9,]*(?:\.[0-9]+)?").Value.Replace(",", ""), System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var value) ? value : null;
    public static ServiceEditorForm From(ServiceEdit item) => new ServiceEditorForm
    {
        MaxPax = item.MaxPax,
        Availability = (item.AvailabilityRules ?? ServiceAvailabilityRules.Defaults(item.BookingService)).Select(ServiceAvailabilityRow.From).ToList(),
        RateUnit = "Peso",
        Id = item.Id,
        Revision = item.Revision,
        CommandId = item.CommandId,
        Create = false,
        Title = item.Title,
        Summary = item.Summary,
        RateText = item.RateText,
        Details = item.Details,
        BookingService = item.BookingService,
        BookingTypeName = item.BookingTypeName,
        Action = item.Action,
        ActionLabel = item.ActionLabel,
        Order = item.Order,
        Active = item.Active,
        DepositType = item.DepositType,
        DepositValue = item.DepositValue,
        Images = item.Images
    }.LoadPricing(item.Pricing ?? PricingRules.Default(item.BookingService));
}
