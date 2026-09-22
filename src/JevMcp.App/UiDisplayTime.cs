using System.Globalization;
using JevMcp.Data;

namespace JevMcp.App;

/// <summary>Formats UTC instants using the admin-configured display time zone.</summary>
public sealed class UiDisplayTime : IDisposable
{
    private readonly IOperationalSettings _settings;

    public UiDisplayTime(IOperationalSettings settings)
    {
        _settings = settings;
        _settings.Changed += OnSettingsChanged;
    }

    public event Action? Changed;

    public TimeZoneInfo Zone => DisplayTimeZone.Resolve(_settings.DisplayTimeZoneId);

    public string FormatInstant(DateTime instantUtc, string? format = null)
    {
        var local = DisplayTimeZone.ToDisplayTime(instantUtc, Zone);
        return local.ToString(format ?? "G", CultureInfo.CurrentCulture);
    }

    public string FormatInstantRange(DateTime fromUtc, DateTime toUtc) =>
        $"{FormatInstant(fromUtc, "yyyy-MM-dd HH:mm:ss")} – {FormatInstant(toUtc, "yyyy-MM-dd HH:mm:ss")}";

    public void Dispose() => _settings.Changed -= OnSettingsChanged;

    private void OnSettingsChanged() => Changed?.Invoke();
}
