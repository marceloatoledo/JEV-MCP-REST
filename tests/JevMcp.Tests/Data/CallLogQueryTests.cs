using JevMcp.Data;
using Microsoft.EntityFrameworkCore;

namespace JevMcp.Tests.Data;

public sealed class CallLogQueryTests
{
    [Fact]
    public async Task DashboardAggregatesTheWindowWithoutLoadingTheFullLog()
    {
        await using var harness = await AuditHarness.CreateAsync();
        await using (var db = harness.Factory.CreateDbContext())
        {
            db.CallLogs.AddRange(
                Log(hoursAgo: 1, "jev_screen", "pass", duration: 40, input: 10, output: 2),
                Log(hoursAgo: 2, "jev_verify", "review", duration: 80, input: 20, output: 4),
                Log(hoursAgo: 40, "jev_screen", "block", duration: 10, input: 5, output: 1));
            await db.SaveChangesAsync();
        }

        var queries = new CallLogQueries(harness.Factory);
        var snapshot = await queries.GetDashboardAsync(new CallLogQuery
        {
            FromUtc = DateTime.UtcNow.AddHours(-24),
            ToUtc = DateTime.UtcNow,
        });

        Assert.Equal(2, snapshot.CallCount);
        Assert.Equal(60, snapshot.AverageLatencyMs);
        Assert.Equal(30, snapshot.InputTokens);
        Assert.Equal(6, snapshot.OutputTokens);
        Assert.Equal(2, snapshot.Actions.Count);
        Assert.Contains(snapshot.Actions, item => item.Name == "pass" && item.Count == 1);
        Assert.Contains(snapshot.Actions, item => item.Name == "review" && item.Count == 1);
    }

    [Fact]
    public async Task EmptyDashboardWindowIsNotAnError()
    {
        await using var harness = await AuditHarness.CreateAsync();
        var queries = new CallLogQueries(harness.Factory);
        var snapshot = await queries.GetDashboardAsync(new CallLogQuery
        {
            FromUtc = DateTime.UtcNow.AddHours(-1),
            ToUtc = DateTime.UtcNow,
        });

        Assert.Equal(0, snapshot.CallCount);
        Assert.Null(snapshot.AverageLatencyMs);
        Assert.Empty(snapshot.Actions);
    }

    [Fact]
    public async Task SeriesHonorCrossFiltersWithoutMaterializingTheLog()
    {
        await using var harness = await AuditHarness.CreateAsync();
        await using (var db = harness.Factory.CreateDbContext())
        {
            db.CallLogs.AddRange(
                Log(hoursAgo: 1, "jev_screen", "escalate", duration: 1200, status: "error"),
                Log(hoursAgo: 1, "jev_screen", "pass", duration: 40, status: "ok"),
                Log(hoursAgo: 1, "jev_verify", "escalate", duration: 80, status: "ok"));
            await db.SaveChangesAsync();
        }

        var queries = new CallLogQueries(harness.Factory);
        var series = await queries.GetDashboardSeriesAsync(new CallLogQuery
        {
            FromUtc = DateTime.UtcNow.AddHours(-24),
            ToUtc = DateTime.UtcNow,
            Tool = "jev_screen",
            Action = "escalate",
        });

        Assert.Equal(1, Assert.Single(series.Tools).Count);
        Assert.Equal("jev_screen", series.Tools[0].Name);
        Assert.Equal("escalate", Assert.Single(series.Actions).Name);
        Assert.Equal("error", Assert.Single(series.Statuses).Name);
        Assert.Equal(1, series.Heatmap.Single(cell => cell.Tool == "jev_screen" && cell.Action == "escalate").Count);
        Assert.Equal(0, series.Heatmap.Single(cell => cell.Tool == "jev_screen" && cell.Action == "pass").Count);
        Assert.DoesNotContain(series.Heatmap, cell => cell.Tool == "jev_verify");
        Assert.Equal(1, series.Latency.Single(bucket => bucket.MinMs == 1000).Count);
        Assert.Equal(1, series.Volume.Sum(bucket => bucket.Count));
    }

    [Fact]
    public async Task SeriesCarryConsumptionPerBucketAndPerTool()
    {
        await using var harness = await AuditHarness.CreateAsync();
        await using (var db = harness.Factory.CreateDbContext())
        {
            db.CallLogs.AddRange(
                Log(hoursAgo: 1, "jev_screen", "pass", duration: 40, input: 100, output: 20, cost: 0.000005m),
                Log(hoursAgo: 1, "jev_screen", "pass", duration: 60, input: 300, output: 40, cost: 0.000014m),
                Log(hoursAgo: 3, "jev_verify", "review", duration: 200, input: 50, output: 10, cost: 0.000003m));
            await db.SaveChangesAsync();
        }

        var queries = new CallLogQueries(harness.Factory);
        var series = await queries.GetDashboardSeriesAsync(new CallLogQuery
        {
            FromUtc = DateTime.UtcNow.AddHours(-24),
            ToUtc = DateTime.UtcNow,
        });

        var screen = series.Tools.Single(tool => tool.Name == "jev_screen");
        Assert.Equal(2, screen.Count);
        Assert.Equal(400, screen.InputTokens);
        Assert.Equal(60, screen.OutputTokens);
        Assert.Equal(0.000019m, screen.CostUsd, 9);
        Assert.Equal(50, screen.AverageDurationMs);

        var verify = series.Tools.Single(tool => tool.Name == "jev_verify");
        Assert.Equal(200, verify.AverageDurationMs);

        Assert.Equal(450, series.Volume.Sum(bucket => bucket.InputTokens));
        Assert.Equal(70, series.Volume.Sum(bucket => bucket.OutputTokens));
        Assert.Equal(0.000022m, series.Volume.Sum(bucket => bucket.CostUsd), 9);

        var busiest = series.Volume.Single(bucket => bucket.Count == 2);
        Assert.Equal(400, busiest.InputTokens);
        Assert.Equal(50, busiest.AverageDurationMs);
    }

    [Fact]
    public async Task EmptyWindowSeriesAreEmpty()
    {
        await using var harness = await AuditHarness.CreateAsync();
        var queries = new CallLogQueries(harness.Factory);
        var series = await queries.GetDashboardSeriesAsync(new CallLogQuery
        {
            FromUtc = DateTime.UtcNow.AddHours(-1),
            ToUtc = DateTime.UtcNow,
        });

        Assert.Empty(series.Volume);
        Assert.Empty(series.Tools);
        Assert.Empty(series.Actions);
        Assert.Empty(series.Statuses);
        Assert.Empty(series.Heatmap);
        Assert.Empty(series.Latency);
    }

    [Fact]
    public async Task SearchPagesAndFiltersWithoutLoadingPayloads()
    {
        await using var harness = await AuditHarness.CreateAsync();
        await using (var db = harness.Factory.CreateDbContext())
        {
            for (var i = 0; i < 5; i++)
            {
                db.CallLogs.Add(Log(
                    hoursAgo: i,
                    tool: i % 2 == 0 ? "jev_screen" : "jev_verify",
                    action: "pass",
                    payload: $"secret-document-{i}"));
            }

            await db.SaveChangesAsync();
        }

        var queries = new CallLogQueries(harness.Factory);
        var page = await queries.SearchAsync(new CallLogQuery
        {
            Tool = "jev_screen",
            SortBy = "Instant",
            SortDescending = true,
            Skip = 0,
            Take = 2,
        });

        Assert.Equal(3, page.Total);
        Assert.Equal(2, page.Items.Count);
        Assert.All(page.Items, item => Assert.Equal("jev_screen", item.Tool));
        Assert.All(page.Items, item => Assert.True(item.HasPayload));

        var detail = await queries.GetAsync(page.Items[0].Id);
        Assert.NotNull(detail);
        Assert.Contains("secret-document", detail.RequestPayload, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SearchFiltersByStatusAndLatencyTail()
    {
        await using var harness = await AuditHarness.CreateAsync();
        await using (var db = harness.Factory.CreateDbContext())
        {
            db.CallLogs.AddRange(
                Log(hoursAgo: 1, "jev_screen", "pass", duration: 40, status: "ok"),
                Log(hoursAgo: 1, "jev_screen", "escalate", duration: 1500, status: "error"),
                Log(hoursAgo: 1, "jev_verify", "review", duration: 2000, status: "ok"));
            await db.SaveChangesAsync();
        }

        var queries = new CallLogQueries(harness.Factory);
        var errors = await queries.SearchAsync(new CallLogQuery { Status = "error" });
        Assert.Equal(1, errors.Total);
        Assert.Equal("error", Assert.Single(errors.Items).Status);

        var slow = await queries.SearchAsync(new CallLogQuery { DurationMinMs = 1000 });
        Assert.Equal(2, slow.Total);
        Assert.All(slow.Items, item => Assert.True(item.DurationMs >= 1000));
    }

    private static CallLog Log(
        int hoursAgo,
        string tool,
        string action,
        int duration = 10,
        int input = 1,
        int output = 1,
        string? payload = null,
        string status = "ok",
        decimal cost = 0m) => new()
    {
        Instant = DateTime.UtcNow.AddHours(-hoursAgo),
        Tool = tool,
        Provider = "typesafe",
        Model = "jev-latest",
        DurationMs = duration,
        InputTokens = input,
        OutputTokens = output,
        CostUsd = cost,
        ResultingAction = action,
        Status = status,
        RequestPayload = payload,
    };
}
