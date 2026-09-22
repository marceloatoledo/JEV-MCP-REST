using JevMcp.App;

namespace JevMcp.Tests.App;

public sealed class DashboardFilterTests
{
    [Fact]
    public void ActionClickIsolatesAndASecondClickClearsOnlyThatDimension()
    {
        var filter = new DashboardFilter { Tool = "jev_verify" }
            .Apply(new ChartHit { Kind = ChartHit.Action, Name = "escalate" });

        Assert.Equal("jev_verify", filter.Tool);
        Assert.Equal("escalate", filter.Action);
        Assert.Contains(filter.Chips(), chip => chip.Dimension == ChartHit.Action && chip.Label == "escalate");

        filter = filter.Apply(new ChartHit { Kind = ChartHit.Action, Name = "escalate" });
        Assert.Equal("jev_verify", filter.Tool);
        Assert.Null(filter.Action);
    }

    [Fact]
    public void ChipRemovesOnlyThatDimension()
    {
        var filter = new DashboardFilter { Tool = "jev_screen", Action = "escalate", Status = "error" }
            .ClearDimension(ChartHit.Action);

        Assert.Equal("jev_screen", filter.Tool);
        Assert.Equal("error", filter.Status);
        Assert.Null(filter.Action);
    }

    [Fact]
    public void HeatmapCombinesToolAndAction()
    {
        var filter = new DashboardFilter().Apply(new ChartHit
        {
            Kind = ChartHit.Heatmap,
            ToolName = "jev_screen",
            ActionName = "escalate",
        });

        Assert.Equal("jev_screen", filter.Tool);
        Assert.Equal("escalate", filter.Action);
    }

    [Fact]
    public void TimeBucketNarrowsAndTheSameBucketRestoresThePreset()
    {
        var from = new DateTime(2026, 9, 21, 3, 0, 0, DateTimeKind.Utc);
        var to = from.AddHours(1);
        var hit = new ChartHit { Kind = ChartHit.Time, FromUtc = from, ToUtc = to };

        var filter = new DashboardFilter { PresetHours = 24, Action = "review" }.Apply(hit);
        Assert.Equal(from, filter.FromUtc);
        Assert.Equal(to, filter.ToUtc);
        Assert.Equal("review", filter.Action);

        filter = filter.Apply(hit);
        Assert.Null(filter.FromUtc);
        Assert.Null(filter.ToUtc);
        Assert.Equal("review", filter.Action);
        Assert.Equal(24, filter.PresetHours);
    }

    [Fact]
    public void DataZoomAppliesTheRangeWithoutToggling()
    {
        var from = new DateTime(2026, 9, 21, 0, 0, 0, DateTimeKind.Utc);
        var to = from.AddHours(6);
        var hit = new ChartHit { Kind = ChartHit.DataZoom, FromUtc = from, ToUtc = to };

        var filter = new DashboardFilter().Apply(hit).Apply(hit);
        Assert.Equal(from, filter.FromUtc);
        Assert.Equal(to, filter.ToUtc);
    }

    [Fact]
    public void LatencyHistogramTogglesTheRange()
    {
        var hit = new ChartHit { Kind = ChartHit.Duration, DurationMinMs = 1000 };
        var filter = new DashboardFilter().Apply(hit);
        Assert.Equal(1000, filter.DurationMinMs);
        Assert.Null(filter.DurationMaxMs);

        filter = filter.Apply(hit);
        Assert.Null(filter.DurationMinMs);
    }

    [Fact]
    public void AuditBridgeCarriesTheCurrentWindow()
    {
        var now = new DateTime(2026, 9, 21, 12, 0, 0, DateTimeKind.Utc);
        var path = new DashboardFilter { Tool = "jev_screen", Action = "escalate" }.ToAuditPath(now);

        Assert.StartsWith("/audit?", path, StringComparison.Ordinal);
        Assert.Contains("tool=jev_screen", path, StringComparison.Ordinal);
        Assert.Contains("action=escalate", path, StringComparison.Ordinal);
        Assert.Contains("from=", path, StringComparison.Ordinal);
        Assert.Contains("to=", path, StringComparison.Ordinal);
    }

    [Fact]
    public void ChartHitParseIsCaseInsensitive()
    {
        var hit = ChartHit.Parse("""{"kind":"heatmap","toolName":"jev_screen","actionName":"escalate"}""");
        Assert.Equal(ChartHit.Heatmap, hit.Kind);
        Assert.Equal("jev_screen", hit.ToolName);
        Assert.Equal("escalate", hit.ActionName);
    }
}
