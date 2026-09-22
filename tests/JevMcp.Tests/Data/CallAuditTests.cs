using System.Collections.Concurrent;
using JevMcp.Core;
using JevMcp.Data;
using JevMcp.Providers;
using JevMcp.Tests.Tools;
using JevMcp.Tools;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace JevMcp.Tests.Data;

public sealed class CallAuditTests
{
    [Fact]
    public async Task SuccessfulCallPersistsOperationalFieldsInSqlite()
    {
        await using var harness = await AuditHarness.CreateAsync();
        var client = new FakeJevClient(new Dictionary<string, JevAnswer>(StringComparer.Ordinal)
        {
            ["injection"] = FakeAnswers.Noul(0.01),
            ["substance"] = FakeAnswers.Noul(0.95),
        });

        var result = await JudgeScreenAsync(harness, client, capturePayloads: false);
        await harness.Writer.WaitForIdleAsync();

        var log = Assert.Single(await harness.ListAsync());
        Assert.Equal("jev_screen", log.Tool);
        Assert.Equal("typesafe", log.Provider);
        Assert.Equal("jev-latest", log.Model);
        Assert.Equal(2, log.QuestionCount);
        Assert.Equal(11, log.InputTokens);
        Assert.Equal(3, log.OutputTokens);
        Assert.Equal(
            TokenCost.ComputeUsd(11, 3, TokenPricing.DefaultUsdPerMillion),
            log.CostUsd);
        Assert.Equal(AuditText.StatusOk, log.Status);
        Assert.Equal("pass", log.ResultingAction);
        Assert.Null(log.Error);
        Assert.Null(log.RequestPayload);
        Assert.Null(log.ResponsePayload);
        Assert.True(log.DurationMs >= 0);
        Assert.Equal("pass", result.Recommendation.Action);

        await using (var db = harness.Factory.CreateDbContext())
        {
            await db.Database.OpenConnectionAsync();
            await using var command = db.Database.GetDbConnection().CreateCommand();
            command.CommandText = "PRAGMA journal_mode;";
            Assert.Equal("wal", Assert.IsType<string>(await command.ExecuteScalarAsync()), ignoreCase: true);
        }
    }

    [Fact]
    public async Task PersistenceFailureDoesNotChangeTheToolResultAndIsLogged()
    {
        var logger = new CollectingLogger<AuditingJevClient>();
        var client = new AuditingJevClient(
            new FakeJevClient(new Dictionary<string, JevAnswer>(StringComparer.Ordinal)
            {
                ["injection"] = FakeAnswers.Noul(0.01),
                ["substance"] = FakeAnswers.Noul(0.95),
            }),
            new ThrowingSink(),
            new StaticOptionsMonitor<CallAuditOptions>(new CallAuditOptions()),
            new JevProviderOptions(),
            new JevProviderResolution(JevProviderKind.TypeSafe, null),
            logger);

        var result = await new ScreenService(client).JudgeAsync(new ScreenRequest("a paragraph"));

        Assert.Equal("pass", result.Recommendation.Action);
        Assert.Contains(logger.Messages, message => message.Contains("Call audit enqueue failed", StringComparison.Ordinal));
    }

    [Fact]
    public async Task DeleteAllRemovesEveryRowAfterTheWriterDrains()
    {
        await using var harness = await AuditHarness.CreateAsync();
        await using (var db = harness.Factory.CreateDbContext())
        {
            db.CallLogs.AddRange(
                new CallLog { Instant = DateTime.UtcNow, Tool = "jev_screen", Status = AuditText.StatusOk },
                new CallLog { Instant = DateTime.UtcNow, Tool = "jev_verify", Status = AuditText.StatusOk });
            await db.SaveChangesAsync();
        }

        var admin = new CallAuditAdmin(harness.Writer, harness.Factory);
        var removed = await admin.DeleteAllAsync();

        Assert.Equal(2, removed);
        Assert.Empty(await harness.ListAsync());
    }

    [Fact]
    public async Task RetentionRemovesOldRowsAndKeepsRecentOnes()
    {
        await using var harness = await AuditHarness.CreateAsync();
        await using (var db = harness.Factory.CreateDbContext())
        {
            db.CallLogs.AddRange(
                new CallLog
                {
                    Instant = DateTime.UtcNow.AddDays(-31),
                    Tool = "jev_screen",
                    Status = AuditText.StatusOk,
                },
                new CallLog
                {
                    Instant = DateTime.UtcNow.AddDays(-2),
                    Tool = "jev_verify",
                    Status = AuditText.StatusOk,
                });
            await db.SaveChangesAsync();
        }

        var removed = await harness.Retention.PurgeAsync();

        Assert.Equal(1, removed);
        var remaining = Assert.Single(await harness.ListAsync());
        Assert.Equal("jev_verify", remaining.Tool);
    }

    [Fact]
    public async Task CredentialInAnErrorIsNotPersisted()
    {
        const string secret = "jev-super-secreta";
        var sink = new MemorySink();
        var client = new AuditingJevClient(
            new ThrowingJevClient(() => new JevTransportException($"upstream 401 Bearer {secret}")),
            sink,
            new StaticOptionsMonitor<CallAuditOptions>(new CallAuditOptions()),
            new JevProviderOptions { CompatibleApiKey = secret },
            new JevProviderResolution(JevProviderKind.Compatible, null),
            NullLogger<AuditingJevClient>.Instance);

        await Assert.ThrowsAsync<JevTransportException>(
            () => new ScreenService(client).JudgeAsync(new ScreenRequest("a paragraph")));

        var log = Assert.Single(sink.Logs);
        Assert.Equal(AuditText.StatusError, log.Status);
        Assert.NotNull(log.Error);
        Assert.DoesNotContain(secret, log.Error, StringComparison.Ordinal);
        Assert.Contains("[redacted]", log.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CaptureOffOmitsPayloadAndLongDocumentsAreTruncatedWhenOn()
    {
        await using var harness = await AuditHarness.CreateAsync(capturePayloads: true, payloadMaxChars: 80);
        var client = new FakeJevClient(new Dictionary<string, JevAnswer>(StringComparer.Ordinal)
        {
            ["injection"] = FakeAnswers.Noul(0.01),
            ["substance"] = FakeAnswers.Noul(0.95),
        });

        await JudgeScreenAsync(harness, client, capturePayloads: true, responseOverride: new string('x', 400));
        await harness.Writer.WaitForIdleAsync();

        var captured = Assert.Single(await harness.ListAsync());
        Assert.NotNull(captured.RequestPayload);
        Assert.NotNull(captured.ResponsePayload);
        Assert.EndsWith("[truncated]", captured.ResponsePayload, StringComparison.Ordinal);
        Assert.True(captured.ResponsePayload.Length <= 80);

        await using var off = await AuditHarness.CreateAsync(capturePayloads: false);
        await JudgeScreenAsync(off, client, capturePayloads: false);
        await off.Writer.WaitForIdleAsync();
        var omitted = Assert.Single(await off.ListAsync());
        Assert.Null(omitted.RequestPayload);
        Assert.Null(omitted.ResponsePayload);
    }

    [Fact]
    public async Task ExtractWithoutUpstreamCallStillPersistsToolAudit()
    {
        await using var harness = await AuditHarness.CreateAsync();
        var client = new FakeJevClient(new Dictionary<string, JevAnswer>(StringComparer.Ordinal));

        using (var scope = CallAuditScope.Enter(
            JevTools.Extract,
            harness.Writer,
            new CallAuditOptions(),
            new JevProviderOptions(),
            NullLogger.Instance))
        {
            var result = await new ExtractService(client).JudgeAsync(new ExtractRequest(
                "nothing here",
                [new ExtractField("iban", "[A-Z]{2}[0-9]{20}", "The IBAN")]));
            scope.Complete(JevJson.Serialize(result), isError: false);
        }

        await harness.Writer.WaitForIdleAsync();

        var log = Assert.Single(await harness.ListAsync());
        Assert.Equal(JevTools.Extract, log.Tool);
        Assert.Equal("none", log.Provider);
        Assert.Equal(AuditText.StatusOk, log.Status);
        Assert.Equal(0, log.QuestionCount);
        Assert.Equal(0, log.InputTokens);
    }

    [Fact]
    public void ResultingActionPicksTheWorstAmongSeveralItems()
    {
        Assert.Equal("review", ResultingActionParser.FromJson("""{"results":[{"action":"auto"},{"action":"review"}]}"""));
        Assert.Equal("block", ResultingActionParser.FromJson("""{"recommendation":{"action":"block"}}"""));
        Assert.Equal("auto", ResultingActionParser.FromJson("""{"action":"auto"}"""));
        Assert.Equal("review", ResultingActionParser.FromJson("""{"tool":"jev_decide","action":"review"}"""));
        Assert.Equal("auto", ResultingActionParser.FromJson("""{"tool":"jev_rerank","action":"auto"}"""));
        Assert.Equal("auto", ResultingActionParser.FromJson("""{"tool":"jev_rerank","ranked":[]}"""));
        Assert.Equal("escalate", ResultingActionParser.FromJson("""{"tool":"jev_rerank","status":"invalid_response"}"""));
    }

    [Fact]
    public async Task RerankActionPersistsInCallAudit()
    {
        await using var harness = await AuditHarness.CreateAsync();
        var inner = new FakeJevClient(new Dictionary<string, JevAnswer>(StringComparer.Ordinal)
        {
            ["rel_0"] = FakeAnswers.Noul(0.2),
            ["rel_1"] = FakeAnswers.Noul(0.9),
        });

        var options = new CallAuditOptions();
        var secrets = new JevProviderOptions();
        var client = new AuditingJevClient(
            inner,
            harness.Writer,
            new StaticOptionsMonitor<CallAuditOptions>(options),
            secrets,
            new JevProviderResolution(JevProviderKind.TypeSafe, null),
            NullLogger<AuditingJevClient>.Instance);

        RerankResult result;
        using (var scope = CallAuditScope.Enter(JevTools.Rerank, harness.Writer, options, secrets, NullLogger.Instance))
        {
            result = await new RerankService(client).JudgeAsync(new RerankRequest(
                "retries",
                [new("a", "Retries in handler."), new("b", "Office floors.")]));
            scope.Complete(JevJson.Serialize(result), isError: false);
        }

        await harness.Writer.WaitForIdleAsync();
        var log = Assert.Single(await harness.ListAsync());
        Assert.Equal(JevTools.Rerank, log.Tool);
        Assert.Equal("auto", log.ResultingAction);
    }

    private static async Task<ScreenResult> JudgeScreenAsync(
        AuditHarness harness,
        FakeJevClient inner,
        bool capturePayloads,
        string? responseOverride = null)
    {
        var options = new CallAuditOptions { CapturePayloads = capturePayloads, PayloadMaxChars = harness.PayloadMaxChars };
        var secrets = new JevProviderOptions();
        var client = new AuditingJevClient(
            inner,
            harness.Writer,
            new StaticOptionsMonitor<CallAuditOptions>(options),
            secrets,
            new JevProviderResolution(JevProviderKind.TypeSafe, null),
            NullLogger<AuditingJevClient>.Instance);

        using var scope = CallAuditScope.Enter(
            "jev_screen",
            harness.Writer,
            options,
            secrets,
            NullLogger.Instance);

        var result = await new ScreenService(client).JudgeAsync(new ScreenRequest("a paragraph"));
        scope.Complete(responseOverride ?? JevJson.Serialize(result), isError: false);
        return result;
    }

    [Fact]
    public async Task AuditRecordsTokenNameAndPrefixWithoutTheSecret()
    {
        await using var harness = await AuditHarness.CreateAsync();
        var inner = new FakeJevClient(new Dictionary<string, JevAnswer>(StringComparer.Ordinal)
        {
            ["injection"] = FakeAnswers.Noul(0.01),
            ["substance"] = FakeAnswers.Noul(0.95),
        });

        var options = new CallAuditOptions();
        var secrets = new JevProviderOptions();
        var client = new AuditingJevClient(
            inner,
            harness.Writer,
            new StaticOptionsMonitor<CallAuditOptions>(options),
            secrets,
            new JevProviderResolution(JevProviderKind.TypeSafe, null),
            NullLogger<AuditingJevClient>.Instance);

        using (var scope = CallAuditScope.Enter("jev_screen", harness.Writer, options, secrets, NullLogger.Instance))
        {
            scope.SetOrigin("Agente", "jevmcp_abcd", accessTokenId: 7);
            var result = await new ScreenService(client).JudgeAsync(new ScreenRequest("a paragraph"));
            scope.Complete(JevJson.Serialize(result), isError: false);
        }

        await harness.Writer.WaitForIdleAsync();
        var log = Assert.Single(await harness.ListAsync());
        Assert.Equal("Agente", log.TokenName);
        Assert.Equal("jevmcp_abcd", log.TokenPrefix);
        Assert.Equal(7L, log.AccessTokenId);
    }
}

internal sealed class AuditHarness : IAsyncDisposable
{
    private readonly string _directory;

    private AuditHarness(
        string directory,
        IDbContextFactory<AppDbContext> factory,
        CallLogWriter writer,
        CallAuditRetention retention,
        int payloadMaxChars)
    {
        _directory = directory;
        Factory = factory;
        Writer = writer;
        Retention = retention;
        PayloadMaxChars = payloadMaxChars;
    }

    public IDbContextFactory<AppDbContext> Factory { get; }

    public CallLogWriter Writer { get; }

    public CallAuditRetention Retention { get; }

    public int PayloadMaxChars { get; }

    public static async Task<AuditHarness> CreateAsync(bool capturePayloads = false, int payloadMaxChars = 4_096)
    {
        var directory = Directory.CreateTempSubdirectory("JevMcp-audit-").FullName;
        var path = Path.Combine(directory, "audit.db");
        var interceptor = new SqliteWalInterceptor();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={path}")
            .AddInterceptors(interceptor)
            .Options;
        var factory = new TestDbContextFactory(options);

        await using (var db = factory.CreateDbContext())
        {
            await db.Database.MigrateAsync();
        }

        var writer = new CallLogWriter(factory, NullLogger<CallLogWriter>.Instance);
        await writer.StartAsync(CancellationToken.None);

        var retention = new CallAuditRetention(
            factory,
            new StaticOptionsMonitor<CallAuditOptions>(new CallAuditOptions
            {
                CapturePayloads = capturePayloads,
                PayloadMaxChars = payloadMaxChars,
                RetentionDays = 30,
            }),
            NullLogger<CallAuditRetention>.Instance);

        return new AuditHarness(directory, factory, writer, retention, payloadMaxChars);
    }

    public async Task<List<CallLog>> ListAsync()
    {
        await using var db = Factory.CreateDbContext();
        return await db.CallLogs.AsNoTracking().OrderBy(log => log.Id).ToListAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await Writer.StopAsync(CancellationToken.None);
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // SQLite on Windows sometimes holds the file until GC closes the connection.
        }
    }
}

internal sealed class TestDbContextFactory : IDbContextFactory<AppDbContext>
{
    private readonly DbContextOptions<AppDbContext> _options;

    public TestDbContextFactory(DbContextOptions<AppDbContext> options)
    {
        _options = options;
    }

    public AppDbContext CreateDbContext() => new(_options);
}

internal sealed class MemorySink : ICallLogSink
{
    public List<CallLog> Logs { get; } = [];

    public void Enqueue(CallLog log) => Logs.Add(log);
}

internal sealed class ThrowingSink : ICallLogSink
{
    public void Enqueue(CallLog log) => throw new IOException("disk full");
}

internal sealed class StaticOptionsMonitor<T> : IOptionsMonitor<T> where T : class
{
    public StaticOptionsMonitor(T value)
    {
        CurrentValue = value;
    }

    public T CurrentValue { get; }

    public T Get(string? name) => CurrentValue;

    public IDisposable? OnChange(Action<T, string?> listener) => null;
}

internal sealed class CollectingLogger<T> : ILogger<T>
{
    public ConcurrentBag<string> Messages { get; } = [];

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        Messages.Add(formatter(state, exception));
    }
}
