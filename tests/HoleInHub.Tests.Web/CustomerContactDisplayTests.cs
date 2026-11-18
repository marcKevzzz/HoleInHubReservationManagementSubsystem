using HoleInHub.Business.Customers;
using HoleInHubReservationManagementSubsystem.ViewModels;
using Xunit;

namespace HoleInHub.Tests.Web;

public sealed class CustomerContactDisplayTests
{
    private static CustomerContact Contact(string email, string phone) =>
        new("Alex", "Santos", email, phone, "booking", DateTime.UnixEpoch);

    [Fact]
    public void EmailsAppearOnceWithoutDiscardingDifferentAddresses()
    {
        var contacts = new[] { Contact(" ALEX@example.test ", ""), Contact("alex+golf@example.test", ""), Contact("", "") };
        Assert.Equal(["alex@example.test", "alex+golf@example.test"], CustomerLabels.ContactEmails("alex@example.test", contacts));
    }

    [Fact]
    public void LocalAndInternationalFormattingDoNotRepeatTheSamePhone()
    {
        var contacts = new[] { Contact("", "+63 917 555 0123"), Contact("", "639175550123"), Contact("", "(0917) 555-0123"), Contact("", "09990001003") };
        Assert.Equal(["+63 917 555 0123", "+63 999 000 1003"], CustomerLabels.ContactPhones("09175550123", contacts));
    }

    [Fact]
    public void MissingContactsDoNotCreateEmptyEntries()
    {
        var contacts = new[] { Contact(" ", " ") };
        Assert.Empty(CustomerLabels.ContactEmails("", contacts));
        Assert.Empty(CustomerLabels.ContactPhones("", contacts));
        Assert.Single(CustomerLabels.ContactEmails("alex@example.test", []));
    }
}
