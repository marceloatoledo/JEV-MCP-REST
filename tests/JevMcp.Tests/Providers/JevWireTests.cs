using System.Text.Json;
using JevMcp.Core;
using JevMcp.Providers;

namespace JevMcp.Tests;

public sealed class JevWireTests
{
    [Fact]
    public void ChoiceSerializesCriteriaInDeclaredOrder()
    {
        var questions = new Dictionary<string, JevQuestion>(StringComparer.Ordinal)
        {
            ["bucket"] = new ChoiceQuestion(
                "Qual o assunto?",
                [
                    new ChoiceCriterion("support", "algo quebrado"),
                    new ChoiceCriterion("sales", "quer comprar"),
                    new ChoiceCriterion("spam"),
                ]),
        };

        var payload = JevWire.BuildQuestions(questions).ToJsonString();

        Assert.Equal(
            """{"bucket":{"type":"choice","instructions":"Qual o assunto?","criteria":{"support":"algo quebrado","sales":"quer comprar","spam":null}}}""",
            payload);
    }

    [Fact]
    public void ScoreSerializesTheRubricAsAnOrderedList()
    {
        var questions = new Dictionary<string, JevQuestion>(StringComparer.Ordinal)
        {
            ["urgencia"] = new ScoreQuestion("Quao urgente?", ["pode esperar", "esta semana", "hoje"]),
        };

        Assert.Equal(
            """{"urgencia":{"type":"score","instructions":"Quao urgente?","criteria":["pode esperar","esta semana","hoje"]}}""",
            JevWire.BuildQuestions(questions).ToJsonString());
    }

    [Fact]
    public void NoulWithoutCriteriaOmitsTheField()
    {
        var questions = new Dictionary<string, JevQuestion>(StringComparer.Ordinal)
        {
            ["urgente"] = new NoulQuestion("Escalar agora?"),
        };

        Assert.Equal(
            """{"urgente":{"type":"noul","instructions":"Escalar agora?"}}""",
            JevWire.BuildQuestions(questions).ToJsonString());
    }

    [Fact]
    public void NoulWithCriteriaSendsTheMeaningOfEachSide()
    {
        var questions = new Dictionary<string, JevQuestion>(StringComparer.Ordinal)
        {
            ["urgente"] = new NoulQuestion("Escalar agora?", "prazo curto", "sem pressa"),
        };

        Assert.Equal(
            """{"urgente":{"type":"noul","instructions":"Escalar agora?","criteria":{"true":"prazo curto","false":"sem pressa"}}}""",
            JevWire.BuildQuestions(questions).ToJsonString());
    }

    [Fact]
    public void VercelGatewayRenamesNoulToBooleanWithoutTouchingOtherTypes()
    {
        var questions = new Dictionary<string, JevQuestion>(StringComparer.Ordinal)
        {
            ["urgente"] = new NoulQuestion("Escalar agora?"),
            ["nivel"] = new ScoreQuestion("Quao grave?", ["baixo", "alto"]),
        };

        var payload = JevWire.BuildQuestions(questions, noulAsBoolean: true).ToJsonString();

        Assert.Contains("""{"type":"boolean","instructions":"Escalar agora?"}""", payload, StringComparison.Ordinal);
        Assert.Contains("\"type\":\"score\"", payload, StringComparison.Ordinal);
    }

    [Fact]
    public void AValidEnvelopeYieldsAnswersUsageAndModel()
    {
        var envelope = Read(
            """
            {
              "model": "jev-1.13.0",
              "answers": { "urgente": { "type": "noul", "noul": 0.82 } },
              "usage": { "input_tokens": 434, "output_tokens": 75 }
            }
            """);

        Assert.Equal("jev-1.13.0", envelope.Model);
        Assert.Equal(new JevUsage(434, 75), envelope.Usage);
        Assert.Equal(0.82, Answers.ValidateNoul(envelope.Answers["urgente"]));
    }

    [Fact]
    public void EnvelopeWithoutUsageReportsZeroInsteadOfFailing()
    {
        var envelope = Read("""{"answers":{"a":{"type":"noul","noul":0.5}}}""");

        Assert.Equal(JevUsage.None, envelope.Usage);
        Assert.Null(envelope.Model);
    }

    [Theory]
    [InlineData("[]", "expected a JSON object.")]
    [InlineData("\"texto\"", "expected a JSON object.")]
    [InlineData("{}", "expected an answers object.")]
    [InlineData("""{"answers":[]}""", "expected an answers object.")]
    [InlineData("""{"answers":{},"usage":[]}""", "usage must report")]
    [InlineData("""{"answers":{},"usage":{"input_tokens":-1,"output_tokens":1}}""", "usage must report")]
    [InlineData("""{"answers":{},"usage":{"output_tokens":1}}""", "usage must report")]
    [InlineData("""{"answers":{},"model":7}""", "model must be absent or a string.")]
    public void ABrokenEnvelopeFailsTheWholeCall(string body, string expected)
    {
        var error = Assert.Throws<JevTransportException>(() => Read(body));

        Assert.StartsWith("Jev-compatible endpoint returned an invalid response:", error.Message, StringComparison.Ordinal);
        Assert.Contains(expected, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void OneMalformedAnswerInTheBatchDoesNotDropTheOthers()
    {
        var envelope = Read(
            """
            {
              "answers": {
                "boa": { "type": "noul", "noul": 0.7 },
                "quebrada": { "type": "noul", "noul": "muito" },
                "nula": null
              }
            }
            """);

        Assert.Equal(3, envelope.Answers.Count);
        Assert.Equal(0.7, Answers.ValidateNoul(envelope.Answers["boa"]));
        Assert.Null(Answers.ValidateNoul(envelope.Answers["quebrada"]));
        Assert.Null(Answers.ValidateNoul(envelope.Answers["nula"]));
    }

    [Fact]
    public void AChoiceAnswerPreservesDistributionConfidenceAndLegend()
    {
        var envelope = Read(
            """
            {
              "answers": {
                "bucket": {
                  "type": "choice",
                  "choice": "support",
                  "probabilities": { "support": 0.9, "sales": 0.1 },
                  "confidence": 0.88,
                  "legend": { "0": "baixo" }
                }
              }
            }
            """);

        var validated = Answers.ValidateChoice(envelope.Answers["bucket"], ["support", "sales"]);

        Assert.NotNull(validated);
        Assert.Equal("support", validated.Choice);
        Assert.Equal(0.88, validated.Confidence);
        Assert.Equal("baixo", envelope.Answers["bucket"].Legend!["0"]);
    }

    private static JevWireEnvelope Read(string body)
    {
        using var document = JsonDocument.Parse(body);

        return JevWire.ReadEnvelope(document.RootElement, "Jev-compatible endpoint");
    }
}
