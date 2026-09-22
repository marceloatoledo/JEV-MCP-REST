using System.Security.Claims;
using System.Text.Json;
using JevMcp.Core;
using JevMcp.Data;
using JevMcp.Providers;
using JevMcp.Tools;

namespace JevMcp.App;

public sealed class PlaygroundOutcome
{
    public required bool Failed { get; init; }

    public required string Json { get; init; }

    public string? Headline { get; init; }

    public string? Action { get; init; }

    public static PlaygroundOutcome FromJson(string json)
    {
        string? status = null;
        string? action = null;
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind == JsonValueKind.Object)
            {
                if (document.RootElement.TryGetProperty("status", out var statusElement) &&
                    statusElement.ValueKind == JsonValueKind.String)
                {
                    status = statusElement.GetString();
                }

                action = ReadAction(document.RootElement);
            }
        }
        catch (JsonException)
        {
        }

        var invalid = string.Equals(status, "invalid_response", StringComparison.Ordinal);
        return new PlaygroundOutcome
        {
            Failed = invalid,
            Json = json,
            Headline = invalid ? "PlaygroundInvalid" : "PlaygroundResult",
            Action = action,
        };
    }

    public static PlaygroundOutcome FromError(string message) => new()
    {
        Failed = true,
        Json = message,
        Headline = "PlaygroundFailed",
    };

    private static string? ReadAction(JsonElement root)
    {
        if (root.TryGetProperty("action", out var action) && action.ValueKind == JsonValueKind.String)
        {
            return action.GetString();
        }

        if (root.TryGetProperty("recommendation", out var recommendation) &&
            recommendation.ValueKind == JsonValueKind.Object &&
            recommendation.TryGetProperty("action", out action) &&
            action.ValueKind == JsonValueKind.String)
        {
            return action.GetString();
        }

        return null;
    }
}

internal static class PlaygroundRun
{
    public static async Task<PlaygroundOutcome> ExecuteAsync(
        ICallAudit audit,
        ClaimsPrincipal user,
        string tool,
        Func<Task<string>> run)
    {
        ArgumentNullException.ThrowIfNull(audit);
        ArgumentNullException.ThrowIfNull(run);

        using var scope = audit.Begin(tool);
        CallAuditOrigin.Apply(scope, user);

        try
        {
            var json = await run().ConfigureAwait(false);
            var outcome = PlaygroundOutcome.FromJson(json);
            scope.Complete(json, isError: outcome.Failed);
            return outcome;
        }
        catch (Exception exception) when (
            exception is ArgumentException or JevConfigurationException or JevTransportException)
        {
            var message = StripParameter(exception.Message);
            scope.SetResponsePayload(message);
            return PlaygroundOutcome.FromError(message);
        }
    }

    private static string StripParameter(string message)
    {
        var marker = " (Parameter '";
        var index = message.IndexOf(marker, StringComparison.Ordinal);
        return index < 0 ? message : message[..index];
    }
}

internal static class PlaygroundJson
{
    public static IReadOnlyList<string> Lines(string text) =>
        [.. text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];

    public static IReadOnlyList<TextItem> Items(string json, string field)
    {
        using var document = ParseArray(json, field);
        var items = new List<TextItem>();
        foreach (var element in document.RootElement.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.Object ||
                !element.TryGetProperty("text", out var text) ||
                text.ValueKind != JsonValueKind.String)
            {
                throw new ArgumentException($"each entry of {field} must have a text string.", field);
            }

            var id = element.TryGetProperty("id", out var idElement) && idElement.ValueKind == JsonValueKind.String
                ? idElement.GetString()
                : null;
            items.Add(new TextItem(id, text.GetString()!));
        }

        return items;
    }

    public static IReadOnlyList<ClassDefinition> Classes(string json)
    {
        using var document = ParseArray(json, "classes");
        var items = new List<ClassDefinition>();
        foreach (var element in document.RootElement.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.Object ||
                !element.TryGetProperty("description", out var description) ||
                description.ValueKind != JsonValueKind.String)
            {
                throw new ArgumentException("each entry of classes must have a description string.", "classes");
            }

            var id = element.TryGetProperty("id", out var idElement) && idElement.ValueKind == JsonValueKind.String
                ? idElement.GetString()
                : null;
            items.Add(new ClassDefinition(id, description.GetString()!));
        }

        return items;
    }

    public static IReadOnlyList<DecideCandidate> Candidates(string json)
    {
        using var document = ParseArray(json, "candidates");
        var items = new List<DecideCandidate>();
        foreach (var element in document.RootElement.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.Object ||
                !element.TryGetProperty("id", out var id) ||
                id.ValueKind != JsonValueKind.String ||
                !element.TryGetProperty("description", out var description) ||
                description.ValueKind != JsonValueKind.String)
            {
                throw new ArgumentException(
                    "each entry of candidates must have id and description strings.",
                    "candidates");
            }

            items.Add(new DecideCandidate(id.GetString()!, description.GetString()!));
        }

        return items;
    }

    public static IReadOnlyList<ExtractField> Fields(string json)
    {
        return JsonSerializer.Deserialize<List<ExtractField>>(json, JevJson.Options)
            ?? throw new ArgumentException("fields must be an array of objects.", "fields");
    }

    public static JsonElement ParseEvidence(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "\"\"" : json);
            return document.RootElement.Clone();
        }
        catch (JsonException exception)
        {
            throw new ArgumentException("evidence is not valid JSON.", "evidence", exception);
        }
    }

    private static JsonDocument ParseArray(string json, string field)
    {
        try
        {
            var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "[]" : json);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                document.Dispose();
                throw new ArgumentException($"{field} must be a JSON array.", field);
            }

            return document;
        }
        catch (JsonException exception)
        {
            throw new ArgumentException($"{field} is not valid JSON.", field, exception);
        }
    }
}
