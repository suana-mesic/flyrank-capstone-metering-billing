namespace BillingApi.Services
{
    public sealed record PricingConfig(
    decimal PricePerApiCall,
    decimal PricePerInputToken,
    decimal PricePerCachedInputToken,
    decimal PricePerOutputToken
);
    public sealed class PricingService
    {
        private static readonly PricingConfig Config = new(
        PricePerApiCall: 0.001m,
        PricePerInputToken: 0.0000015m,
        PricePerCachedInputToken: 0.00000075m,
        PricePerOutputToken: 0.000006m
    );

        public decimal CalculateCost(long apiCalls, long inputTokens, long cachedInputTokens, long outputTokens)
        {
            var callCost = apiCalls * Config.PricePerApiCall;
            var inputCost = inputTokens * Config.PricePerInputToken;
            var cachedCost = cachedInputTokens * Config.PricePerCachedInputToken;
            var outputCost = outputTokens * Config.PricePerOutputToken;

            return callCost + inputCost + cachedCost + outputCost;
        }
    }
}
