using System.Globalization;

namespace JevMcp.App;

/// <summary>
/// Card-side counterpart of the chart formatters: the same value has to read the same
/// way in the card and in the tooltip. Audit keeps <see cref="Data.TokenCost.FormatUsd"/>.
/// </summary>
internal static class UsageFormat
{
    public static string Whole(long value) => value.ToString("N0", CultureInfo.CurrentCulture);

    public static string Milliseconds(double? value) =>
        value is { } ms ? ms.ToString("N0", CultureInfo.CurrentCulture) + " ms" : "—";

    /// <summary>A window priced per million tokens lands below a cent; two decimals would read zero.</summary>
    public static string Usd(decimal value)
    {
        if (value == 0)
        {
            return "$0";
        }

        var magnitude = Math.Abs(value);
        var digits = magnitude >= 1m ? 2 : magnitude >= 0.01m ? 4 : 6;
        var rounded = Math.Round(value, digits, MidpointRounding.AwayFromZero);
        return "$" + rounded.ToString("0." + new string('#', digits), CultureInfo.CurrentCulture);
    }
}
