using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using JevMcp.Core;
using JevMcp.Providers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace JevMcp.Data;

/// <summary>
/// Times the query and captures usage without tools knowing a database exists.
/// Persistence failure is logged here and does not cross <see cref="AskAsync"/>.
/// </summary>
internal sealed class AuditingJevClient : IJevClient
{
    private readonly IJevClient _inner;
    private readonly ICallLogSink _sink;
    private readonly IOptionsMonitor<CallAuditOptions> _options;
    private readonly JevProviderOptions _secrets;
    private readonly JevProviderResolution _resolution;
    private readonly ILogger<AuditingJevClient> _logger;
    private readonly IOperationalSettings? _settings;

    public AuditingJevClient(
        IJevClient inner,
        ICallLogSink sink,
        IOptionsMonitor<CallAuditOptions> options,
        JevProviderOptions secrets,
        JevProviderResolution resolution,
        ILogger<AuditingJevClient> logger,
        IOperationalSettings? settings = null)
    {
        _inner = inner;
        _sink = sink;
        _options = options;
        _secrets = secrets;
        _resolution = resolution;
        _logger = logger;
        _settings = settings;
    }

    public async Task<JevAskResult> AskAsync(
        JsonNode state,
        IReadOnlyDictionary<string, JevQuestion> questions,
        string? model = null,
        CancellationToken cancellationToken = default)
    {
        var instant = DateTime.UtcNow;
        var clock = Stopwatch.StartNew();
        var options = CallAuditOptionsOverlay.Effective(_options.CurrentValue, _settings);

        try
        {
            var result = await _inner
                .AskAsync(state, questions, model, cancellationToken)
                .ConfigureAwait(false);

            Record(
                instant,
                clock.Elapsed,
                questions,
                AuditText.StatusOk,
                error: null,
                result.Provider.ToString().ToLowerInvariant(),
                result.Model,
                result.Usage,
                state,
                SerializeAnswers(result),
                options);

            return result;
        }
        catch (Exception exception)
        {
            Record(
                instant,
                clock.Elapsed,
                questions,
                AuditText.StatusError,
                _secrets.SummarizeError(exception.Message),
                _resolution.Provider?.ToString().ToLowerInvariant(),
                model ?? _secrets.Model,
                JevUsage.None,
                state,
                responsePayload: null,
                options);

            throw;
        }
    }

    private void Record(
        DateTime instant,
        TimeSpan duration,
        IReadOnlyDictionary<string, JevQuestion> questions,
        string status,
        string? error,
        string? provider,
        string? model,
        JevUsage usage,
        JsonNode state,
        string? responsePayload,
        CallAuditOptions options)
    {
        var log = new CallLog
        {
            Instant = instant,
            Tool = CallAuditScope.Peek()?.Tool ?? "",
            Provider = provider,
            Model = model,
            DurationMs = duration.TotalMilliseconds > int.MaxValue ? int.MaxValue : (int)duration.TotalMilliseconds,
            InputTokens = usage.InputTokens,
            OutputTokens = usage.OutputTokens,
            CostUsd = TokenCost.ComputeUsd(
                usage.InputTokens,
                usage.OutputTokens,
                _settings?.GetUsdPerMillionTokens(provider) ?? TokenPricing.DefaultUsdPerMillion),
            QuestionCount = questions.Count,
            Status = status,
            Error = error,
            RequestPayload = AuditText.Capture(SerializeRequest(state, questions), options, _secrets),
            ResponsePayload = AuditText.Capture(responsePayload, options, _secrets),
        };

        var scope = CallAuditScope.Peek();
        if (scope is not null)
        {
            scope.Add(log);
            return;
        }

        try
        {
            _sink.Enqueue(log);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Call audit enqueue failed.");
        }
    }

    private static string SerializeRequest(JsonNode state, IReadOnlyDictionary<string, JevQuestion> questions)
    {
        var ids = new JsonArray();
        foreach (var id in questions.Keys)
        {
            ids.Add(id);
        }

        return new JsonObject
        {
            ["state"] = state.DeepClone(),
            ["questions"] = ids,
        }.ToJsonString();
    }

    private static string SerializeAnswers(JevAskResult result)
    {
        return JsonSerializer.Serialize(result.Answers);
    }
}
