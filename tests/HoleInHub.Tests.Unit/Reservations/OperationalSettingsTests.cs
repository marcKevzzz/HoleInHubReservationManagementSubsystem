using HoleInHub.Business.Settings;
using HoleInHub.Business.Reservations;
using Xunit;
namespace HoleInHub.Tests.Unit.Settings;
public sealed class OperationalSettingsTests
{
    private static SettingsEdit Valid() => new() { OnlineEnabled=true,DurationsApproved=true,ReviewStartMinutes=600,ReviewEndMinutes=1440,ReviewDays=[1,2,3,4,5,6,0],StaffEmails=["staff@example.test"] };
    [Fact] public void OpeningRequestsRequiresApprovedCoverageAndRecipients()
    {
        OperationalSettings.Validate(Valid());
        var edit=Valid();edit.StaffEmails=[];Assert.Throws<ArgumentException>(()=>OperationalSettings.Validate(edit));
        edit=Valid();edit.DurationsApproved=false;Assert.Throws<ArgumentException>(()=>OperationalSettings.Validate(edit));
        edit=Valid();edit.ReviewDays=[1,1];Assert.Throws<ArgumentException>(()=>OperationalSettings.Validate(edit));
        edit=Valid();edit.ReviewEndMinutes=600;Assert.Throws<ArgumentException>(()=>OperationalSettings.Validate(edit));
    }
    [Fact] public void RuntimePolicyUsesSavedCoverageWithoutEnablingPayments()
    {
        var edit=Valid();edit.OnlineEnabled=false;
        var policy=OperationalSettings.Apply(edit,new ReservationPolicy { Approved=true,DemoPaymentsEnabled=false });
        Assert.False(policy.Approved);Assert.False(policy.DemoPaymentsEnabled);Assert.Equal(TimeSpan.FromHours(10),policy.ReviewStart);
        Assert.Throws<ReservationValidationException>(()=>policy.ValidateCoverage(DateTimeOffset.UtcNow));
    }
    [Fact] public void PublicationRequiresAllPolicies()
    {
        var edit=Valid();edit.PoliciesPublished=true;Assert.Throws<ArgumentException>(()=>OperationalSettings.Validate(edit));
        edit.BookingPolicy="Booking terms";edit.PrivacyPolicy="Privacy terms";edit.VenueRules="Venue rules";OperationalSettings.Validate(edit);
    }
}
