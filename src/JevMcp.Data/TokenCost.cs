using System.Globalization;

namespace JevMcp.Data;

public static class TokenCost
{
    public static decimal ComputeUsd(int inputTokens, int outputTokens, decimal usdPerMillion)
    {
        if (usdPerMillion <= 0)
        {
            return 0;
        }

        var total = (decimal)inputTokens + outputTokens;
        return Math.Round(total * usdPerMillion / 1_000_000m, 10, MidpointRounding.AwayFromZero);
    }

    public static string FormatUsd(decimal cost) =>
        cost.ToString("F10", CultureInfo.InvariantCulture);
}
