using JellyFlix.Profiles.Security;
using Xunit;

namespace JellyFlix.Profiles.Tests;

public class PinHasherTests
{
    [Theory]
    [InlineData("1234", true)]
    [InlineData("0000", true)]
    [InlineData("123", false)]
    [InlineData("12345", false)]
    [InlineData("12a4", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsValidPin_RequiresFourDigits(string? pin, bool expected)
    {
        Assert.Equal(expected, PinHasher.IsValidPin(pin));
    }

    [Fact]
    public void Verify_MatchesOnlyTheHashedPin()
    {
        var hash = PinHasher.Hash("4821");

        Assert.True(PinHasher.Verify("4821", hash));
        Assert.False(PinHasher.Verify("4822", hash));
        Assert.False(PinHasher.Verify(null, hash));
    }

    [Fact]
    public void Hash_IsSalted()
    {
        Assert.NotEqual(PinHasher.Hash("4821"), PinHasher.Hash("4821"));
    }
}
