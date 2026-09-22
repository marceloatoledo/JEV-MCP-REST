using System.Collections;
using System.Globalization;
using System.Resources;
using JevMcp.App;
using JevMcp.App.Resources;

namespace JevMcp.Tests.App;

public sealed class UiLocalizationTests
{
    private static readonly ResourceManager Resources =
        new("JevMcp.App.Resources.Ui", typeof(Ui).Assembly);

    [Theory]
    [InlineData("pt-BR", "Painel")]
    [InlineData("en", "Dashboard")]
    [InlineData("es", "Panel")]
    public void NavDashboardFollowsTheCulture(string culture, string expected)
    {
        Assert.Equal(expected, Resources.GetString("NavDashboard", CultureInfo.GetCultureInfo(culture)));
    }

    [Fact]
    public void SatelliteResourcesHaveTheSameKeysAsInvariant()
    {
        var invariant = Resources.GetResourceSet(CultureInfo.InvariantCulture, createIfNotExists: true, tryParents: true);
        Assert.NotNull(invariant);

        foreach (var name in new[] { "en", "es" })
        {
            var satellite = Resources.GetResourceSet(CultureInfo.GetCultureInfo(name), createIfNotExists: true, tryParents: false);
            Assert.NotNull(satellite);
            foreach (DictionaryEntry entry in invariant)
            {
                var key = (string)entry.Key;
                Assert.False(
                    string.IsNullOrEmpty(satellite.GetString(key)),
                    $"A chave {key} falta em Ui.{name}.resx.");
            }
        }
    }

    [Theory]
    [InlineData("pt", "pt-BR")]
    [InlineData("pt-PT", "pt-BR")]
    [InlineData("en-US", "en")]
    [InlineData("es-MX", "es")]
    public void NormalizesKnownCultures(string incoming, string canonical)
    {
        Assert.True(UiCultures.TryNormalize(incoming, out var culture));
        Assert.Equal(canonical, culture);
    }

    [Fact]
    public void RejectsAnUnsupportedCulture()
    {
        Assert.False(UiCultures.TryNormalize("de", out _));
        Assert.Equal("/", UiCultures.SafeReturn("https://evil.example/"));
        Assert.Equal("/", UiCultures.SafeReturn("//evil.example"));
        Assert.Equal("/audit?tool=jev_verify", UiCultures.SafeReturn("/audit?tool=jev_verify"));
    }
}

internal sealed class CultureScope : IDisposable
{
    private readonly CultureInfo _culture;
    private readonly CultureInfo _ui;

    public CultureScope(string name)
    {
        _culture = CultureInfo.CurrentCulture;
        _ui = CultureInfo.CurrentUICulture;
        Set(name);
    }

    public static void Set(string name)
    {
        var culture = CultureInfo.GetCultureInfo(name);
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
    }

    public void Dispose()
    {
        CultureInfo.CurrentCulture = _culture;
        CultureInfo.CurrentUICulture = _ui;
    }
}
