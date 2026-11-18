namespace HoleInHubReservationManagementSubsystem.ViewModels;

public sealed record ReasonConfirmationModel(string Id, string FormId, string Title, string Description,
    string FieldName, string Value, string ConfirmLabel);
