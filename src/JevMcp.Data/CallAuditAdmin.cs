using Microsoft.EntityFrameworkCore;

namespace JevMcp.Data;

/// <summary>Destructive maintenance on the call audit table (admin UI).</summary>
public interface ICallAuditAdmin
{
    /// <summary>Removes every call log after the async writer queue drains.</summary>
    Task<int> DeleteAllAsync(CancellationToken cancellationToken = default);
}

internal sealed class CallAuditAdmin : ICallAuditAdmin
{
    private readonly CallLogWriter _writer;
    private readonly IDbContextFactory<AppDbContext> _factory;

    public CallAuditAdmin(CallLogWriter writer, IDbContextFactory<AppDbContext> factory)
    {
        _writer = writer;
        _factory = factory;
    }

    public async Task<int> DeleteAllAsync(CancellationToken cancellationToken = default)
    {
        await _writer.WaitForIdleAsync(cancellationToken).ConfigureAwait(false);
        await using var db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await db.CallLogs.ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
    }
}
