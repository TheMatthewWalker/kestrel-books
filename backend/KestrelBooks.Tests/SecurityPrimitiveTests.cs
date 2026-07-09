using System.Text;
using KestrelBooks.Api.Services;
using Xunit;

namespace KestrelBooks.Tests;

public class TotpTests
{
    // RFC 6238 Appendix B test secret (ASCII "12345678901234567890").
    private static readonly byte[] RfcSecret = Encoding.ASCII.GetBytes("12345678901234567890");

    [Theory]
    // RFC vectors give 8-digit codes; the 6-digit code is the last 6 of each.
    [InlineData(59, "287082")]          // 1970-01-01 00:00:59 → 94287082
    [InlineData(1111111109, "081804")]  // 2005-03-18 01:58:29 → 07081804
    [InlineData(1234567890, "005924")]  // 2009-02-13 23:31:30 → 89005924
    public void MatchesRfc6238Vectors(long unixSeconds, string expected)
    {
        var t = DateTime.UnixEpoch.AddSeconds(unixSeconds);
        Assert.True(Totp.Verify(RfcSecret, expected, t));
    }

    [Fact]
    public void RejectsWrongCode()
    {
        Assert.False(Totp.Verify(RfcSecret, "000000", DateTime.UnixEpoch.AddSeconds(59)));
    }

    [Fact]
    public void AcceptsAdjacentTimeStep()
    {
        // Code for t=59 (step 1) still accepted 30s later (step 2) via the ±1 window.
        Assert.True(Totp.Verify(RfcSecret, "287082", DateTime.UnixEpoch.AddSeconds(89)));
    }

    [Fact]
    public void Base32_EncodesKnownValue()
    {
        Assert.Equal("MZXW6YTBOI", Totp.ToBase32(Encoding.ASCII.GetBytes("foobar")));
    }
}
