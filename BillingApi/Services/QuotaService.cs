namespace BillingApi.Services;

public enum QuotaResult
{
    Allowed,
    ApiCallLimitExceeded,
    TokenLimitExceeded
}

public sealed class QuotaService
{
    // Pure decision: does this request fit within the plan's quota?
    // "At quota" (used == limit) means the next unit is refused.
    // Order matters: api-call limit is checked before token limit.
    public QuotaResult Check(
        long callsUsed, long apiCallLimit, int callsRequested,
        long tokensUsed, long tokenLimit, int tokensRequested)
    {
        if (callsUsed + callsRequested > apiCallLimit)
            return QuotaResult.ApiCallLimitExceeded;

        if (tokensUsed + tokensRequested > tokenLimit)
            return QuotaResult.TokenLimitExceeded;

        return QuotaResult.Allowed;
    }
}