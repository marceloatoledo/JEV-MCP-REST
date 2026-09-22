using Microsoft.JSInterop;

namespace JevMcp.App;

/// <summary>Dark/light preference shared across layouts and persisted in the browser.</summary>
public sealed class ThemeState
{
    private bool _loaded;

    public bool IsDarkMode { get; private set; }

    public event Action? Changed;

    public async Task EnsureLoadedAsync(IJSRuntime js)
    {
        if (_loaded)
        {
            return;
        }

        IsDarkMode = await js.InvokeAsync<bool>("javmcpTheme.isDark").ConfigureAwait(false);
        _loaded = true;
        Changed?.Invoke();
    }

    public async Task SetDarkModeAsync(IJSRuntime js, bool isDark)
    {
        await EnsureLoadedAsync(js).ConfigureAwait(false);
        if (IsDarkMode == isDark)
        {
            return;
        }

        IsDarkMode = isDark;
        try
        {
            await js.InvokeVoidAsync("javmcpTheme.setDark", isDark).ConfigureAwait(false);
        }
        catch (JSException)
        {
            // Circuit disconnected; server state still updates for the next render.
        }

        Changed?.Invoke();
    }
}
