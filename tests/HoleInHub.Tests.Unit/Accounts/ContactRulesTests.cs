using HoleInHub.Business.Accounts;
using Xunit;

namespace HoleInHub.Tests.Unit.Accounts;

public sealed class ContactRulesTests
{
    [Theory]
    [InlineData("María-José", true)]
    [InlineData("O’Connor", true)]
    [InlineData("王小明", true)]
    [InlineData("Anne Marie", true)]
    [InlineData("Player2", false)]
    [InlineData("Guest٣", false)]
    [InlineData("", false)]
    public void NamesAllowRealNameCharactersButNeverNumbers(string value, bool valid) => Assert.Equal(valid, ContactRules.IsName(value));

    [Theory]
    [InlineData("09123456789", true)]
    [InlineData("639123456789", true)]
    [InlineData("+639123456789", false)]
    [InlineData("0912 345 6789", false)]
    [InlineData("123456", false)]
    [InlineData("1234567890123456", false)]
    [InlineData("１２３４５６７８９", false)]
    public void PhonesAcceptSevenToFifteenAsciiDigitsOnly(string value, bool valid) => Assert.Equal(valid, ContactRules.IsPhone(value));
}
