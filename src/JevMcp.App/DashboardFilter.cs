using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using JevMcp.Data;

namespace JevMcp.App;

public sealed class ChartHit
{
    public const string Action = "action";

    public const string Tool = "tool";

    public const string Status = "status";

    public const string Heatmap = "heatmap";

    public const string Time = "time";

    public const string Duration = "duration";

    public const string DataZoom = "datazoom";

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    public string Kind { get; init; } = "";

    public string? Name { get; init; }

    public string? ToolName { get; init; }

    public string? ActionName { get; init; }

    public DateTime? FromUtc { get; init; }

    public DateTime? ToUtc { get; init; }

    public int? DurationMinMs { get; init; }

    public int? DurationMaxMs { get; init; }

    public static ChartHit Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        return JsonSerializer.Deserialize<ChartHit>(json, Json)
            ?? throw new InvalidOperationException("Chart hit payload was empty.");
    }
}

public sealed record FilterChip(string Dimension, string Label, string Value);

/// <summary>
/// Shared dashboard window. An ECharts click only changes state through here.
/// </summary>
public sealed record DashboardFilter
{
    public int PresetHours { get; init; } = 24;

    public DateTime? FromUtc { get; init; }

    public DateTime? ToUtc { get; init; }

    public string? Tool { get; init; }

    public string? Provider { get; init; }

    public string? Action { get; init; }

    public string? Status { get; init; }

    public int? DurationMinMs { get; init; }

    public int? DurationMaxMs { get; init; }

    public DashboardFilter Apply(ChartHit hit)
    {
        ArgumentNullException.ThrowIfNull(hit);

        return hit.Kind switch
        {
            ChartHit.Action => this with { Action = Toggle(Action, hit.Name) },
            ChartHit.Tool => this with { Tool = Toggle(Tool, hit.Name) },
            ChartHit.Status => this with { Status = Toggle(Status, hit.Name) },
            ChartHit.Heatmap => ApplyHeatmap(hit.ToolName, hit.ActionName),
            ChartHit.Time => ApplyRange(hit.FromUtc, hit.ToUtc, toggle: true),
            ChartHit.DataZoom => ApplyRange(hit.FromUtc, hit.ToUtc, toggle: false),
            ChartHit.Duration => ApplyDuration(hit.DurationMinMs, hit.DurationMaxMs),
            _ => this,
        };
    }

    public DashboardFilter ClearDimension(string dimension)
    {
        return dimension switch
        {
            ChartHit.Action => this with { Action = null },
            ChartHit.Tool => this with { Tool = null },
            ChartHit.Status => this with { Status = null },
            ChartHit.Time => this with { FromUtc = null, ToUtc = null },
            ChartHit.Duration => this with { DurationMinMs = null, DurationMaxMs = null },
            "provider" => this with { Provider = null },
            _ => this,
        };
    }

    public DashboardFilter WithPreset(int hours) => new() { PresetHours = hours };

    public (DateTime FromUtc, DateTime ToUtc) Window(DateTime utcNow)
    {
        if (FromUtc is { } from && ToUtc is { } to)
        {
            return (from, to);
        }

        return (utcNow.AddHours(-PresetHours), utcNow);
    }

    public CallLogQuery ToQuery(DateTime utcNow)
    {
        var (from, to) = Window(utcNow);
        return new CallLogQuery
        {
            FromUtc = from,
            ToUtc = to,
            Tool = Tool,
            Provider = Provider,
            Action = Action,
            Status = Status,
            DurationMinMs = DurationMinMs,
            DurationMaxMs = DurationMaxMs,
        };
    }

    public IReadOnlyList<FilterChip> Chips(
        string emptyActionLabel = "(no action)",
        Func<DateTime, DateTime, string>? formatUtcRange = null)
    {
        formatUtcRange ??= static (from, to) =>
            $"{from.ToString("u", CultureInfo.InvariantCulture)} – {to.ToString("u", CultureInfo.InvariantCulture)}";
        var chips = new List<FilterChip>();
        if (Tool is not null)
        {
            chips.Add(new FilterChip(ChartHit.Tool, Tool, Tool));
        }

        if (Action is not null)
        {
            var label = Action.Length == 0 ? emptyActionLabel : Action;
            chips.Add(new FilterChip(ChartHit.Action, label, Action));
        }

        if (Status is not null)
        {
            chips.Add(new FilterChip(ChartHit.Status, Status, Status));
        }

        if (Provider is not null)
        {
            chips.Add(new FilterChip("provider", Provider, Provider));
        }

        if (FromUtc is not null && ToUtc is not null)
        {
            chips.Add(new FilterChip(
                ChartHit.Time,
                formatUtcRange(FromUtc.Value, ToUtc.Value),
                FromUtc.Value.ToString("o", CultureInfo.InvariantCulture)));
        }

        if (DurationMinMs is not null || DurationMaxMs is not null)
        {
            var max = DurationMaxMs is { } ceiling ? $"<{ceiling}" : "+";
            chips.Add(new FilterChip(
                ChartHit.Duration,
                $"{DurationMinMs ?? 0}–{max} ms",
                (DurationMinMs ?? 0).ToString(CultureInfo.InvariantCulture)));
        }

        return chips;
    }

    public string ToAuditPath(DateTime utcNow)
    {
        var (from, to) = Window(utcNow);
        var parts = new List<string>
        {
            "from=" + Uri.EscapeDataString(from.ToString("o", CultureInfo.InvariantCulture)),
            "to=" + Uri.EscapeDataString(to.ToString("o", CultureInfo.InvariantCulture)),
        };
        Append(parts, "tool", Tool);
        if (Action is not null)
        {
            parts.Add("action=" + Uri.EscapeDataString(Action));
        }

        Append(parts, "status", Status);
        Append(parts, "provider", Provider);
        if (DurationMinMs is { } min)
        {
            parts.Add("dmin=" + min.ToString(CultureInfo.InvariantCulture));
        }

        if (DurationMaxMs is { } max)
        {
            parts.Add("dmax=" + max.ToString(CultureInfo.InvariantCulture));
        }

        var query = new StringBuilder("/audit?");
        query.Append(string.Join("&", parts));
        return query.ToString();
    }

    private DashboardFilter ApplyHeatmap(string? tool, string? action)
    {
        if (string.Equals(Tool, tool, StringComparison.Ordinal) &&
            string.Equals(Action, action, StringComparison.Ordinal))
        {
            return this with { Tool = null, Action = null };
        }

        return this with { Tool = tool, Action = action };
    }

    private DashboardFilter ApplyRange(DateTime? from, DateTime? to, bool toggle)
    {
        if (from is null || to is null)
        {
            return this;
        }

        if (toggle && FromUtc == from && ToUtc == to)
        {
            return this with { FromUtc = null, ToUtc = null };
        }

        return this with { FromUtc = from, ToUtc = to };
    }

    private DashboardFilter ApplyDuration(int? min, int? max)
    {
        if (DurationMinMs == min && DurationMaxMs == max)
        {
            return this with { DurationMinMs = null, DurationMaxMs = null };
        }

        return this with { DurationMinMs = min, DurationMaxMs = max };
    }

    private static string? Toggle(string? current, string? incoming)
    {
        return string.Equals(current, incoming, StringComparison.Ordinal) ? null : incoming;
    }

    private static void Append(List<string> parts, string name, string? value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            parts.Add(name + "=" + Uri.EscapeDataString(value));
        }
    }
}
