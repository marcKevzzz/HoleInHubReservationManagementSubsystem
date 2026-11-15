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
    [InlineData("Dela Cruz, Jr.", true)]
    [InlineData("김민준", true)]
    [InlineData("Player2", false)]
    [InlineData("Guest٣", false)]
    [InlineData("", false)]
    public void NamesAllowRealNameCharactersButNeverNumbers(string value, bool valid) => Assert.Equal(valid, ContactRules.IsName(value));

    [Theory]
    [InlineData("09123456789", true)]
    [InlineData("639123456789", true)]
    [InlineData("+639123456789", true)]
    [InlineData("01012345678", true)]
    [InlineData("0212345678", true)]
    [InlineData("+821012345678", true)]
    [InlineData("821012345678", true)]
    [InlineData("+8221234567", true)]
    [InlineData("+82212345678", true)]
    [InlineData("+63281234567", true)]
    [InlineData("0912345678", false)]
    [InlineData("091234567890", false)]
    [InlineData("63912345678", false)]
    [InlineData("+6391234567890", false)]
    [InlineData("+6309123456789", false)]
    [InlineData("0101234567", false)]
    [InlineData("010123456789", false)]
    [InlineData("+82101234567", false)]
    [InlineData("8210123456789", false)]
    [InlineData("+8201012345678", false)]
    [InlineData("02123456", false)]
    [InlineData("02123456789", false)]
    [InlineData("+14155552671", true)]
    [InlineData("+63+9123456789", false)]
    [InlineData("+", false)]
    [InlineData("0912 345 6789", false)]
    [InlineData("123456", false)]
    [InlineData("1234567890123456", false)]
    [InlineData("１２３４５６７８９", false)]
    public void PhonesAcceptLocalAndInternationalAsciiNumbers(string value, bool valid) => Assert.Equal(valid, ContactRules.IsPhone(value));
}
