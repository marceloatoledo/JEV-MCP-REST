using System.Diagnostics;
using System.Text.RegularExpressions;
using JevMcp.Core;

namespace JevMcp.Tools;

/// <summary>Eligible matches of a pattern, with what was left out and why.</summary>
public sealed record RegexMatches(
    IReadOnlyList<string> Candidates,
    bool Truncated,
    int TooLong,
    string? Error);

/// <summary>
/// Execution of the caller's regex. The original runs in a worker with a deadline; here
/// containment is the non-backtracking engine when the pattern allows it, and an
/// execution deadline when it does not. A problematic pattern becomes an error of that
/// field, never an exception.
/// </summary>
public static class RegexRunner
{
    public static RegexMatches Run(string document, string pattern, string? flags)
    {
        RegexOptions options;
        try
        {
            options = Options(flags);
        }
        catch (ArgumentException error)
        {
            return Failed(error.Message);
        }

        Regex regex;
        try
        {
            // NonBacktracking eliminates catastrophic backtracking by construction. It
            // refuses lookaround and backreferences, and only then do we fall back to the
            // ordinary engine, where the execution deadline is the only containment.
            regex = new Regex(pattern, options | RegexOptions.NonBacktracking, Limits.RegexTimeout);
        }
        catch (NotSupportedException)
        {
            try
            {
                regex = new Regex(pattern, options, Limits.RegexTimeout);
            }
            catch (ArgumentException error)
            {
                return Failed(error.Message);
            }
        }
        catch (ArgumentException error)
        {
            return Failed(error.Message);
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var candidates = new List<string>();
        var truncated = false;
        var tooLong = 0;
        var elapsed = Stopwatch.StartNew();

        try
        {
            foreach (Match match in regex.Matches(document))
            {
                // The Regex deadline applies per match; the clock here limits the whole
                // scan, which is what the caller expects when asking over a large document.
                if (elapsed.Elapsed > Limits.RegexTimeout)
                {
                    return Failed(TimedOut());
                }

                var value = match.Value;
                if (value.Length == 0 || !seen.Add(value))
                {
                    continue;
                }

                // An overlong match is discarded before the quantity cap, so an ineligible
                // value never occupies the slot of an eligible one.
                if (value.Length > Limits.MaxExtractCandidateChars)
                {
                    tooLong++;
                    continue;
                }

                if (candidates.Count >= Limits.MaxExtractCandidates)
                {
                    truncated = true;
                    break;
                }

                candidates.Add(value);
            }
        }
        catch (RegexMatchTimeoutException)
        {
            return Failed(TimedOut());
        }

        return new RegexMatches(candidates, truncated, tooLong, null);
    }

    /// <summary>
    /// JavaScript-style flags. `g` is implicit in a .NET scan and `u`, `y`, and `d` have
    /// no effect here; any other letter is an invalid pattern, as in the original.
    /// </summary>
    private static RegexOptions Options(string? flags)
    {
        var options = RegexOptions.None;

        foreach (var flag in flags ?? string.Empty)
        {
            options |= flag switch
            {
                'i' => RegexOptions.IgnoreCase,
                'm' => RegexOptions.Multiline,
                's' => RegexOptions.Singleline,
                'g' or 'u' or 'v' or 'y' or 'd' => RegexOptions.None,
                _ => throw new ArgumentException($"Invalid regular expression flag: {flag}", nameof(flags)),
            };
        }

        return options;
    }

    private static string TimedOut() =>
        $"regex timed out after {Limits.RegexTimeout.TotalMilliseconds:F0}ms; simplify the pattern";

    private static RegexMatches Failed(string error) => new([], false, 0, error);
}
