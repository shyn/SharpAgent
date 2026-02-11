namespace Sharp.AI.Infrastructure;

public static class UsageCostCalculator
{
    private const decimal PerMillionTokens = 1_000_000m;

    public static Usage? AttachCost(Usage? usage, ModelPricing? pricing)
    {
        if (usage is null)
            return null;

        var cost = Calculate(
            pricing,
            usage.InputTokens,
            usage.OutputTokens,
            usage.CacheReadTokens,
            usage.CacheWriteTokens);

        return usage with { Cost = cost };
    }

    public static CostBreakdown Calculate(
        ModelPricing? pricing,
        int inputTokens,
        int outputTokens,
        int cacheReadTokens,
        int cacheWriteTokens)
    {
        if (pricing is null)
            return new CostBreakdown(0m, 0m, 0m, 0m, 0m);

        var input = CalculateLine(pricing.InputPerMillionTokens, inputTokens);
        var output = CalculateLine(pricing.OutputPerMillionTokens, outputTokens);
        var cacheRead = CalculateLine(pricing.CacheReadPerMillionTokens, cacheReadTokens);
        var cacheWrite = CalculateLine(pricing.CacheWritePerMillionTokens, cacheWriteTokens);
        var total = input + output + cacheRead + cacheWrite;
        return new CostBreakdown(input, output, cacheRead, cacheWrite, total);
    }

    private static decimal CalculateLine(decimal pricePerMillionTokens, int tokens)
    {
        if (pricePerMillionTokens <= 0m || tokens <= 0)
            return 0m;

        return decimal.Round(pricePerMillionTokens * tokens / PerMillionTokens, 8, MidpointRounding.AwayFromZero);
    }
}
