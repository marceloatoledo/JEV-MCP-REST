using System.Text.Json;
using JevMcp.Providers;
using Microsoft.Extensions.Logging;

namespace JevMcp.Data;

/// <summary>
/// Context of the tool that originated the query. The Jev client decorator does not
/// know the tool name or the policy action; this scope carries both until write.
/// </summary>
public sealed class CallAuditScope : IDisposable
{
    private static readonly AsyncLocal<CallAuditScope?> Current = new();

    private readonly ICallLogSink _sink;
    private readonly CallAuditOptions _options;
    private readonly JevProviderOptions _secrets;
    private readonly ILogger _logger;
    private readonly CallAuditScope? _parent;
    private readonly List<CallLog> _pending = [];
    private readonly DateTime _startedUtc = DateTime.UtcNow;
    private bool _disposed;
    private bool _finished;
    private bool _failed;
    private string? _errorText;
    private string? _provider;
    private string? _model;

    private CallAuditScope(
        string tool,
        ICallLogSink sink,
        CallAuditOptions options,
        JevProviderOptions secrets,
        ILogger logger)
    {
        Tool = tool;
        _sink = sink;
        _options = options;
        _secrets = secrets;
        _logger = logger;
        _parent = Current.Value;
        Current.Value = this;
    }

    public string Tool { get; }

    public string? ResultingAction { get; private set; }

    public string? TokenName { get; private set; }

    public string? TokenPrefix { get; private set; }

    public long? AccessTokenId { get; private set; }

    internal string? RequestPayload { get; private set; }

    internal string? ResponsePayload { get; private set; }

    internal static CallAuditScope? Peek() => Current.Value;

    internal static CallAuditScope Enter(
        string tool,
        ICallLogSink sink,
        CallAuditOptions options,
        JevProviderOptions secrets,
        ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(tool);
        ArgumentNullException.ThrowIfNull(sink);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(secrets);
        ArgumentNullException.ThrowIfNull(logger);

        return new CallAuditScope(tool, sink, options, secrets, logger);
    }

    public void SetRequestPayload(string? payload)
    {
        RequestPayload = AuditText.Capture(payload, _options, _secrets);
    }

    public void SetResponsePayload(string? payload)
    {
        ResponsePayload = AuditText.Capture(payload, _options, _secrets);
    }

    public void SetResultingAction(string? action)
    {
        ResultingAction = action;
    }

    public void SetOrigin(string? tokenName, string? tokenPrefix, long? accessTokenId = null)
    {
        TokenName = tokenName;
        TokenPrefix = tokenPrefix;
        AccessTokenId = accessTokenId;
    }

    /// <summary>Stores the response text and, on success, extracts the action from the tool JSON.</summary>
    public void Complete(string? responsePayload, bool isError)
    {
        _finished = true;
        _failed = isError;
        if (isError)
        {
            _errorText = responsePayload;
        }

        TryParseProviderModel(responsePayload, out _provider, out _model);
        SetResponsePayload(responsePayload);
        ResultingAction = ResultingActionParser.FromJson(responsePayload);
    }

    internal void Add(CallLog log)
    {
        ArgumentNullException.ThrowIfNull(log);
        _pending.Add(log);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Current.Value = _parent;

        try
        {
            if (_pending.Count == 0 && _finished && !string.IsNullOrEmpty(Tool))
            {
                _pending.Add(BuildToolOnlyLog());
            }

            foreach (var log in _pending)
            {
                log.Tool = Tool;
                log.ResultingAction ??= ResultingAction;
                log.TokenName ??= TokenName;
                log.TokenPrefix ??= TokenPrefix;
                log.AccessTokenId ??= AccessTokenId;
                if (RequestPayload is not null)
                {
                    log.RequestPayload = RequestPayload;
                }

                if (ResponsePayload is not null)
                {
                    log.ResponsePayload = ResponsePayload;
                }

                _sink.Enqueue(log);
            }
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Call audit enqueue failed.");
        }
    }

    private CallLog BuildToolOnlyLog()
    {
        var duration = (int)Math.Min(int.MaxValue, (DateTime.UtcNow - _startedUtc).TotalMilliseconds);

        return new CallLog
        {
            Instant = _startedUtc,
            Tool = Tool,
            Provider = _provider,
            Model = _model,
            DurationMs = duration,
            QuestionCount = 0,
            InputTokens = 0,
            OutputTokens = 0,
            Status = _failed ? AuditText.StatusError : AuditText.StatusOk,
            Error = _failed ? _secrets.SummarizeError(_errorText ?? "") : null,
            ResultingAction = ResultingAction,
            RequestPayload = RequestPayload,
            ResponsePayload = ResponsePayload,
            TokenName = TokenName,
            TokenPrefix = TokenPrefix,
            AccessTokenId = AccessTokenId,
        };
    }

    private static void TryParseProviderModel(string? json, out string? provider, out string? model)
    {
        provider = null;
        model = null;
        if (string.IsNullOrWhiteSpace(json))
        {
            return;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.TryGetProperty("provider", out var providerProperty) &&
                providerProperty.ValueKind == JsonValueKind.String)
            {
                provider = providerProperty.GetString();
            }

            if (root.TryGetProperty("model", out var modelProperty) &&
                modelProperty.ValueKind == JsonValueKind.String)
            {
                model = modelProperty.GetString();
            }
        }
        catch (JsonException)
        {
        }
    }
}
