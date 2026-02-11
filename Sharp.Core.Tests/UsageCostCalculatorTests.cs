using Sharp.AI;
using Sharp.Core.Configuration;

namespace Sharp.Core.Tests;

public sealed class UsageCostCalculatorTests
{
    [Fact]
    public void Calculate_WithModelPricing_ComputesExpectedBreakdown()
    {
        var pricing = new ModelPricing(
            InputPerMillionTokens: 2.5m,
            OutputPerMillionTokens: 10m,
            CacheReadPerMillionTokens: 0.5m,
            CacheWritePerMillionTokens: 3m);

        var cost = UsageCostCalculator.Calculate(
            pricing,
            inputTokens: 100_000,
            outputTokens: 20_000,
            cacheReadTokens: 10_000,
            cacheWriteTokens: 5_000);

        Assert.Equal(0.25m, cost.Input);
        Assert.Equal(0.2m, cost.Output);
        Assert.Equal(0.005m, cost.CacheRead);
        Assert.Equal(0.015m, cost.CacheWrite);
        Assert.Equal(0.47m, cost.Total);
    }

    [Fact]
    public void AttachCost_NoPricing_LeavesUsageWithZeroCost()
    {
        var usage = new Usage(
            InputTokens: 123,
            OutputTokens: 45,
            CacheReadTokens: 0,
            CacheWriteTokens: 0,
            Cost: new CostBreakdown(0m, 0m, 0m, 0m, 0m));

        var priced = UsageCostCalculator.AttachCost(usage, pricing: null);

        Assert.NotNull(priced);
        Assert.Equal(0m, priced!.Cost.Total);
        Assert.Equal(0m, priced.Cost.Input);
        Assert.Equal(0m, priced.Cost.Output);
    }

    [Fact]
    public void ToModelPricing_AllZeroValues_ReturnsNull()
    {
        var pricing = AgentConfig.ToModelPricing(new ModelPricingConfig
        {
            InputPerMillionTokens = 0m,
            OutputPerMillionTokens = 0m,
            CacheReadPerMillionTokens = 0m,
            CacheWritePerMillionTokens = 0m
        });

        Assert.Null(pricing);
    }

    [Fact]
    public void ToModelPricing_NegativeValue_ThrowsJsonException()
    {
        Assert.Throws<System.Text.Json.JsonException>(() =>
            AgentConfig.ToModelPricing(new ModelPricingConfig
            {
                InputPerMillionTokens = -0.1m
            }));
    }
}
