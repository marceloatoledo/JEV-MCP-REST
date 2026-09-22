using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using JevMcp.Data;

namespace JevMcp.App;

/// <summary>
/// Builds the ECharts <c>option</c> in C#. JS only resolves theme tokens, installs the
/// number formatters described by <c>meta</c> and applies the option.
/// </summary>
internal static class DashboardChartOptions
{
    /// <summary>Formats resolved in the browser, where the UI culture lives.</summary>
    private const string Count = "int";

    private const string Compact = "compact";

    private const string Milliseconds = "ms";

    private const string Usd = "usd";

    private const string TokensIn = "token:info";

    private const string TokensOut = "token:secondary";

    private const string Cost = "token:warning";

    private const string Calls = "token:primary";

    private const string LatencyLine = "token:tertiary";

    private const string Muted = "token:muted";

    /// <summary>Policy actions keep one colour across every chart of the panel.</summary>
    private static readonly Dictionary<string, string> ActionTokens = new(StringComparer.Ordinal)
    {
        ["auto"] = "token:success",
        ["pass"] = "token:info",
        ["review"] = "token:warning",
        ["escalate"] = "token:tertiary",
        ["block"] = "token:error",
        ["skip"] = Muted,
        [""] = Muted,
    };

    private static readonly Dictionary<string, string> StatusTokens = new(StringComparer.Ordinal)
    {
        ["ok"] = "token:success",
        ["error"] = "token:error",
    };

    /// <summary>Latency bands read from fast to slow, so the colour has to as well.</summary>
    private static readonly string[] LatencyTokens =
        ["token:success", "token:info", "token:warning", "token:warning", "token:error"];

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static string Consumption(
        IReadOnlyList<TimeBucket> buckets,
        DashboardChartText text,
        TimeZoneInfo displayZone)
    {
        var labels = new JsonArray();
        var titles = new JsonArray();
        var points = new JsonArray();
        var input = new JsonArray();
        var output = new JsonArray();
        var cost = new JsonArray();

        foreach (var bucket in buckets)
        {
            var hit = TimeHit(bucket);
            labels.Add(ShortLabel(bucket, displayZone));
            titles.Add(FullLabel(bucket, displayZone));
            points.Add(TimePoint(bucket));
            input.Add(Point(bucket.InputTokens, hit));
            output.Add(Point(bucket.OutputTokens, hit));
            cost.Add(Point(bucket.CostUsd, hit));
        }

        return Serialize(new JsonObject
        {
            ["animationDuration"] = 600,
            ["legend"] = Legend(),
            ["toolbox"] = Toolbox(text),
            ["tooltip"] = AxisTooltip("cross"),
            ["grid"] = Grid(top: 48, bottom: 56),
            ["dataZoom"] = TimeZoom(),
            ["xAxis"] = CategoryAxis(labels),
            ["yAxis"] = new JsonArray
            {
                ValueAxis(text.Tokens),
                ValueAxis(text.CostUsd, opposite: true, scale: true),
            },
            ["series"] = new JsonArray
            {
                StackedBar(text.InputTokens, "tokens", TokensIn, input, round: false),
                StackedBar(text.OutputTokens, "tokens", TokensOut, output, round: true),
                AreaLine(text.CostUsd, Cost, cost, axisIndex: 1),
            },
            ["meta"] = Meta(
                axisFormats: new JsonObject { ["y0"] = Compact, ["y1"] = Usd },
                seriesFormats: new JsonObject
                {
                    [text.InputTokens] = Compact,
                    [text.OutputTokens] = Compact,
                    [text.CostUsd] = Usd,
                },
                tooltipTitles: titles,
                timePoints: points),
        });
    }

    public static string Volume(
        IReadOnlyList<TimeBucket> buckets,
        DashboardChartText text,
        TimeZoneInfo displayZone)
    {
        var labels = new JsonArray();
        var titles = new JsonArray();
        var points = new JsonArray();
        var calls = new JsonArray();
        var latency = new JsonArray();

        foreach (var bucket in buckets)
        {
            var hit = TimeHit(bucket);
            labels.Add(ShortLabel(bucket, displayZone));
            titles.Add(FullLabel(bucket, displayZone));
            points.Add(TimePoint(bucket));
            calls.Add(Point(bucket.Count, hit));
            latency.Add(Point(Math.Round(bucket.AverageDurationMs, 1), hit));
        }

        return Serialize(new JsonObject
        {
            ["animationDuration"] = 600,
            ["legend"] = Legend(),
            ["toolbox"] = Toolbox(text),
            ["tooltip"] = AxisTooltip("cross"),
            ["grid"] = Grid(top: 48, bottom: 56),
            ["dataZoom"] = TimeZoom(),
            ["xAxis"] = CategoryAxis(labels),
            ["yAxis"] = new JsonArray
            {
                ValueAxis(text.Calls, minInterval: 1),
                ValueAxis(text.AverageLatency, opposite: true, scale: true),
            },
            ["series"] = new JsonArray
            {
                StackedBar(text.Calls, stack: null, Calls, calls, round: true),
                AreaLine(text.AverageLatency, LatencyLine, latency, axisIndex: 1),
            },
            ["meta"] = Meta(
                axisFormats: new JsonObject { ["y0"] = Compact, ["y1"] = Milliseconds },
                seriesFormats: new JsonObject
                {
                    [text.Calls] = Count,
                    [text.AverageLatency] = Milliseconds,
                },
                tooltipTitles: titles,
                timePoints: points),
        });
    }

    /// <summary>
    /// Nightingale pie aligned with the Apache ECharts <c>pie-roseType-simple</c> example
    /// (<c>roseType: area</c>, annulus <c>radius</c>, rounded slice corners).
    /// </summary>
    public static string Pie(
        IReadOnlyList<NamedCount> items,
        string kind,
        DashboardChartText text,
        string seriesName)
    {
        var data = new JsonArray();
        foreach (var item in items)
        {
            data.Add(new JsonObject
            {
                ["name"] = Display(item.Name, text.EmptyAction),
                ["value"] = item.Count,
                ["itemStyle"] = new JsonObject { ["color"] = SliceToken(kind, item.Name) },
                ["hit"] = HitNode(new ChartHit { Kind = kind, Name = item.Name }),
            });
        }

        return Serialize(new JsonObject
        {
            ["animationDuration"] = 600,
            ["tooltip"] = new JsonObject { ["trigger"] = "item" },
            ["legend"] = new JsonObject
            {
                ["top"] = "bottom",
                ["left"] = "center",
                ["icon"] = "circle",
                ["itemWidth"] = 10,
                ["itemHeight"] = 10,
            },
            ["series"] = new JsonArray
            {
                new JsonObject
                {
                    ["name"] = seriesName,
                    ["type"] = "pie",
                    ["radius"] = new JsonArray("20%", "72%"),
                    ["center"] = new JsonArray("50%", "46%"),
                    ["roseType"] = "area",
                    ["avoidLabelOverlap"] = true,
                    ["itemStyle"] = new JsonObject { ["borderRadius"] = 8 },
                    ["emphasis"] = new JsonObject
                    {
                        ["itemStyle"] = new JsonObject
                        {
                            ["shadowBlur"] = 10,
                            ["shadowOffsetX"] = 0,
                            ["shadowColor"] = "token:shadow-strong",
                        },
                    },
                    ["data"] = data,
                },
            },
            ["meta"] = Meta(tooltip: "share"),
        });
    }

    public static string Tools(IReadOnlyList<ToolUsage> items, DashboardChartText text)
    {
        var labels = new JsonArray();
        var input = new JsonArray();
        var output = new JsonArray();
        var extras = new JsonArray();

        // Horizontal bars grow upwards, so the heaviest consumer has to be serialized last.
        var ordered = items
            .OrderBy(item => item.InputTokens + item.OutputTokens)
            .ThenBy(item => item.Count)
            .ToArray();

        foreach (var item in ordered)
        {
            var hit = new ChartHit { Kind = ChartHit.Tool, Name = item.Name };
            labels.Add(item.Name);
            input.Add(Point(item.InputTokens, hit));
            output.Add(Point(item.OutputTokens, hit));
            extras.Add(new JsonArray
            {
                Extra(text.Calls, item.Count, Count),
                Extra(text.CostUsd, item.CostUsd, Usd),
                Extra(text.AverageLatency, Math.Round(item.AverageDurationMs, 1), Milliseconds),
            });
        }

        return Serialize(new JsonObject
        {
            ["animationDuration"] = 600,
            ["legend"] = Legend(),
            ["tooltip"] = AxisTooltip("shadow"),
            ["grid"] = Grid(top: 48, bottom: 8),
            ["xAxis"] = ValueAxis(text.Tokens),
            ["yAxis"] = new JsonObject
            {
                ["type"] = "category",
                ["data"] = labels,
                ["axisTick"] = new JsonObject { ["show"] = false },
            },
            ["series"] = new JsonArray
            {
                HorizontalBar(text.InputTokens, TokensIn, input, round: false),
                HorizontalBar(text.OutputTokens, TokensOut, output, round: true),
            },
            ["meta"] = Meta(
                axisFormats: new JsonObject { ["x0"] = Compact },
                seriesFormats: new JsonObject
                {
                    [text.InputTokens] = Compact,
                    [text.OutputTokens] = Compact,
                },
                axisExtras: extras),
        });
    }

    public static string Latency(IReadOnlyList<DurationBucket> buckets, DashboardChartText text)
    {
        var labels = new JsonArray();
        var data = new JsonArray();
        var total = buckets.Sum(bucket => bucket.Count);

        for (var index = 0; index < buckets.Count; index++)
        {
            var bucket = buckets[index];
            labels.Add(bucket.Label);

            var point = Point(bucket.Count, new ChartHit
            {
                Kind = ChartHit.Duration,
                Name = bucket.Label,
                DurationMinMs = bucket.MinMs,
                DurationMaxMs = bucket.MaxMs,
            });
            point["itemStyle"] = new JsonObject
            {
                ["color"] = index < LatencyTokens.Length ? LatencyTokens[index] : Calls,
                ["borderRadius"] = new JsonArray(4, 4, 0, 0),
            };

            // A literal share beats {c}: the histogram answers "how spread", not "how many".
            point["label"] = new JsonObject
            {
                ["show"] = bucket.Count > 0,
                ["position"] = "top",
                ["formatter"] = Share(bucket.Count, total),
            };

            data.Add(point);
        }

        return Serialize(new JsonObject
        {
            ["animationDuration"] = 600,
            ["tooltip"] = AxisTooltip("shadow"),
            ["grid"] = Grid(top: 24, bottom: 8),
            ["xAxis"] = CategoryAxis(labels),
            ["yAxis"] = ValueAxis(text.Calls, minInterval: 1),
            ["series"] = new JsonArray
            {
                new JsonObject
                {
                    ["name"] = text.Calls,
                    ["type"] = "bar",
                    ["barMaxWidth"] = 48,
                    ["data"] = data,
                },
            },
            ["meta"] = Meta(
                axisFormats: new JsonObject { ["y0"] = Count },
                seriesFormats: new JsonObject { [text.Calls] = Count }),
        });
    }

    public static string Heatmap(IReadOnlyList<HeatCell> cells, DashboardChartText text)
    {
        var tools = cells.Select(cell => cell.Tool).Distinct(StringComparer.Ordinal).ToArray();
        var actions = DashboardBuckets.HeatmapActions;
        var actionLabels = new JsonArray();
        foreach (var action in actions)
        {
            actionLabels.Add(Display(action, text.EmptyAction));
        }

        var toolLabels = new JsonArray();
        foreach (var tool in tools)
        {
            toolLabels.Add(tool);
        }

        var data = new JsonArray();
        var max = 1;
        for (var y = 0; y < tools.Length; y++)
        {
            for (var x = 0; x < actions.Length; x++)
            {
                var count = cells
                    .FirstOrDefault(cell => cell.Tool == tools[y] && cell.Action == actions[x])
                    ?.Count ?? 0;
                max = Math.Max(max, count);
                data.Add(new JsonObject
                {
                    ["value"] = new JsonArray(x, y, count),
                    ["title"] = tools[y] + " · " + Display(actions[x], text.EmptyAction),
                    ["label"] = new JsonObject { ["show"] = count > 0 },
                    ["hit"] = HitNode(new ChartHit
                    {
                        Kind = ChartHit.Heatmap,
                        ToolName = tools[y],
                        ActionName = actions[x],
                    }),
                });
            }
        }

        return Serialize(new JsonObject
        {
            ["animationDuration"] = 600,
            ["tooltip"] = new JsonObject { ["position"] = "top" },
            ["grid"] = Grid(top: 16, bottom: 8, right: 72),
            ["xAxis"] = new JsonObject
            {
                ["type"] = "category",
                ["data"] = actionLabels,
                ["splitArea"] = new JsonObject { ["show"] = true },
                ["axisTick"] = new JsonObject { ["show"] = false },
            },
            ["yAxis"] = new JsonObject
            {
                ["type"] = "category",
                ["data"] = toolLabels,
                ["splitArea"] = new JsonObject { ["show"] = true },
                ["axisTick"] = new JsonObject { ["show"] = false },
            },
            ["visualMap"] = new JsonObject
            {
                ["min"] = 0,
                ["max"] = max,
                ["calculable"] = true,
                ["orient"] = "vertical",
                ["right"] = 8,
                ["top"] = "middle",
                ["itemHeight"] = 96,
                ["inRange"] = new JsonObject
                {
                    ["color"] = new JsonArray("token:heat-low", "token:heat-mid", "token:heat-high"),
                },
            },
            ["series"] = new JsonArray
            {
                new JsonObject
                {
                    ["type"] = "heatmap",
                    ["data"] = data,
                    ["label"] = new JsonObject { ["show"] = true },
                    ["itemStyle"] = new JsonObject
                    {
                        ["borderRadius"] = 3,
                        ["borderWidth"] = 2,
                        ["borderColor"] = "token:surface",
                    },
                    ["emphasis"] = new JsonObject
                    {
                        ["itemStyle"] = new JsonObject { ["shadowBlur"] = 8 },
                    },
                },
            },
            ["meta"] = Meta(tooltip: "matrix"),
        });
    }

    /// <summary>Chart height that keeps the heatmap rows readable as tools accumulate.</summary>
    public static int HeatmapHeight(IReadOnlyList<HeatCell> cells)
    {
        var tools = cells.Select(cell => cell.Tool).Distinct(StringComparer.Ordinal).Count();
        return Math.Clamp(96 + (tools * 34), 240, 720);
    }

    private static JsonObject Legend() => new()
    {
        ["top"] = 4,
        ["icon"] = "circle",
        ["itemWidth"] = 10,
        ["itemHeight"] = 10,
    };

    private static JsonObject Toolbox(DashboardChartText text) => new()
    {
        ["right"] = 8,
        ["top"] = 0,
        ["feature"] = new JsonObject
        {
            ["saveAsImage"] = new JsonObject
            {
                ["title"] = text.SaveImage,
                ["pixelRatio"] = 2,
            },
        },
    };

    private static JsonObject AxisTooltip(string pointer) => new()
    {
        ["trigger"] = "axis",
        ["axisPointer"] = new JsonObject { ["type"] = pointer },
    };

    private static JsonObject Grid(int top, int bottom, int right = 16) => new()
    {
        ["left"] = 8,
        ["right"] = right,
        ["top"] = top,
        ["bottom"] = bottom,
        ["containLabel"] = true,
    };

    private static JsonArray TimeZoom() =>
    [
        new JsonObject { ["type"] = "inside", ["throttle"] = 80 },
        new JsonObject { ["type"] = "slider", ["height"] = 16, ["bottom"] = 8 },
    ];

    private static JsonObject CategoryAxis(JsonArray labels) => new()
    {
        ["type"] = "category",
        ["data"] = labels,
        ["boundaryGap"] = true,
        ["axisTick"] = new JsonObject { ["alignWithLabel"] = true },
        ["axisLabel"] = new JsonObject { ["hideOverlap"] = true },
    };

    private static JsonObject ValueAxis(
        string name,
        bool opposite = false,
        bool scale = false,
        int? minInterval = null)
    {
        var axis = new JsonObject
        {
            ["type"] = "value",
            ["name"] = name,
            ["nameGap"] = 12,
            ["nameTextStyle"] = new JsonObject { ["align"] = opposite ? "right" : "left" },
        };

        if (opposite)
        {
            axis["position"] = "right";
            axis["splitLine"] = new JsonObject { ["show"] = false };
        }

        if (scale)
        {
            axis["scale"] = true;
        }

        if (minInterval is { } interval)
        {
            axis["minInterval"] = interval;
        }

        return axis;
    }

    private static JsonObject StackedBar(
        string name,
        string? stack,
        string colorToken,
        JsonArray data,
        bool round)
    {
        var series = new JsonObject
        {
            ["name"] = name,
            ["type"] = "bar",
            ["color"] = colorToken,
            ["barMaxWidth"] = 28,
            ["itemStyle"] = new JsonObject
            {
                ["borderRadius"] = round ? new JsonArray(4, 4, 0, 0) : new JsonArray(0, 0, 0, 0),
            },
            ["data"] = data,
        };

        if (stack is not null)
        {
            series["stack"] = stack;
        }

        return series;
    }

    private static JsonObject HorizontalBar(string name, string colorToken, JsonArray data, bool round) => new()
    {
        ["name"] = name,
        ["type"] = "bar",
        ["stack"] = "tokens",
        ["color"] = colorToken,
        ["barMaxWidth"] = 24,
        ["itemStyle"] = new JsonObject
        {
            ["borderRadius"] = round ? new JsonArray(0, 4, 4, 0) : new JsonArray(0, 0, 0, 0),
        },
        ["data"] = data,
    };

    /// <summary>The empty <c>areaStyle</c> is the marker the JS layer turns into a gradient.</summary>
    private static JsonObject AreaLine(string name, string colorToken, JsonArray data, int axisIndex) => new()
    {
        ["name"] = name,
        ["type"] = "line",
        ["yAxisIndex"] = axisIndex,
        ["color"] = colorToken,
        ["smooth"] = true,
        ["symbol"] = "circle",
        ["symbolSize"] = 6,
        ["showSymbol"] = false,
        ["lineStyle"] = new JsonObject { ["width"] = 2 },
        ["areaStyle"] = new JsonObject(),
        ["emphasis"] = new JsonObject { ["focus"] = "series" },
        ["data"] = data,
    };

    private static JsonObject Meta(
        JsonObject? axisFormats = null,
        JsonObject? seriesFormats = null,
        JsonArray? tooltipTitles = null,
        JsonArray? timePoints = null,
        JsonArray? axisExtras = null,
        string? tooltip = null)
    {
        var meta = new JsonObject();
        if (axisFormats is not null)
        {
            meta["axisFormats"] = axisFormats;
        }

        if (seriesFormats is not null)
        {
            meta["seriesFormats"] = seriesFormats;
        }

        if (tooltipTitles is not null)
        {
            meta["tooltipTitles"] = tooltipTitles;
        }

        if (timePoints is not null)
        {
            meta["timePoints"] = timePoints;
        }

        if (axisExtras is not null)
        {
            meta["axisExtras"] = axisExtras;
        }

        if (tooltip is not null)
        {
            meta["tooltip"] = tooltip;
        }

        return meta;
    }

    private static JsonObject Extra<T>(string label, T value, string format)
        where T : struct => new()
        {
            ["label"] = label,
            ["value"] = JsonValue.Create(value),
            ["format"] = format,
        };

    private static JsonObject Point<T>(T value, ChartHit hit)
        where T : struct => new()
        {
            ["value"] = JsonValue.Create(value),
            ["hit"] = HitNode(hit),
        };

    private static ChartHit TimeHit(TimeBucket bucket) => new()
    {
        Kind = ChartHit.Time,
        FromUtc = bucket.FromUtc,
        ToUtc = bucket.ToUtc,
    };

    private static JsonObject TimePoint(TimeBucket bucket) => new()
    {
        ["fromUtc"] = bucket.FromUtc.ToString("o", CultureInfo.InvariantCulture),
        ["toUtc"] = bucket.ToUtc.ToString("o", CultureInfo.InvariantCulture),
    };

    /// <summary>Axis label for the bucket: as short as its granularity allows.</summary>
    private static string ShortLabel(TimeBucket bucket, TimeZoneInfo displayZone)
    {
        var culture = CultureInfo.CurrentCulture;
        var from = DisplayTimeZone.ToDisplayTime(bucket.FromUtc, displayZone);
        var size = bucket.ToUtc - bucket.FromUtc;
        if (size <= TimeSpan.FromHours(1))
        {
            return from.ToString("HH:mm", culture);
        }

        if (size <= TimeSpan.FromHours(6))
        {
            return from.ToString(DayMonth(culture) + " HH'h'", culture);
        }

        return from.ToString(DayMonth(culture), culture);
    }

    private static string FullLabel(TimeBucket bucket, TimeZoneInfo displayZone)
    {
        var culture = CultureInfo.CurrentCulture;
        var day = DayMonth(culture);
        var from = DisplayTimeZone.ToDisplayTime(bucket.FromUtc, displayZone);
        var to = DisplayTimeZone.ToDisplayTime(bucket.ToUtc, displayZone);
        var fromText = from.ToString(day + " HH:mm", culture);
        var toText = to.Date == from.Date
            ? to.ToString("HH:mm", culture)
            : to.ToString(day + " HH:mm", culture);
        var zoneLabel = DisplayTimeZone.OffsetLabel(displayZone, bucket.FromUtc);
        return $"{fromText} – {toText} {zoneLabel}";
    }

    /// <summary>Day and month in the order the culture writes a short date.</summary>
    private static string DayMonth(CultureInfo culture) =>
        culture.DateTimeFormat.ShortDatePattern.StartsWith('M') ? "MM/dd" : "dd/MM";

    private static string Share(int count, int total) =>
        total == 0 ? "" : ((double)count / total).ToString("P0", CultureInfo.CurrentCulture);

    private static string SliceToken(string kind, string name)
    {
        var tokens = kind == ChartHit.Status ? StatusTokens : ActionTokens;
        return tokens.TryGetValue(name, out var token) ? token : "token:info";
    }

    private static JsonNode HitNode(ChartHit hit) => JsonSerializer.SerializeToNode(hit, Json)!;

    private static string Display(string name, string emptyAction) => name.Length == 0 ? emptyAction : name;

    private static string Serialize(JsonObject option) => option.ToJsonString(Json);
}

/// <summary>Localized labels the chart option needs; the JS layer never translates.</summary>
internal sealed class DashboardChartText
{
    public string Calls { get; init; } = "";

    public string Tokens { get; init; } = "";

    public string InputTokens { get; init; } = "";

    public string OutputTokens { get; init; } = "";

    public string CostUsd { get; init; } = "";

    public string AverageLatency { get; init; } = "";

    public string EmptyAction { get; init; } = "";

    public string SaveImage { get; init; } = "";
}
