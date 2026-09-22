using JevMcp.Data;

namespace JevMcp.Tests.Data;

public sealed class TokenCostTests
{
    [Fact]
    public void ComputeUsdUsesTenDecimalPlaces()
    {
        var cost = TokenCost.ComputeUsd(1_000_000, 0, 0.042m);
        Assert.Equal(0.0420000000m, cost);
        Assert.Equal("0.0420000000", TokenCost.FormatUsd(cost));
    }

    [Fact]
    public void ComputeUsdSumsInputAndOutput()
    {
        var cost = TokenCost.ComputeUsd(11, 3, 0.042m);
        Assert.Equal(0.0000005880m, cost);
    }

    [Fact]
    public void ZeroRateYieldsZeroCost()
    {
        Assert.Equal(0m, TokenCost.ComputeUsd(100, 100, 0m));
    }
}
