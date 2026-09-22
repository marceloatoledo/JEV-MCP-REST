using JevMcp.Data;

namespace JevMcp.Tests.Data;

public sealed class DisplayTimeZoneTests
{
    [Fact]
    public void UnspecifiedInstantFromStorageIsTreatedAsUtcWhenFormatting()
    {
        var zone = DisplayTimeZone.Resolve(
            TimeZoneInfo.GetSystemTimeZones()
                .First(item => item.BaseUtcOffset == TimeSpan.FromHours(-3))
                .Id);
        var storedUtc = new DateTime(2026, 9, 22, 0, 30, 0, DateTimeKind.Unspecified);

        var display = DisplayTimeZone.ToDisplayTime(storedUtc, zone);

        Assert.Equal(new DateTime(2026, 9, 21, 21, 30, 0), display);
    }
}
