using System.ComponentModel.DataAnnotations;
using HoleInHub.Business.Customers;
using HoleInHub.Business.Reservations;

namespace HoleInHubReservationManagementSubsystem.ViewModels;

public sealed class CustomerMergeForm
{
    [Required, StringLength(100)] public string SourceId { get; set; } = "";
    [Required, StringLength(100)] public string TargetId { get; set; } = "";
    [Required, StringLength(32)] public string OperationId { get; set; } = Guid.NewGuid().ToString("N");
    public long SourceContactRevision { get; set; }
    public long SourceMembershipRevision { get; set; }
    public long TargetContactRevision { get; set; }
    public long TargetMembershipRevision { get; set; }
    [Required] public string NameFrom { get; set; } = "target";
    [Required] public string PhoneFrom { get; set; } = "target";
    [Required] public string EmailFrom { get; set; } = "target";
    [Required] public string BookingScope { get; set; } = "all";
    public string[] ReservationIds { get; set; } = [];
    [Required(ErrorMessage = "Enter a reason for combining these records."), StringLength(500)] public string Reason { get; set; } = "";
    public CustomerMergeCommand Command() => new(OperationId, SourceId, TargetId, SourceContactRevision, SourceMembershipRevision, TargetContactRevision, TargetMembershipRevision, NameFrom, PhoneFrom, EmailFrom,
        BookingScope == "all" ? null : ReservationIds, Reason);
}
public sealed record CustomerMergeModel(CustomerProfile Source, StaffCustomerDirectory Candidates, CustomerMergePreview? Preview,
    CustomerMergeForm Form, string Search, string? Error = null);
public sealed class CustomerUndoForm
{
    [Required, StringLength(32)] public string OperationId { get; set; } = "";
    [Required, StringLength(32)] public string CommandId { get; set; } = Guid.NewGuid().ToString("N");
    [Required, StringLength(500)] public string Reason { get; set; } = "";
}
