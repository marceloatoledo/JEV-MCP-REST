namespace JevMcp.Data;

/// <summary>Resolves IANA or OS time zone ids for audit and dashboard labels.</summary>
public static class DisplayTimeZone
{
    public static TimeZoneInfo Resolve(string? id)
    {
        if (string.IsNullOrWhiteSpace(id) ||
            string.Equals(id, DisplaySettings.DefaultTimeZoneId, StringComparison.OrdinalIgnoreCase))
        {
            return TimeZoneInfo.Utc;
        }

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id.Trim());
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.Utc;
        }
        catch (InvalidTimeZoneException)
        {
            return TimeZoneInfo.Utc;
        }
    }

    public static string NormalizeId(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        var zone = TimeZoneInfo.FindSystemTimeZoneById(id.Trim());
        return zone.Id;
    }

    /// <summary>SQLite and EF return persisted UTC instants as <see cref="DateTimeKind.Unspecified"/>.</summary>
    public static DateTime AsUtc(DateTime value) =>
        value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        };

    public static DateTime ToDisplayTime(DateTime instantUtc, TimeZoneInfo zone) =>
        TimeZoneInfo.ConvertTimeFromUtc(AsUtc(instantUtc), zone);

    public static string OffsetLabel(TimeZoneInfo zone, DateTime instantUtc)
    {
        if (ReferenceEquals(zone, TimeZoneInfo.Utc) ||
            string.Equals(zone.Id, DisplaySettings.DefaultTimeZoneId, StringComparison.OrdinalIgnoreCase))
        {
            return DisplaySettings.DefaultTimeZoneId;
        }

        var utc = AsUtc(instantUtc);
        var offset = zone.GetUtcOffset(utc);
        var sign = offset >= TimeSpan.Zero ? "+" : "-";
        var magnitude = offset.Duration();
        return $"UTC{sign}{magnitude.Hours:D2}:{magnitude.Minutes:D2}";
    }

    public static IReadOnlyList<TimeZoneInfo> SystemZones() =>
        TimeZoneInfo.GetSystemTimeZones()
            .OrderBy(zone => zone.BaseUtcOffset)
            .ThenBy(zone => zone.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
}
