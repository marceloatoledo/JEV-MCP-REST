using System.Text.Json;
using JevMcp.App;
using JevMcp.Data;

namespace JevMcp.Tests.App;

public sealed class DashboardChartOptionsTests
{
    private static readonly DashboardChartText Text = new()
    {
        Calls = "Chamadas",
        Tokens = "Tokens",
        InputTokens = "Entrada",
        OutputTokens = "Saída",
        CostUsd = "Custo (USD)",
        AverageLatency = "Latência média",
        EmptyAction = "(sem ação)",
        SaveImage = "Salvar imagem",
    };

    [Fact]
    public void ConsumptionKeepsCostOffTheTokenAxis()
    {
        var from = new DateTime(2026, 9, 21, 14, 0, 0, DateTimeKind.Utc);
        var bucket = new TimeBucket(from, from.AddHours(1), 3, 1_400_000, 900, 0.000126m, 42);

        using var option = JsonDocument.Parse(DashboardChartOptions.Consumption([bucket], Text, TimeZoneInfo.Utc));
        var root = option.RootElement;
        var series = root.GetProperty("series");

        Assert.Equal(3, series.GetArrayLength());
        Assert.Equal("tokens", series[0].GetProperty("stack").GetString());
        Assert.Equal("tokens", series[1].GetProperty("stack").GetString());
        Assert.Equal(1_400_000, series[0].GetProperty("data")[0].GetProperty("value").GetInt64());
        Assert.Equal("line", series[2].GetProperty("type").GetString());
        Assert.Equal(1, series[2].GetProperty("yAxisIndex").GetInt32());
        Assert.Equal(2, root.GetProperty("yAxis").GetArrayLength());

        var meta = root.GetProperty("meta");
        Assert.Equal("compact", meta.GetProperty("axisFormats").GetProperty("y0").GetString());
        Assert.Equal("usd", meta.GetProperty("axisFormats").GetProperty("y1").GetString());
        Assert.Equal("usd", meta.GetProperty("seriesFormats").GetProperty("Custo (USD)").GetString());
        Assert.Contains("14:00", meta.GetProperty("tooltipTitles")[0].GetString(), StringComparison.Ordinal);
        Assert.Equal("14:00", root.GetProperty("xAxis").GetProperty("data")[0].GetString());
    }

    [Fact]
    public void TimeBucketClickStillCarriesTheWindow()
    {
        var from = new DateTime(2026, 9, 21, 14, 0, 0, DateTimeKind.Utc);
        var bucket = new TimeBucket(from, from.AddHours(1), 3, 10, 5, 0.0000004m, 42);

        using var option = JsonDocument.Parse(DashboardChartOptions.Volume([bucket], Text, TimeZoneInfo.Utc));
        var root = option.RootElement;
        var hit = root.GetProperty("series")[0].GetProperty("data")[0].GetProperty("hit");

        Assert.Equal(ChartHit.Time, hit.GetProperty("kind").GetString());
        Assert.Equal(from, hit.GetProperty("fromUtc").GetDateTime());
        Assert.Equal("ms", root.GetProperty("meta").GetProperty("axisFormats").GetProperty("y1").GetString());
        Assert.Equal(2, root.GetProperty("dataZoom").GetArrayLength());
        Assert.Equal(from.AddHours(1), root.GetProperty("meta").GetProperty("timePoints")[0]
            .GetProperty("toUtc").GetDateTime());
    }

    [Fact]
    public void PolicyActionsKeepOneColourToken()
    {
        NamedCount[] items = [new("block", 2), new("auto", 1), new("", 1)];

        using var option = JsonDocument.Parse(DashboardChartOptions.Pie(items, ChartHit.Action, Text, "Ações"));
        var root = option.RootElement;
        var series = root.GetProperty("series")[0];
        var data = series.GetProperty("data");

        Assert.Equal("token:error", data[0].GetProperty("itemStyle").GetProperty("color").GetString());
        Assert.Equal("token:success", data[1].GetProperty("itemStyle").GetProperty("color").GetString());
        Assert.Equal("(sem ação)", data[2].GetProperty("name").GetString());
        Assert.Equal("pie", series.GetProperty("type").GetString());
        Assert.Equal("area", series.GetProperty("roseType").GetString());
        Assert.Equal(2, series.GetProperty("radius").GetArrayLength());
        Assert.Equal("20%", series.GetProperty("radius")[0].GetString());
        Assert.Equal("bottom", root.GetProperty("legend").GetProperty("top").GetString());
        Assert.Equal(8, series.GetProperty("itemStyle").GetProperty("borderRadius").GetInt32());
        Assert.False(root.TryGetProperty("title", out _));
        Assert.Equal("share", root.GetProperty("meta").GetProperty("tooltip").GetString());
    }

    [Fact]
    public void StatusReusesTheSameSemanticTokens()
    {
        NamedCount[] items = [new("ok", 5), new("error", 1)];

        using var option = JsonDocument.Parse(DashboardChartOptions.Pie(items, ChartHit.Status, Text, "Status"));
        var data = option.RootElement.GetProperty("series")[0].GetProperty("data");

        Assert.Equal("token:success", data[0].GetProperty("itemStyle").GetProperty("color").GetString());
        Assert.Equal("token:error", data[1].GetProperty("itemStyle").GetProperty("color").GetString());
    }

    [Fact]
    public void ToolsRankByConsumptionAndCarryTheUsageRows()
    {
        ToolUsage[] items =
        [
            new("jev_screen", 10, 100, 20, 0.0000051m, 40),
            new("jev_verify", 2, 4_000, 500, 0.00019m, 120),
        ];

        using var option = JsonDocument.Parse(DashboardChartOptions.Tools(items, Text));
        var root = option.RootElement;
        var categories = root.GetProperty("yAxis").GetProperty("data");

        // Horizontal categories grow upwards: the heaviest consumer is serialized last.
        Assert.Equal("jev_screen", categories[0].GetString());
        Assert.Equal("jev_verify", categories[1].GetString());
        Assert.Equal(4_000, root.GetProperty("series")[0].GetProperty("data")[1].GetProperty("value").GetInt64());

        var extras = root.GetProperty("meta").GetProperty("axisExtras")[1];
        Assert.Equal("Chamadas", extras[0].GetProperty("label").GetString());
        Assert.Equal(2, extras[0].GetProperty("value").GetInt32());
        Assert.Equal("usd", extras[1].GetProperty("format").GetString());
        Assert.Equal("ms", extras[2].GetProperty("format").GetString());
        Assert.Equal(ChartHit.Tool, root.GetProperty("series")[0].GetProperty("data")[1]
            .GetProperty("hit").GetProperty("kind").GetString());
    }

    [Fact]
    public void LatencyBandsRunFromFastToSlowAndLabelTheShare()
    {
        DurationBucket[] buckets =
        [
            new(0, 100, "0-100", 3),
            new(100, 250, "100-250", 1),
            new(250, 500, "250-500", 0),
            new(500, 1000, "500-1000", 0),
            new(1000, null, "1000+", 0),
        ];

        using var option = JsonDocument.Parse(DashboardChartOptions.Latency(buckets, Text));
        var data = option.RootElement.GetProperty("series")[0].GetProperty("data");

        Assert.Equal("token:success", data[0].GetProperty("itemStyle").GetProperty("color").GetString());
        Assert.Equal("token:error", data[4].GetProperty("itemStyle").GetProperty("color").GetString());
        Assert.True(data[0].GetProperty("label").GetProperty("show").GetBoolean());
        Assert.False(data[2].GetProperty("label").GetProperty("show").GetBoolean());
        Assert.Equal(1000, data[4].GetProperty("hit").GetProperty("durationMinMs").GetInt32());
    }

    [Fact]
    public void HeatmapLabelsOnlyTheCellsWithCallsAndGrowsWithTools()
    {
        HeatCell[] cells =
        [
            new("jev_screen", "auto", 4),
            new("jev_screen", "block", 0),
            new("jev_verify", "auto", 1),
        ];

        using var option = JsonDocument.Parse(DashboardChartOptions.Heatmap(cells, Text));
        var root = option.RootElement;
        var data = root.GetProperty("series")[0].GetProperty("data");
        var filled = data.EnumerateArray().First(cell => cell.GetProperty("value")[2].GetInt32() == 4);
        var empty = data.EnumerateArray().First(cell => cell.GetProperty("value")[2].GetInt32() == 0);

        Assert.True(filled.GetProperty("label").GetProperty("show").GetBoolean());
        Assert.False(empty.GetProperty("label").GetProperty("show").GetBoolean());
        Assert.Contains("jev_screen", filled.GetProperty("title").GetString(), StringComparison.Ordinal);
        Assert.Equal(
            "token:heat-high",
            root.GetProperty("visualMap").GetProperty("inRange").GetProperty("color")[2].GetString());
        Assert.True(DashboardChartOptions.HeatmapHeight(cells) < DashboardChartOptions.HeatmapHeight(
        [
            .. Enumerable.Range(0, 10).Select(index => new HeatCell($"jev_{index}", "auto", 1)),
        ]));
    }

    [Fact]
    public void CostBelowOneCentDoesNotReadAsZero()
    {
        using var culture = new CultureScope("en");

        Assert.Equal("$0", UsageFormat.Usd(0));
        Assert.Equal("$0.000126", UsageFormat.Usd(0.000126m));
        Assert.Equal("$12.34", UsageFormat.Usd(12.34m));
        Assert.Equal("1,400,000", UsageFormat.Whole(1_400_000));
        Assert.Equal("42 ms", UsageFormat.Milliseconds(42.4));
        Assert.Equal("—", UsageFormat.Milliseconds(null));
    }
}
