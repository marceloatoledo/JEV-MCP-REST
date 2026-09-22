namespace JevMcp.Data;

/// <summary>
/// Detects SQLite on the container writable layer. Without a volume, restart
/// wipes history and tokens; the operator needs to see that in the log.
/// </summary>
internal static class SqliteStorageGuard
{
    internal const string EphemeralWarning =
        "SQLite database {Path} is not on a mounted volume. Audit history and tokens will be lost when the container is recreated.";

    internal static bool ShouldWarnEphemeral(string databasePath)
    {
        var inContainer = string.Equals(
            Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER"),
            "true",
            StringComparison.OrdinalIgnoreCase);

        var mountInfo = File.Exists("/proc/self/mountinfo")
            ? File.ReadAllText("/proc/self/mountinfo")
            : null;

        return IsEphemeral(databasePath, inContainer, mountInfo);
    }

    internal static bool IsEphemeral(string databasePath, bool runningInContainer, string? mountInfo)
    {
        if (!runningInContainer)
        {
            return false;
        }

        return !IsOnNonRootMount(DataDirectory(databasePath), mountInfo);
    }

    internal static string DataDirectory(string databasePath)
    {
        if (string.IsNullOrWhiteSpace(databasePath))
        {
            return "/";
        }

        var unix = databasePath.Replace('\\', '/').TrimEnd('/');
        var slash = unix.LastIndexOf('/');
        return slash <= 0 ? "/" : unix[..slash];
    }

    internal static bool IsOnNonRootMount(string directory, string? mountInfo)
    {
        if (string.IsNullOrEmpty(mountInfo))
        {
            return false;
        }

        foreach (var line in mountInfo.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var mountPoint = ParseMountPoint(line);
            if (string.IsNullOrEmpty(mountPoint) || mountPoint == "/")
            {
                continue;
            }

            var normalized = mountPoint.TrimEnd('/');
            if (directory.Equals(normalized, StringComparison.Ordinal) ||
                directory.StartsWith(normalized + "/", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    internal static string? ParseMountPoint(string line)
    {
        var field = 0;
        var i = 0;
        while (i < line.Length && field < 5)
        {
            while (i < line.Length && line[i] == ' ')
            {
                i++;
            }

            var start = i;
            while (i < line.Length && line[i] != ' ')
            {
                i++;
            }

            if (i == start)
            {
                break;
            }

            field++;
            if (field == 5)
            {
                return UnescapeMount(line[start..i]);
            }
        }

        return null;
    }

    private static string UnescapeMount(string value)
    {
        return value
            .Replace("\\040", " ", StringComparison.Ordinal)
            .Replace("\\011", "\t", StringComparison.Ordinal)
            .Replace("\\012", "\n", StringComparison.Ordinal)
            .Replace("\\134", "\\", StringComparison.Ordinal);
    }
}
