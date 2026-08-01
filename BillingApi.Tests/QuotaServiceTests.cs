using BillingApi.Services;
using Xunit;

namespace BillingApi.Tests;

public class QuotaServiceTests
{
    private readonly QuotaService _quota = new();

    [Fact]
    public void Check_JustUnderApiCallLimit_IsAllowed()
    {
        // 999 used, limit 1000, requesting 1 -> lands exactly on 1000 -> allowed
        var result = _quota.Check(
            callsUsed: 999, apiCallLimit: 1000, callsRequested: 1,
            tokensUsed: 0, tokenLimit: 100000, tokensRequested: 0);

        Assert.Equal(QuotaResult.Allowed, result);
    }

    [Fact]
    public void Check_AtApiCallLimit_IsRefusedWith429Semantics()
    {
        // 1000 used, limit 1000, requesting 1 -> would be 1001 -> refused
        var result = _quota.Check(
            callsUsed: 1000, apiCallLimit: 1000, callsRequested: 1,
            tokensUsed: 0, tokenLimit: 100000, tokensRequested: 0);

        Assert.Equal(QuotaResult.ApiCallLimitExceeded, result);
    }

    [Fact]
    public void Check_OverTokenLimit_IsRefusedWith402Semantics()
    {
        // api calls fine, but tokens exceed the limit -> token refusal
        var result = _quota.Check(
            callsUsed: 0, apiCallLimit: 1000, callsRequested: 1,
            tokensUsed: 99_000, tokenLimit: 100_000, tokensRequested: 5_000);

        Assert.Equal(QuotaResult.TokenLimitExceeded, result);
    }

    [Fact]
    public void Check_BothUnderLimits_IsAllowed()
    {
        var result = _quota.Check(
            callsUsed: 10, apiCallLimit: 1000, callsRequested: 1,
            tokensUsed: 500, tokenLimit: 100000, tokensRequested: 50);

        Assert.Equal(QuotaResult.Allowed, result);
    }
}