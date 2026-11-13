using System.ComponentModel.DataAnnotations;
using HoleInHub.Business.Services;
namespace HoleInHubReservationManagementSubsystem.ViewModels;

public sealed class ServiceEditorForm
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
    public string[] Images { get; set; } = [];
    public List<IFormFile> Uploads { get; set; } = [];
    public ServiceEdit Command()
    {
        if (RateAmount is null || RateUnit is not ("Peso" or "Percentage") || RateUnit == "Percentage" && RateAmount > 100)
        {
            throw new ArgumentException("Enter a valid rate, up to 100% or ₱1,000,000.");
        }
        var original = ParseRate(RateText);
        var rate = !Create && original == RateAmount && RateUnit == (RateText.Contains('%') ? "Percentage" : "Peso")
            ? RateText : ServiceCatalog.FormatDeposit(RateUnit, RateAmount.Value).Replace(" deposit", "");
        return new(Id, Revision, CommandId, Create, Title, Summary, rate, string.IsNullOrWhiteSpace(Details) ? Summary : Details,
        BookingService, Action, ActionLabel, Order, Active, DepositType, DepositValue, Images)
        { MaxPax = MaxPax, ImageOrder = ImageOrder };
    }
    private static decimal? ParseRate(string text) => decimal.TryParse(System.Text.RegularExpressions.Regex.Match(text, @"[0-9][0-9,]*(?:\.[0-9]+)?").Value.Replace(",", ""), System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var value) ? value : null;
    public static ServiceEditorForm From(ServiceEdit item) => new()
    {
        MaxPax = item.MaxPax,
        RateAmount = ParseRate(item.RateText),
        RateUnit = item.RateText.Contains('%') ? "Percentage" : "Peso",
        Id = item.Id,
        Revision = item.Revision,
        CommandId = item.CommandId,
        Create = false,
        Title = item.Title,
        Summary = item.Summary,
        RateText = item.RateText,
        Details = item.Details,
        BookingService = item.BookingService,
        Action = item.Action,
        ActionLabel = item.ActionLabel,
        Order = item.Order,
        Active = item.Active,
        DepositType = item.DepositType,
        DepositValue = item.DepositValue,
        Images = item.Images
    };
}
