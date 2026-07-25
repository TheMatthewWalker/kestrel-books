using KestrelBooks.Api.Domain;
using KestrelBooks.Api.Services;
using Xunit;

namespace KestrelBooks.Tests;

/// <summary>
/// Rule matching is pure logic, so most of this tests the predicate directly —
/// which is where the bugs would be. Ordering matters as much as matching: a
/// specific rule must be able to sit in front of a general one.
/// </summary>
public class BankRuleTests
{
    private static BankRule Rule(string match, RuleMatch type = RuleMatch.Contains,
        RuleDirection direction = RuleDirection.Any, decimal? min = null, decimal? max = null,
        Guid? bankAccountId = null, bool enabled = true, int priority = 100) => new()
    {
        Id = Guid.NewGuid(), Name = match, MatchText = match, MatchType = type,
        Direction = direction, MinAmount = min, MaxAmount = max,
        BankAccountId = bankAccountId, Enabled = enabled, Priority = priority,
    };

    private static readonly Guid Bank = Guid.NewGuid();

    [Fact]
    public void Contains_IsCaseInsensitive_BecauseBanksShout()
    {
        var rule = Rule("british gas");
        Assert.True(rule.Matches("DD BRITISH GAS 4471029", -85m, Bank));
        Assert.True(rule.Matches("british gas", -85m, Bank));
        Assert.False(rule.Matches("BRITISH TELECOM", -85m, Bank));
    }

    [Fact]
    public void StartsWith_AndExact_NarrowTheMatch()
    {
        Assert.True(Rule("SAGE", RuleMatch.StartsWith).Matches("SAGE SOFTWARE LTD", -30m, Bank));
        Assert.False(Rule("SAGE", RuleMatch.StartsWith).Matches("PAY SAGE SOFTWARE", -30m, Bank));

        Assert.True(Rule("BANK CHARGE", RuleMatch.Exact).Matches("  bank charge ", -5m, Bank));
        Assert.False(Rule("BANK CHARGE", RuleMatch.Exact).Matches("BANK CHARGES", -5m, Bank));
    }

    [Fact]
    public void Direction_SeparatesMoneyInFromMoneyOut()
    {
        var inbound = Rule("STRIPE", direction: RuleDirection.MoneyIn);
        Assert.True(inbound.Matches("STRIPE PAYOUT", 250m, Bank));
        Assert.False(inbound.Matches("STRIPE FEE", -12m, Bank));

        var outbound = Rule("STRIPE", direction: RuleDirection.MoneyOut);
        Assert.False(outbound.Matches("STRIPE PAYOUT", 250m, Bank));
        Assert.True(outbound.Matches("STRIPE FEE", -12m, Bank));
    }

    [Fact]
    public void AmountBounds_UseMagnitude_SoTheyWorkOnPaymentsOut()
    {
        var rule = Rule("AMAZON", min: 10m, max: 100m);
        Assert.False(rule.Matches("AMAZON", -5m, Bank));
        Assert.True(rule.Matches("AMAZON", -50m, Bank));
        Assert.False(rule.Matches("AMAZON", -500m, Bank));
    }

    [Fact]
    public void AccountScope_AndDisabledFlag_AreRespected()
    {
        var otherBank = Guid.NewGuid();
        Assert.False(Rule("X", bankAccountId: otherBank).Matches("XYZ", -1m, Bank));
        Assert.True(Rule("X", bankAccountId: Bank).Matches("XYZ", -1m, Bank));
        Assert.True(Rule("X").Matches("XYZ", -1m, Bank));            // null = any account
        Assert.False(Rule("X", enabled: false).Matches("XYZ", -1m, Bank));
    }

    [Fact]
    public void EmptyMatchText_NeverMatches_SoARuleCannotSwallowEverything()
    {
        Assert.False(Rule("").Matches("ANYTHING AT ALL", -1m, Bank));
    }

    [Fact]
    public void PriorityOrder_LetsASpecificRuleBeatAGeneralOne()
    {
        var rules = new[]
        {
            Rule("AMAZON", priority: 200),                 // general
            Rule("AMAZON PRIME", priority: 10),            // specific, checked first
        }.OrderBy(r => r.Priority).ToList();

        var winner = rules.First(r => r.Matches("AMAZON PRIME MEMBERSHIP", -8.99m, Bank));
        Assert.Equal("AMAZON PRIME", winner.MatchText);

        var fallback = rules.First(r => r.Matches("AMAZON MARKETPLACE", -40m, Bank));
        Assert.Equal("AMAZON", fallback.MatchText);
    }

    [Theory]
    [InlineData("DD BRITISH GAS 4471029", "DD BRITISH GAS")]
    [InlineData("CARD PAYMENT TO TESCO STORES 3421", "CARD PAYMENT TO")]
    [InlineData("12345 67890", "12345 67890")]
    public void SuggestedMatchText_KeepsTheStablePart_AndDropsReferenceNumbers(string description, string expected)
    {
        Assert.Equal(expected, BankRuleService.SuggestMatchText(description));
    }
}
