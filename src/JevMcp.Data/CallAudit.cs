using JevMcp.Providers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace JevMcp.Data;

/// <summary>Opens the audit scope for a tool call.</summary>
public interface ICallAudit
{
    CallAuditScope Begin(string tool);
}

internal sealed class CallAudit : ICallAudit
{
    private readonly ICallLogSink _sink;
    private readonly IOptionsMonitor<CallAuditOptions> _options;
    private readonly JevProviderOptions _secrets;
    private readonly ILogger<CallAudit> _logger;
    private readonly IOperationalSettings? _settings;

    public CallAudit(
        ICallLogSink sink,
        IOptionsMonitor<CallAuditOptions> options,
        JevProviderOptions secrets,
        ILogger<CallAudit> logger,
        IOperationalSettings? settings = null)
    {
        _sink = sink;
        _options = options;
        _secrets = secrets;
        _logger = logger;
        _settings = settings;
    }

    public CallAuditScope Begin(string tool)
    {
        return CallAuditScope.Enter(
            tool,
            _sink,
            CallAuditOptionsOverlay.Effective(_options.CurrentValue, _settings),
            _secrets,
            _logger);
    }
}
