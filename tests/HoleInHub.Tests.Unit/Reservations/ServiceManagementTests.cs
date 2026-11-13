using HoleInHub.Business.Services;
using Xunit;
namespace HoleInHub.Tests.Unit.Services;
public sealed class ServiceManagementTests
{
    private static ServiceEdit Edit => new("service-test", 0, Guid.NewGuid().ToString("N"), true,
        "Golf session", "A short description", "₱600 / hour", "Detailed description", "Regular", "Book",
        "Request a booking", 0, false, "None", 0, []);
    [Fact]
    public void DepositsSupportNonePesoAndPercentageAndRejectInvalidAmounts()
    {
        ServiceCatalog.Validate(Edit);
        ServiceCatalog.Validate(Edit with { DepositType = "Peso", DepositValue = 500.50m });
        ServiceCatalog.Validate(Edit with { DepositType = "Percentage", DepositValue = 30m });
        foreach (var value in new[] { 0m, -1m, 100.01m, 0.001m })
        {
            Assert.Throws<ArgumentException>(() => ServiceCatalog.Validate(Edit with { DepositType = "Percentage", DepositValue = value }));
        }

        Assert.Throws<ArgumentException>(() => ServiceCatalog.Validate(Edit with { DepositType = "Other" }));
    }
    [Fact]
    public void ServiceOrderRejectsDuplicatesAndInvalidRevisions()
    {
        ServiceCatalog.ValidateOrder([new("VIP", 3), new("Regular", 1)]);
        Assert.Throws<ArgumentException>(() => ServiceCatalog.ValidateOrder([]));
        Assert.Throws<ArgumentException>(() => ServiceCatalog.ValidateOrder([new("VIP", 1), new("VIP", 1)]));
        Assert.Throws<ArgumentException>(() => ServiceCatalog.ValidateOrder([new("VIP", -1)]));
    }
    [Fact]
    public void CustomerImagesAndTextAreValidatedBeforePersistence()
    {
        Assert.Throws<ArgumentException>(() => ServiceCatalog.Validate(Edit with { BookingService = "Unknown" }));
        Assert.Throws<ArgumentException>(() => ServiceCatalog.Validate(Edit with { Images = ["https://example.test/image.jpg"] }));
        Assert.Throws<ArgumentException>(() => ServiceCatalog.Validate(Edit with { Images = ["/media/../secret"] }));
        Assert.Throws<ArgumentException>(() => ServiceCatalog.ImageType("<svg xmlns='http://www.w3.org/2000/svg'></svg>"u8.ToArray()));
        Assert.Equal("image/png", ServiceCatalog.ImageType(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10, 0, 0, 0, 0 }));
        Assert.Equal("image/webp", ServiceCatalog.ImageType("RIFF1234WEBP"u8.ToArray()));
        Assert.Contains("30%", ServiceCatalog.FormatDeposit("Percentage", 30));
    }
}
