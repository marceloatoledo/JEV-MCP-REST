using System.Text.Json;
using System.Text.Json.Nodes;
using JevMcp.Core;

namespace JevMcp.Providers;

/// <summary>Envelope common to all providers, after transport validation.</summary>
internal sealed record JevWireEnvelope(
    IReadOnlyDictionary<string, JevAnswer> Answers,
    JevUsage Usage,
    string? Model);

/// <summary>
/// Translation between the `{state, questions}` / `answers` contract and the project's types.
/// </summary>
internal static class JevWire
{
    private static readonly IReadOnlyDictionary<string, double> EmptyProbabilities =
        new Dictionary<string, double>(StringComparer.Ordinal);

    /// <summary>Builds the questions object while preserving criterion order.</summary>
    /// <param name="noulAsBoolean">The Vercel gateway names the noul primitive `boolean`.</param>
    public static JsonObject BuildQuestions(
        IReadOnlyDictionary<string, JevQuestion> questions,
        bool noulAsBoolean = false)
    {
        var payload = new JsonObject();

        foreach (var (id, question) in questions)
        {
            var node = new JsonObject
            {
                ["type"] = question is NoulQuestion && noulAsBoolean ? "boolean" : question.WireType,
                ["instructions"] = question.Instructions.ToNode(),
            };

            switch (question)
            {
                case ChoiceQuestion choice:
                    var criteria = new JsonObject();
                    foreach (var criterion in choice.Criteria)
                    {
                        criteria[criterion.Key] = criterion.Description is null
                            ? null
                            : JsonValue.Create(criterion.Description);
                    }

                    node["criteria"] = criteria;
                    break;

                case ScoreQuestion score:
                    var levels = new JsonArray();
                    foreach (var level in score.Levels)
                    {
                        levels.Add(JsonValue.Create(level));
                    }

                    node["criteria"] = levels;
                    break;

                case NoulQuestion { TrueMeaning: not null } or NoulQuestion { FalseMeaning: not null }:
                    var noul = (NoulQuestion)question;
                    node["criteria"] = new JsonObject
                    {
                        ["true"] = noul.TrueMeaning is null ? null : JsonValue.Create(noul.TrueMeaning),
                        ["false"] = noul.FalseMeaning is null ? null : JsonValue.Create(noul.FalseMeaning),
                    };
                    break;

                default:
                    break;
            }

            payload[id] = node;
        }

        return payload;
    }

    /// <summary>
    /// Validates the envelope and extracts answers, usage, and the effective model. Individual
    /// answers are not judged here: a malformed question in a batch of 64 must not take down
    /// the other 63.
    /// </summary>
    public static JevWireEnvelope ReadEnvelope(
        JsonElement body,
        string label,
        string inputTokensKey = "input_tokens",
        string outputTokensKey = "output_tokens",
        Func<JsonElement, IReadOnlyDictionary<string, JevAnswer>>? answerParser = null)
    {
        if (body.ValueKind != JsonValueKind.Object)
        {
            throw Invalid(label, "expected a JSON object.");
        }

        if (!body.TryGetProperty("answers", out var answers) || answers.ValueKind != JsonValueKind.Object)
        {
            throw Invalid(label, "expected an answers object.");
        }

        var usage = JevUsage.None;
        if (body.TryGetProperty("usage", out var usageElement) &&
            usageElement.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined))
        {
            if (usageElement.ValueKind != JsonValueKind.Object ||
                !TryReadTokenCount(usageElement, inputTokensKey, out var inputTokens) ||
                !TryReadTokenCount(usageElement, outputTokensKey, out var outputTokens))
            {
                throw Invalid(
                    label,
                    $"usage must report finite non-negative {inputTokensKey} and {outputTokensKey}.");
            }

            usage = new JevUsage(inputTokens, outputTokens);
        }

        string? model = null;
        if (body.TryGetProperty("model", out var modelElement) &&
            modelElement.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined))
        {
            if (modelElement.ValueKind != JsonValueKind.String)
            {
                throw Invalid(label, "model must be absent or a string.");
            }

            model = modelElement.GetString();
        }

        var parsed = answerParser is null ? ParseAnswers(answers) : answerParser(answers);

        return new JevWireEnvelope(parsed, usage, model);
    }

    /// <summary>
    /// Answers from the Vercel gateway: `boolean` is mapped back to noul, and Choice and
    /// Score confidence comes from provider metadata, not from the response body.
    /// </summary>
    public static IReadOnlyDictionary<string, JevAnswer> ParseVercelAnswers(
        JsonElement answers,
        IReadOnlyDictionary<string, double> confidenceById)
    {
        var parsed = new Dictionary<string, JevAnswer>(StringComparer.Ordinal);

        foreach (var property in answers.EnumerateObject())
        {
            var answer = property.Value;
            var type = answer.ValueKind == JsonValueKind.Object ? ReadString(answer, "type") : null;
            var confidence = confidenceById.TryGetValue(property.Name, out var value) ? value : (double?)null;

            parsed[property.Name] = type switch
            {
                "boolean" => new JevAnswer
                {
                    Kind = JevAnswerKind.Noul,
                    Noul = ReadNumber(answer, "probability"),
                },
                "choice" => new JevAnswer
                {
                    Kind = JevAnswerKind.Choice,
                    Choice = ReadString(answer, "choice"),
                    Probabilities = ReadNumberMap(answer, "probabilities") ?? EmptyProbabilities,
                    Confidence = confidence,
                },
                "score" => new JevAnswer
                {
                    Kind = JevAnswerKind.Score,
                    Score = ReadNumber(answer, "score"),
                    Probabilities = ReadNumberMap(answer, "probabilities") ?? EmptyProbabilities,
                    Confidence = confidence,
                },
                _ => ParseAnswer(answer),
            };
        }

        return parsed;
    }

    /// <summary>Converts the answers object, leaving anything that does not match the contract as unknown.</summary>
    public static IReadOnlyDictionary<string, JevAnswer> ParseAnswers(JsonElement answers)
    {
        var parsed = new Dictionary<string, JevAnswer>(StringComparer.Ordinal);

        foreach (var property in answers.EnumerateObject())
        {
            parsed[property.Name] = ParseAnswer(property.Value);
        }

        return parsed;
    }

    /// <summary>
    /// A single answer. A field with an unexpected type stays null instead of throwing:
    /// the tool is what reports `invalid_response` and fails closed.
    /// </summary>
    public static JevAnswer ParseAnswer(JsonElement answer)
    {
        if (answer.ValueKind != JsonValueKind.Object)
        {
            return new JevAnswer();
        }

        var type = ReadString(answer, "type");

        return new JevAnswer
        {
            Kind = type switch
            {
                "choice" => JevAnswerKind.Choice,
                "score" => JevAnswerKind.Score,
                "noul" => JevAnswerKind.Noul,
                _ => JevAnswerKind.Unknown,
            },
            Choice = ReadString(answer, "choice"),
            Score = ReadNumber(answer, "score"),
            Noul = ReadNumber(answer, "noul"),
            Confidence = ReadConfidence(answer),
            Probabilities = ReadNumberMap(answer, "probabilities"),
            Legend = ReadStringMap(answer, "legend"),
        };
    }

    public static JevTransportException Invalid(string label, string why)
    {
        return new JevTransportException($"{label} returned an invalid response: {why}");
    }

    private static bool TryReadTokenCount(JsonElement usage, string name, out int value)
    {
        value = 0;
        return usage.TryGetProperty(name, out var element) &&
            element.ValueKind == JsonValueKind.Number &&
            element.TryGetInt32(out value) &&
            value >= 0;
    }

    private static string? ReadString(JsonElement owner, string name)
    {
        return owner.TryGetProperty(name, out var element) && element.ValueKind == JsonValueKind.String
            ? element.GetString()
            : null;
    }

    /// <summary>
    /// Missing confidence is null; present but unusable becomes NaN. Tools rely on that
    /// difference: a broken confidence invalidates the answer; one that never arrived
    /// simply leaves the verdict without backing.
    /// </summary>
    private static double? ReadConfidence(JsonElement owner)
    {
        if (!owner.TryGetProperty("confidence", out var element) ||
            element.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        return element.ValueKind == JsonValueKind.Number && element.TryGetDouble(out var value)
            ? value
            : double.NaN;
    }

    private static double? ReadNumber(JsonElement owner, string name)
    {
        return owner.TryGetProperty(name, out var element) &&
            element.ValueKind == JsonValueKind.Number &&
            element.TryGetDouble(out var value)
            ? value
            : null;
    }

    private static IReadOnlyDictionary<string, double>? ReadNumberMap(JsonElement owner, string name)
    {
        if (!owner.TryGetProperty(name, out var element) || element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var map = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            if (property.Value.ValueKind == JsonValueKind.Number && property.Value.TryGetDouble(out var value))
            {
                map[property.Name] = value;
            }
        }

        return map;
    }

    private static IReadOnlyDictionary<string, string>? ReadStringMap(JsonElement owner, string name)
    {
        if (!owner.TryGetProperty(name, out var element) || element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            if (property.Value.ValueKind == JsonValueKind.String)
            {
                map[property.Name] = property.Value.GetString()!;
            }
        }

        return map;
    }
}
