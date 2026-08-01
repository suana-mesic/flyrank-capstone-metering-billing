using BillingApi.Services;
using Xunit;

namespace BillingApi.Tests;

public class PricingServiceTests
{
    private readonly PricingService _pricing = new();

    [Fact]
    public void CalculateCost_WithPinnedInputs_ReturnsExactExpectedCost()
    {
        // PINNED inputs — fixed numbers, fixed expected result.
        // If someone changes the prices in PricingService, this test breaks.
        // apiCalls=100, input=1000, cached=200, output=500
        // 100*0.001      = 0.10000
        // 1000*0.0000015 = 0.00150
        // 200*0.00000075 = 0.00015
        // 500*0.000006   = 0.00300
        // -------------------------
        // total          = 0.10465
        var cost = _pricing.CalculateCost(
            apiCalls: 100, inputTokens: 1000, cachedInputTokens: 200, outputTokens: 500);

        Assert.Equal(0.10465m, cost);
    }

    [Fact]
    public void CachedInputTokens_AreCheaperThanNormalInputTokens()
    {
        // GOTCHA #1: cached input is cheaper than normal input.
        // For the same token count, cached must cost less.
        var normalCost = _pricing.CalculateCost(0, inputTokens: 1000, 0, 0);
        var cachedCost = _pricing.CalculateCost(0, 0, cachedInputTokens: 1000, 0);

        Assert.True(cachedCost < normalCost);
        Assert.Equal(normalCost / 2, cachedCost); // cached is exactly half price here
    }

    [Fact]
    public void ReasoningTokens_AreBilledAsOutput_NotAsSeparateCategory()
    {
        // GOTCHA #2: reasoning tokens are PART of output, not a separate category.
        // If the model "thinks" for 300 tokens then writes a 200-token answer,
        // that is 500 OUTPUT tokens total — billed at the output rate,
        // with no additional "reasoning" charge.
        var answerOnly = 200L;
        var reasoning = 300L;
        var totalOutput = answerOnly + reasoning; // 500

        var cost = _pricing.CalculateCost(0, 0, 0, outputTokens: totalOutput);

        Assert.Equal(500 * 0.000006m, cost); // = 0.003, all at output rate
    }
}