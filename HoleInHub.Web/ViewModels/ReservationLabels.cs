namespace HoleInHubReservationManagementSubsystem.ViewModels;

public static class ReservationLabels
{
    public static string History(string value) => value.StartsWith("Demo payment: ", StringComparison.Ordinal) ? "Payment: " + Payment(value[14..]) : value;
    public static string Service(string value) => value switch
    {
        "Regular" => "Regular bay",
        "VIP" => "VIP bay",
        "PhilippineCoach" => "Filipino coach",
        "KpgaPro" => "Korean pro coaching",
        "KpgaGroup" => "Korean lesson program",
        "WholeShop" => "Whole-shop rental",
        _ => "Reservation"
    };
    public static string Payment(string value) => value switch
    {
        "AwaitingDeposit" => "Awaiting deposit",
        "DepositReceived" => "Deposit received",
        "Failed" => "Payment failed — retry before expiry",
        "CheckoutCancelled" => "Checkout cancelled — retry before expiry",
        "LatePayment" => "Late payment — reconciliation required",
        _ => "No deposit required"
    };
}
