using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace JevMcp.Data;

/// <summary>
/// WAL avoids <c>database is locked</c> with one background writer and admin readers.
/// busy_timeout gives the single writer time instead of failing on first contention.
/// </summary>
internal sealed class SqliteWalInterceptor : DbConnectionInterceptor
{
    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        Enable(connection);
    }

    public override Task ConnectionOpenedAsync(
        DbConnection connection,
        ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        Enable(connection);
        return Task.CompletedTask;
    }

    private static void Enable(DbConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA journal_mode=WAL;";
        command.ExecuteNonQuery();
        command.CommandText = "PRAGMA busy_timeout=5000;";
        command.ExecuteNonQuery();
    }
}
