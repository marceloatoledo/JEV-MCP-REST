namespace JevMcp.Data;

/// <summary>Granularity of dashboard series. Does not invent points outside the window.</summary>
public static class DashboardBuckets
{
    public static readonly (int MinMs, int? MaxMs, string Label)[] Latency =
    [
        (0, 100, "0-100"),
        (100, 250, "100-250"),
        (250, 500, "250-500"),
        (500, 1000, "500-1000"),
        (1000, null, "1000+"),
    ];

    public static readonly string[] HeatmapActions =
        ["auto", "review", "block", "escalate", "pass", "skip", ""];

    public static TimeSpan TimeSize(DateTime fromUtc, DateTime toUtc)
    {
        var window = toUtc - fromUtc;
        if (window <= TimeSpan.FromHours(24))
        {
            return TimeSpan.FromHours(1);
        }

        if (window <= TimeSpan.FromDays(7))
        {
            return TimeSpan.FromHours(6);
        }

        return TimeSpan.FromDays(1);
    }
}
