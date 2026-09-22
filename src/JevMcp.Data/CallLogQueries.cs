using Microsoft.EntityFrameworkCore;

namespace JevMcp.Data;

/// <summary>History reads for the admin. Aggregates and pages in SQLite.</summary>
public interface ICallLogQueries
{
    Task<DashboardSnapshot> GetDashboardAsync(CallLogQuery query, CancellationToken cancellationToken = default);

    Task<DashboardSeries> GetDashboardSeriesAsync(CallLogQuery query, CancellationToken cancellationToken = default);

    Task<CallLogPage> SearchAsync(CallLogQuery query, CancellationToken cancellationToken = default);

    Task<CallLog?> GetAsync(long id, CancellationToken cancellationToken = default);
}

public sealed class DashboardSnapshot
{
    public int CallCount { get; init; }

    public double? AverageLatencyMs { get; init; }

    public long InputTokens { get; init; }

    public long OutputTokens { get; init; }

    public decimal TotalCostUsd { get; init; }

    public IReadOnlyList<NamedCount> Actions { get; init; } = [];
}

public sealed class DashboardSeries
{
    public IReadOnlyList<TimeBucket> Volume { get; init; } = [];

    public IReadOnlyList<ToolUsage> Tools { get; init; } = [];

    public IReadOnlyList<NamedCount> Actions { get; init; } = [];

    public IReadOnlyList<NamedCount> Statuses { get; init; } = [];

    public IReadOnlyList<HeatCell> Heatmap { get; init; } = [];

    public IReadOnlyList<DurationBucket> Latency { get; init; } = [];
}

public sealed record NamedCount(string Name, int Count);

public sealed record TimeBucket(
    DateTime FromUtc,
    DateTime ToUtc,
    int Count,
    long InputTokens = 0,
    long OutputTokens = 0,
    decimal CostUsd = 0,
    double AverageDurationMs = 0);

public sealed record ToolUsage(
    string Name,
    int Count,
    long InputTokens,
    long OutputTokens,
    decimal CostUsd,
    double AverageDurationMs);

public sealed record HeatCell(string Tool, string Action, int Count);

public sealed record DurationBucket(int MinMs, int? MaxMs, string Label, int Count);

public sealed class CallLogQuery
{
    public DateTime? FromUtc { get; init; }

    public DateTime? ToUtc { get; init; }

    public string? Tool { get; init; }

    public string? Provider { get; init; }

    /// <summary>
    /// <see langword="null"/> does not filter. An empty string selects a missing action.
    /// </summary>
    public string? Action { get; init; }

    public string? Status { get; init; }

    public int? DurationMinMs { get; init; }

    public int? DurationMaxMs { get; init; }

    public string? SortBy { get; init; }

    public bool SortDescending { get; init; } = true;

    public int Skip { get; init; }

    public int Take { get; init; } = 25;
}

public sealed record CallLogPage(int Total, IReadOnlyList<CallLogSummary> Items);

public sealed class CallLogSummary
{
    public long Id { get; init; }

    public DateTime Instant { get; init; }

    public string Tool { get; init; } = "";

    public string? Provider { get; init; }

    public string? Model { get; init; }

    public int DurationMs { get; init; }

    public int InputTokens { get; init; }

    public int OutputTokens { get; init; }

    public decimal CostUsd { get; init; }

    public string? ResultingAction { get; init; }

    public string Status { get; init; } = "";

    public string? TokenName { get; init; }

    public string? TokenPrefix { get; init; }

    public long? AccessTokenId { get; init; }

    public bool HasPayload { get; init; }
}

internal sealed class CallLogQueries : ICallLogQueries
{
    private readonly IDbContextFactory<AppDbContext> _factory;

    public CallLogQueries(IDbContextFactory<AppDbContext> factory)
    {
        _factory = factory;
    }

    public async Task<DashboardSnapshot> GetDashboardAsync(
        CallLogQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        await using var db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var period = Apply(db.CallLogs.AsNoTracking(), query);

        var count = await period.CountAsync(cancellationToken).ConfigureAwait(false);
        if (count == 0)
        {
            return new DashboardSnapshot();
        }

        var average = await period.AverageAsync(log => (double)log.DurationMs, cancellationToken)
            .ConfigureAwait(false);
        var input = await period.SumAsync(log => (long)log.InputTokens, cancellationToken).ConfigureAwait(false);
        var output = await period.SumAsync(log => (long)log.OutputTokens, cancellationToken).ConfigureAwait(false);
        var cost = await period.SumAsync(log => log.CostUsd, cancellationToken).ConfigureAwait(false);
        var actions = await period
            .GroupBy(log => log.ResultingAction)
            .Select(group => new { Action = group.Key, Count = group.Count() })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return new DashboardSnapshot
        {
            CallCount = count,
            AverageLatencyMs = average,
            InputTokens = input,
            OutputTokens = output,
            TotalCostUsd = cost,
            Actions =
            [
                .. actions
                    .Select(item => new NamedCount(item.Action ?? "", item.Count))
                    .OrderByDescending(item => item.Count),
            ],
        };
    }

    public async Task<DashboardSeries> GetDashboardSeriesAsync(
        CallLogQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        await using var db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var period = Apply(db.CallLogs.AsNoTracking(), query);

        if (!await period.AnyAsync(cancellationToken).ConfigureAwait(false))
        {
            return new DashboardSeries();
        }

        var tools = await period
            .GroupBy(log => log.Tool)
            .Select(group => new ToolUsage(
                group.Key,
                group.Count(),
                group.Sum(log => (long)log.InputTokens),
                group.Sum(log => (long)log.OutputTokens),
                group.Sum(log => log.CostUsd),
                group.Average(log => (double)log.DurationMs)))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var actions = await period
            .GroupBy(log => log.ResultingAction)
            .Select(group => new { Action = group.Key, Count = group.Count() })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var statuses = await period
            .GroupBy(log => log.Status)
            .Select(group => new NamedCount(group.Key, group.Count()))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var heat = await period
            .GroupBy(log => new { log.Tool, log.ResultingAction })
            .Select(group => new { group.Key.Tool, Action = group.Key.ResultingAction, Count = group.Count() })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var latency = await period
            .GroupBy(_ => 1)
            .Select(group => new
            {
                B0 = group.Count(log => log.DurationMs < 100),
                B1 = group.Count(log => log.DurationMs >= 100 && log.DurationMs < 250),
                B2 = group.Count(log => log.DurationMs >= 250 && log.DurationMs < 500),
                B3 = group.Count(log => log.DurationMs >= 500 && log.DurationMs < 1000),
                B4 = group.Count(log => log.DurationMs >= 1000),
            })
            .FirstAsync(cancellationToken)
            .ConfigureAwait(false);

        var volume = await VolumeAsync(period, query, cancellationToken).ConfigureAwait(false);

        var toolRows = tools
            .Select(item => item.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var heatCells = new List<HeatCell>();
        foreach (var tool in toolRows)
        {
            foreach (var action in DashboardBuckets.HeatmapActions)
            {
                var count = heat
                    .Where(item => item.Tool == tool && (item.Action ?? "") == action)
                    .Select(item => item.Count)
                    .FirstOrDefault();
                heatCells.Add(new HeatCell(tool, action, count));
            }
        }

        var latencyCounts = new[] { latency.B0, latency.B1, latency.B2, latency.B3, latency.B4 };

        return new DashboardSeries
        {
            Volume = volume,
            Tools = [.. tools.OrderByDescending(item => item.Count)],
            Actions =
            [
                .. actions
                    .Select(item => new NamedCount(item.Action ?? "", item.Count))
                    .OrderByDescending(item => item.Count),
            ],
            Statuses = [.. statuses.OrderByDescending(item => item.Count)],
            Heatmap = heatCells,
            Latency =
            [
                .. DashboardBuckets.Latency.Select((band, index) =>
                    new DurationBucket(band.MinMs, band.MaxMs, band.Label, latencyCounts[index])),
            ],
        };
    }

    public async Task<CallLogPage> SearchAsync(CallLogQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var take = Math.Clamp(query.Take, 1, 100);
        var skip = Math.Max(query.Skip, 0);

        await using var db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var rows = Apply(db.CallLogs.AsNoTracking(), query);
        var total = await rows.CountAsync(cancellationToken).ConfigureAwait(false);
        rows = Sort(rows, query.SortBy, query.SortDescending);

        var items = await rows
            .Skip(skip)
            .Take(take)
            .Select(log => new CallLogSummary
            {
                Id = log.Id,
                Instant = log.Instant,
                Tool = log.Tool,
                Provider = log.Provider,
                Model = log.Model,
                DurationMs = log.DurationMs,
                InputTokens = log.InputTokens,
                OutputTokens = log.OutputTokens,
                CostUsd = log.CostUsd,
                ResultingAction = log.ResultingAction,
                Status = log.Status,
                TokenName = log.TokenName,
                TokenPrefix = log.TokenPrefix,
                AccessTokenId = log.AccessTokenId,
                HasPayload = log.RequestPayload != null || log.ResponsePayload != null,
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return new CallLogPage(total, items);
    }

    public async Task<CallLog?> GetAsync(long id, CancellationToken cancellationToken = default)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await db.CallLogs.AsNoTracking()
            .FirstOrDefaultAsync(log => log.Id == id, cancellationToken)
            .ConfigureAwait(false);
    }

    internal static IQueryable<CallLog> Apply(IQueryable<CallLog> rows, CallLogQuery query)
    {
        if (query.FromUtc is { } from)
        {
            rows = rows.Where(log => log.Instant >= from);
        }

        if (query.ToUtc is { } to)
        {
            rows = rows.Where(log => log.Instant < to);
        }

        if (!string.IsNullOrWhiteSpace(query.Tool))
        {
            rows = rows.Where(log => log.Tool == query.Tool);
        }

        if (!string.IsNullOrWhiteSpace(query.Provider))
        {
            rows = rows.Where(log => log.Provider == query.Provider);
        }

        if (query.Action is not null)
        {
            rows = query.Action.Length == 0
                ? rows.Where(log => log.ResultingAction == null || log.ResultingAction == "")
                : rows.Where(log => log.ResultingAction == query.Action);
        }

        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            rows = rows.Where(log => log.Status == query.Status);
        }

        if (query.DurationMinMs is { } min)
        {
            rows = rows.Where(log => log.DurationMs >= min);
        }

        if (query.DurationMaxMs is { } max)
        {
            rows = rows.Where(log => log.DurationMs < max);
        }

        return rows;
    }

    private static async Task<IReadOnlyList<TimeBucket>> VolumeAsync(
        IQueryable<CallLog> period,
        CallLogQuery query,
        CancellationToken cancellationToken)
    {
        var from = query.FromUtc ?? DateTime.SpecifyKind(DateTime.MinValue, DateTimeKind.Utc);
        var to = query.ToUtc ?? DateTime.UtcNow;
        var size = DashboardBuckets.TimeSize(from, to);

        if (size == TimeSpan.FromHours(1))
        {
            var groups = await period
                .GroupBy(log => new { log.Instant.Year, log.Instant.Month, log.Instant.Day, log.Instant.Hour })
                .Select(group => new VolumeRow(
                    group.Key.Year,
                    group.Key.Month,
                    group.Key.Day,
                    group.Key.Hour,
                    group.Count(),
                    group.Sum(log => (long)log.InputTokens),
                    group.Sum(log => (long)log.OutputTokens),
                    group.Sum(log => log.CostUsd),
                    group.Average(log => (double)log.DurationMs)))
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            return Buckets(groups, TimeSpan.FromHours(1));
        }

        if (size == TimeSpan.FromHours(6))
        {
            var groups = await period
                .GroupBy(log => new { log.Instant.Year, log.Instant.Month, log.Instant.Day, Block = log.Instant.Hour / 6 })
                .Select(group => new VolumeRow(
                    group.Key.Year,
                    group.Key.Month,
                    group.Key.Day,
                    group.Key.Block * 6,
                    group.Count(),
                    group.Sum(log => (long)log.InputTokens),
                    group.Sum(log => (long)log.OutputTokens),
                    group.Sum(log => log.CostUsd),
                    group.Average(log => (double)log.DurationMs)))
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            return Buckets(groups, TimeSpan.FromHours(6));
        }

        var days = await period
            .GroupBy(log => new { log.Instant.Year, log.Instant.Month, log.Instant.Day })
            .Select(group => new VolumeRow(
                group.Key.Year,
                group.Key.Month,
                group.Key.Day,
                0,
                group.Count(),
                group.Sum(log => (long)log.InputTokens),
                group.Sum(log => (long)log.OutputTokens),
                group.Sum(log => log.CostUsd),
                group.Average(log => (double)log.DurationMs)))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return Buckets(days, TimeSpan.FromDays(1));
    }

    private static IReadOnlyList<TimeBucket> Buckets(List<VolumeRow> rows, TimeSpan size)
    {
        return
        [
            .. rows
                .Select(row =>
                {
                    var start = DateTime.SpecifyKind(
                        new DateTime(row.Year, row.Month, row.Day, row.Hour, 0, 0),
                        DateTimeKind.Utc);
                    return new TimeBucket(
                        start,
                        start + size,
                        row.Count,
                        row.InputTokens,
                        row.OutputTokens,
                        row.CostUsd,
                        row.AverageDurationMs);
                })
                .OrderBy(bucket => bucket.FromUtc),
        ];
    }

    /// <summary>Shape of the grouped volume query; the bucket start is rebuilt in memory.</summary>
    private sealed record VolumeRow(
        int Year,
        int Month,
        int Day,
        int Hour,
        int Count,
        long InputTokens,
        long OutputTokens,
        decimal CostUsd,
        double AverageDurationMs);

    private static IQueryable<CallLog> Sort(IQueryable<CallLog> rows, string? sortBy, bool descending)
    {
        return (sortBy, descending) switch
        {
            ("Tool", true) => rows.OrderByDescending(log => log.Tool),
            ("Tool", false) => rows.OrderBy(log => log.Tool),
            ("Provider", true) => rows.OrderByDescending(log => log.Provider),
            ("Provider", false) => rows.OrderBy(log => log.Provider),
            ("DurationMs", true) => rows.OrderByDescending(log => log.DurationMs),
            ("DurationMs", false) => rows.OrderBy(log => log.DurationMs),
            ("ResultingAction", true) => rows.OrderByDescending(log => log.ResultingAction),
            ("ResultingAction", false) => rows.OrderBy(log => log.ResultingAction),
            ("Status", true) => rows.OrderByDescending(log => log.Status),
            ("Status", false) => rows.OrderBy(log => log.Status),
            (_, true) => rows.OrderByDescending(log => log.Instant),
            _ => rows.OrderBy(log => log.Instant),
        };
    }
}
