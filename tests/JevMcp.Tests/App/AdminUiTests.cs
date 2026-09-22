using Bunit;
using JevMcp.App;
using JevMcp.App.Components.Pages;
using JevMcp.Data;
using JevMcp.Providers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MudBlazor.Services;

namespace JevMcp.Tests.App;

public sealed class AdminUiTests : BunitContext, IAsyncLifetime
{
    public AdminUiTests()
    {
        CultureScope.Set("pt-BR");
        Services.AddLocalization();
        Services.AddMudServices();
        JSInterop.Mode = JSRuntimeMode.Loose;
        JSInterop.SetupModule("/js/dashboard-charts.js");
        JSInterop.Setup<bool>("javmcpTheme.isDark").SetResult(false);
        JSInterop.SetupVoid("javmcpTheme.setDark");
        Services.AddScoped<ThemeState>();
        Services.AddSingleton<IOperationalSettings>(new FakeSettings());
        Services.AddSingleton<UiDisplayTime>();
        Services.AddSingleton<ICallAuditAdmin>(new FakeCallAuditAdmin());
        Services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        Services.AddSingleton(Options.Create(new AccessControlOptions { Username = "ada", Password = "secret" }));
        AddAuthorization().SetAuthorized("operador");
    }

    public Task InitializeAsync() => Task.CompletedTask;

    async Task IAsyncLifetime.DisposeAsync() => await DisposeAsync();

    [Fact]
    public void SettingsDoesNotRenderTheCredentialValue()
    {
        const string secret = "sk-super-secret-credential-value";
        Services.AddSingleton(new CredentialOverview(
            new JevProviderOptions { TypeSafeApiKey = secret, Model = "jev-latest" },
            new JevProviderResolution(JevProviderKind.TypeSafe, null),
            new AccessControlOptions { Username = "ada", Password = "super-secret-admin-password" }));
        Services.AddSingleton<IAccessTokenService>(new FakeTokens());

        var cut = Render<Settings>();

        Assert.Contains("TYPESAFE__API__KEY", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("ACCESSCONTROL__USERNAME", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("ada", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("presente", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain(secret, cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain(secret[4..], cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("super-secret-admin-password", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("typesafe", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("mud-data-grid", cut.Markup, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LoginShowsUsernameAndPasswordWithoutATokenField()
    {
        AddAuthorization().SetNotAuthorized();
        Services.AddSingleton(new AccessControlState());

        var cut = Render<Login>();

        Assert.Contains("Entrar", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("name=\"username\"", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("name=\"password\"", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("name=\"token\"", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void AuditEmptyStateExplainsMissingCalls()
    {
        Services.AddSingleton<ICallLogQueries>(new FakeCallLogs());

        var cut = Render<Audit>();
        cut.WaitForAssertion(() =>
            Assert.Contains("Ainda não há chamadas", cut.Markup, StringComparison.Ordinal));
    }

    [Fact]
    public void AuditOffersBulkDelete()
    {
        Services.AddSingleton<ICallLogQueries>(new FakeCallLogs());

        var cut = Render<Audit>();

        Assert.Contains("Apagar todo o histórico", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void AuditListsThePageReturnedByTheServer()
    {
        Services.AddSingleton<ICallLogQueries>(new FakeCallLogs
        {
            Page = new CallLogPage(1,
            [
                new CallLogSummary
                {
                    Id = 7,
                    Instant = DateTime.UtcNow,
                    Tool = "jev_screen",
                    Provider = "typesafe",
                    ResultingAction = "escalate",
                    Status = "ok",
                    DurationMs = 42,
                    TokenName = "Agente",
                },
            ]),
        });

        var cut = Render<Audit>();
        cut.WaitForAssertion(() =>
        {
            Assert.Contains("jev_screen", cut.Markup, StringComparison.Ordinal);
            Assert.Contains("escalate", cut.Markup, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void MaskedPrefixDoesNotContainTheSecret()
    {
        const string secret = "sk-super-secret-credential-value";
        var masked = SecretMask.Describe("TYPESAFE_API_KEY", secret);
        Assert.True(masked.Present);
        Assert.DoesNotContain(secret, masked.Display, StringComparison.Ordinal);
        Assert.StartsWith("sk-s", masked.Display, StringComparison.Ordinal);
    }

    [Fact]
    public void EmptyDashboardExplainsAndDoesNotInventCharts()
    {
        Services.AddSingleton(EmptyCredentials());
        Services.AddSingleton<ICallLogQueries>(new FakeCallLogs());

        var cut = Render<Dashboard>();
        cut.WaitForAssertion(() =>
            Assert.Contains("Nenhuma chamada neste período", cut.Markup, StringComparison.Ordinal));
        Assert.DoesNotContain("data-chart", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void DashboardWithHistoryRendersEveryConsumptionChart()
    {
        Services.AddSingleton(EmptyCredentials());
        Services.AddSingleton<ICallLogQueries>(WithHistory());

        var cut = Render<Dashboard>();
        cut.WaitForAssertion(() =>
            Assert.Contains("data-chart=\"consumption\"", cut.Markup, StringComparison.Ordinal));

        foreach (var kind in new[] { "consumption", "volume", "tools", "latency", "actions", "status", "heatmap" })
        {
            Assert.Contains($"data-chart=\"{kind}\"", cut.Markup, StringComparison.Ordinal);
        }

        Assert.Contains("Consumo no tempo", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Consumo por tool", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Chamadas e latência no tempo", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Tokens totais", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SimulatedChartClickShowsAFilterChip()
    {
        var queries = WithHistory();
        Services.AddSingleton(EmptyCredentials());
        Services.AddSingleton<ICallLogQueries>(queries);

        var cut = Render<Dashboard>();
        cut.WaitForAssertion(() => Assert.Contains("data-chart=\"volume\"", cut.Markup, StringComparison.Ordinal));

        await cut.InvokeAsync(async () => await cut.Instance.ApplyHitAsync(new ChartHit
        {
            Kind = ChartHit.Action,
            Name = "escalate",
        }));
        cut.Render();

        cut.WaitForAssertion(() => Assert.Contains("Limpar filtros", cut.Markup, StringComparison.Ordinal));
        Assert.Equal("escalate", queries.LastQuery?.Action);
        Assert.Contains("action=escalate", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void DashboardInEnglishTranslatesLabels()
    {
        using var culture = new CultureScope("en");
        Services.AddSingleton(EmptyCredentials());
        Services.AddSingleton<ICallLogQueries>(new FakeCallLogs());

        var cut = Render<Dashboard>();
        cut.WaitForAssertion(() =>
            Assert.Contains("No calls in this period", cut.Markup, StringComparison.Ordinal));
        Assert.Contains("Dashboard", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Painel", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Nenhuma chamada", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void AuditInEnglishKeepsTheToolName()
    {
        using var culture = new CultureScope("en");
        Services.AddSingleton<ICallLogQueries>(new FakeCallLogs
        {
            Page = new CallLogPage(1,
            [
                new CallLogSummary
                {
                    Id = 3,
                    Instant = DateTime.UtcNow,
                    Tool = "jev_verify",
                    Provider = "typesafe",
                    ResultingAction = "auto",
                    Status = "ok",
                    DurationMs = 10,
                    TokenName = "Agent",
                },
            ]),
        });

        var cut = Render<Audit>();
        cut.WaitForAssertion(() =>
        {
            Assert.Contains("jev_verify", cut.Markup, StringComparison.Ordinal);
            Assert.Contains("History of model calls", cut.Markup, StringComparison.Ordinal);
        });
        Assert.DoesNotContain("Histórico das chamadas", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void AuditAppliesTheQueryStringWindow()
    {
        var queries = new FakeCallLogs();
        Services.AddSingleton<ICallLogQueries>(queries);

        var navigation = Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>();
        navigation.NavigateTo("/audit?tool=jev_screen&action=escalate");

        var cut = Render<Audit>();
        cut.WaitForAssertion(() =>
        {
            Assert.Equal("jev_screen", queries.LastQuery?.Tool);
            Assert.Equal("escalate", queries.LastQuery?.Action);
        });
    }

    private static FakeCallLogs WithHistory() => new()
    {
        Snapshot = new DashboardSnapshot
        {
            CallCount = 2,
            AverageLatencyMs = 55,
            InputTokens = 120,
            OutputTokens = 40,
            TotalCostUsd = 0.0000067m,
            Actions = [new NamedCount("escalate", 2)],
        },
        Series = new DashboardSeries
        {
            Actions = [new NamedCount("escalate", 2)],
            Tools = [new ToolUsage("jev_screen", 2, 120, 40, 0.0000067m, 55)],
            Statuses = [new NamedCount("ok", 2)],
            Volume =
            [
                new TimeBucket(DateTime.UtcNow.AddHours(-1), DateTime.UtcNow, 2, 120, 40, 0.0000067m, 55),
            ],
            Latency = [new DurationBucket(0, 100, "0-100", 2)],
            Heatmap = [new HeatCell("jev_screen", "escalate", 2)],
        },
    };

    private static CredentialOverview EmptyCredentials() => new(
        new JevProviderOptions { Model = "jev-latest" },
        new JevProviderResolution(null, "No Jev provider credentials found."),
        new AccessControlOptions());
}

internal sealed class FakeCallAuditAdmin : ICallAuditAdmin
{
    public Task<int> DeleteAllAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);
}

internal sealed class FakeCallLogs : ICallLogQueries
{
    public CallLogPage Page { get; init; } = new(0, []);

    public CallLog? Detail { get; init; }

    public DashboardSnapshot Snapshot { get; init; } = new();

    public DashboardSeries Series { get; init; } = new();

    public CallLogQuery? LastQuery { get; private set; }

    public Task<DashboardSnapshot> GetDashboardAsync(
        CallLogQuery query,
        CancellationToken cancellationToken = default)
    {
        LastQuery = query;
        return Task.FromResult(Snapshot);
    }

    public Task<DashboardSeries> GetDashboardSeriesAsync(
        CallLogQuery query,
        CancellationToken cancellationToken = default)
    {
        LastQuery = query;
        return Task.FromResult(Series);
    }
    public Task<CallLogPage> SearchAsync(CallLogQuery query, CancellationToken cancellationToken = default)
    {
        LastQuery = query;
        return Task.FromResult(Page);
    }

    public Task<CallLog?> GetAsync(long id, CancellationToken cancellationToken = default) =>
        Task.FromResult(Detail);
}

internal sealed class FakeSettings : IOperationalSettings
{
    public bool AllowAnonymousMcp { get; private set; }

    public bool CapturePayloads { get; private set; }

    public int RetentionDays { get; private set; } = 30;

    public string DisplayTimeZoneId { get; private set; } = DisplaySettings.DefaultTimeZoneId;

    public IReadOnlyDictionary<string, decimal> UsdPerMillionByProvider => _rates;

    private readonly Dictionary<string, decimal> _rates = TokenPricing.ProviderSlugs
        .ToDictionary(slug => slug, _ => TokenPricing.DefaultUsdPerMillion, StringComparer.OrdinalIgnoreCase);

    public event Action? Changed;

    public decimal GetUsdPerMillionTokens(string? provider) =>
        provider is not null && _rates.TryGetValue(provider, out var rate)
            ? rate
            : TokenPricing.DefaultUsdPerMillion;

    public Task EnsureDefaultsAsync(
        bool allowAnonymousMcp,
        bool capturePayloads,
        int retentionDays,
        CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task SetAllowAnonymousMcpAsync(bool value, CancellationToken cancellationToken = default)
    {
        AllowAnonymousMcp = value;
        Changed?.Invoke();
        return Task.CompletedTask;
    }

    public Task SetCapturePayloadsAsync(bool value, CancellationToken cancellationToken = default)
    {
        CapturePayloads = value;
        Changed?.Invoke();
        return Task.CompletedTask;
    }

    public Task SetRetentionDaysAsync(int value, CancellationToken cancellationToken = default)
    {
        RetentionDays = value;
        Changed?.Invoke();
        return Task.CompletedTask;
    }

    public Task SetDisplayTimeZoneIdAsync(string timeZoneId, CancellationToken cancellationToken = default)
    {
        DisplayTimeZoneId = DisplayTimeZone.NormalizeId(timeZoneId);
        Changed?.Invoke();
        return Task.CompletedTask;
    }

    public Task SetUsdPerMillionTokensAsync(
        IReadOnlyDictionary<string, decimal> ratesByProvider,
        CancellationToken cancellationToken = default)
    {
        _rates.Clear();
        foreach (var (slug, rate) in ratesByProvider)
        {
            _rates[TokenPricing.NormalizeSlug(slug)] = rate;
        }

        Changed?.Invoke();
        return Task.CompletedTask;
    }

    public Task UpsertUsdPerMillionTokensAsync(
        string provider,
        decimal rate,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(rate);
        _rates[TokenPricing.NormalizeSlug(provider)] = rate;
        Changed?.Invoke();
        return Task.CompletedTask;
    }

    public Task DeleteUsdPerMillionTokensAsync(string provider, CancellationToken cancellationToken = default)
    {
        _rates.Remove(TokenPricing.NormalizeSlug(provider));
        Changed?.Invoke();
        return Task.CompletedTask;
    }
}

internal sealed class FakeTokens : IAccessTokenService
{
    public Task<IReadOnlyList<McpAccessToken>> ListAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<McpAccessToken>>([]);

    public Task<IssuedAccessToken> CreateAsync(string name, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<McpAccessToken?> AuthenticateAsync(string? secret, CancellationToken cancellationToken = default) =>
        Task.FromResult<McpAccessToken?>(null);

    public Task RevokeAsync(long id, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task ReactivateAsync(long id, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task DeleteAsync(long id, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task<bool> HasActiveAsync(CancellationToken cancellationToken = default) => Task.FromResult(false);

    public Task<bool> IsActiveAsync(long id, CancellationToken cancellationToken = default) => Task.FromResult(false);

    public void Touch(long id)
    {
    }
}
