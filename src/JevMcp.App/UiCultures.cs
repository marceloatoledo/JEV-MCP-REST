using System.Globalization;
using Microsoft.AspNetCore.Localization;

namespace JevMcp.App;

internal static class UiCultures
{
    public const string Default = "pt-BR";

    public const string CookieName = "jevmcp.culture";

    public static readonly string[] Supported = [Default, "en", "es"];

    public static bool TryNormalize(string? value, out string culture)
    {
        culture = Default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        CultureInfo info;
        try
        {
            info = CultureInfo.GetCultureInfo(value.Trim());
        }
        catch (CultureNotFoundException)
        {
            return false;
        }

        culture = info.TwoLetterISOLanguageName switch
        {
            "pt" => Default,
            "en" => "en",
            "es" => "es",
            _ => Default,
        };
        return info.TwoLetterISOLanguageName is "pt" or "en" or "es";
    }

    public static bool TryParseCookie(string? cookie, out string culture)
    {
        culture = Default;
        if (string.IsNullOrEmpty(cookie))
        {
            return false;
        }

        var parsed = CookieRequestCultureProvider.ParseCookieValue(cookie);
        if (parsed is null)
        {
            return false;
        }

        var raw = parsed.UICultures.Count > 0
            ? parsed.UICultures[0].Value
            : parsed.Cultures.Count > 0 ? parsed.Cultures[0].Value : null;
        return TryNormalize(raw, out culture);
    }

    public static string CookieValue(string culture) =>
        CookieRequestCultureProvider.MakeCookieValue(new RequestCulture(culture));

    public static string SafeReturn(string? returnUrl)
    {
        if (string.IsNullOrEmpty(returnUrl) ||
            !returnUrl.StartsWith('/') ||
            returnUrl.StartsWith("//", StringComparison.Ordinal) ||
            returnUrl.Contains('\\', StringComparison.Ordinal) ||
            returnUrl.Contains("://", StringComparison.Ordinal))
        {
            return "/";
        }

        return returnUrl;
    }
}

internal sealed class UiCultureProvider : RequestCultureProvider
{
    public override Task<ProviderCultureResult?> DetermineProviderCultureResult(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        if (httpContext.Request.Cookies.TryGetValue(UiCultures.CookieName, out var cookie) &&
            UiCultures.TryParseCookie(cookie, out var fromCookie))
        {
            return Task.FromResult<ProviderCultureResult?>(new ProviderCultureResult(fromCookie, fromCookie));
        }

        var accept = httpContext.Request.GetTypedHeaders().AcceptLanguage
            .OrderByDescending(language => language.Quality ?? 1);
        foreach (var language in accept)
        {
            if (UiCultures.TryNormalize(language.Value.Value, out var mapped))
            {
                return Task.FromResult<ProviderCultureResult?>(new ProviderCultureResult(mapped, mapped));
            }
        }

        return NullProviderCultureResult;
    }
}
