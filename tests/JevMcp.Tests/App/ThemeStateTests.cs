using Bunit;
using JevMcp.App;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using MudBlazor.Services;

namespace JevMcp.Tests.App;

public sealed class ThemeStateTests : BunitContext, IAsyncLifetime
{
    public ThemeStateTests()
    {
        Services.AddScoped<ThemeState>();
        Services.AddMudServices();
        JSInterop.Mode = JSRuntimeMode.Loose;
        JSInterop.Setup<bool>("javmcpTheme.isDark").SetResult(false);
        JSInterop.SetupVoid("javmcpTheme.setDark");
    }

    public Task InitializeAsync() => Task.CompletedTask;

    async Task IAsyncLifetime.DisposeAsync() => await DisposeAsync();

    [Fact]
    public async Task PersistsDarkModeThroughScopedState()
    {
        var theme = Services.GetRequiredService<ThemeState>();
        var js = Services.GetRequiredService<IJSRuntime>();

        await theme.EnsureLoadedAsync(js);
        await theme.SetDarkModeAsync(js, true);

        var again = Services.GetRequiredService<ThemeState>();
        Assert.True(again.IsDarkMode);
    }
}
