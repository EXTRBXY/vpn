using NothingVpn.Domain.Updates;

namespace NothingVpn.Application.Tests;

public sealed class SemanticVersionPolicyTests
{
    [Theory]
    [InlineData("1.2.3", "1.2.3")]
    [InlineData("v2.4", "2.4.0")]
    [InlineData("V3.1.5-beta", "3.1.5")]
    public void Normalize_ValidVersion_ReturnsThreeComponents(string input, string expected)
    {
        Assert.Equal(expected, SemanticVersionPolicy.Normalize(input));
    }

    [Fact]
    public void Compare_OrdersVersionsNumerically()
    {
        Assert.True(SemanticVersionPolicy.Compare("1.10.0", "1.9.9") > 0);
        Assert.True(SemanticVersionPolicy.Compare("2.0.0", "10.0.0") < 0);
        Assert.Equal(0, SemanticVersionPolicy.Compare("v1.2", "1.2.0"));
    }

    [Fact]
    public void Compare_InvalidVersion_IsOlderThanValidVersion()
    {
        Assert.True(SemanticVersionPolicy.Compare("invalid", "1.0.0") < 0);
        Assert.Equal(0, SemanticVersionPolicy.Compare("invalid", null));
    }

    [Fact]
    public void ToGitTag_NormalizesPrefix()
    {
        Assert.Equal("v1.2.3", SemanticVersionPolicy.ToGitTag("V1.2.3"));
        Assert.Equal(string.Empty, SemanticVersionPolicy.ToGitTag("invalid"));
    }
}
