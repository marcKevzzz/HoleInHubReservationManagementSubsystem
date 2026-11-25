using HoleInHub.Business.Reservations;
using Xunit;

namespace HoleInHub.Tests.Unit.Reservations;

public sealed class FamilyRosterTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(8)]
    public void AdultsCanBookUpToTheClassCeiling(int count)
    {
        var request = new SubmitReservationRequest
        {
            Learners = Enumerable.Range(0, count).Select(_ => new FamilyLearnerInput
            {
                FirstName = "Alex",
                LastName = "Cruz",
                Age = 18
            }).ToArray()
        };
        var roster = ReservationService.BuildFamilyRoster(request);
        Assert.Equal(count, roster.Learners.Select(x => x.Id).Distinct().Count());
        Assert.Empty(roster.Guardians);
    }

    [Fact]
    public void EachChildCanHaveTheirOwnGuardian()
    {
        var request = new SubmitReservationRequest
        {
            Learners = [new() { FirstName = "Alex", LastName = "Cruz", Age = 17, GuardianIndex = 0 },
                new() { FirstName = "Sam", LastName = "Cruz", Age = 8, GuardianIndex = 1 }],
            Guardians = [new() { Name = "Rosa", Relationship = "Parent" },
                new() { Name = "Ana", Relationship = "Guardian" }]
        };
        var roster = ReservationService.BuildFamilyRoster(request);
        Assert.Equal(2, roster.Guardians.Length);
        Assert.NotEqual(roster.Learners[0].GuardianId, roster.Learners[1].GuardianId);
    }

    [Fact]
    public void AChildNeedsAValidGuardian()
    {
        var request = new SubmitReservationRequest { Learners = [new() { FirstName = "Sam", LastName = "Cruz", Age = 8 }] };
        Assert.Throws<ReservationValidationException>(() => ReservationService.BuildFamilyRoster(request));
    }
}
