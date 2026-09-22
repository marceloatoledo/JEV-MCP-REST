using JevMcp.Core;

namespace JevMcp.Tests;

public sealed class AnswersTests
{
    private static readonly string[] Expected = ["a", "b"];

    private static JevAnswer Choice(string choice, (string Key, double Value)[] probabilities, double? confidence = null)
    {
        return new JevAnswer
        {
            Kind = JevAnswerKind.Choice,
            Choice = choice,
            Confidence = confidence,
            Probabilities = probabilities.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal),
        };
    }

    [Fact]
    public void ValidChoiceAcceptsTheAnswerWithConfidence()
    {
        var validated = Answers.ValidateChoice(Choice("a", [("a", 0.7), ("b", 0.3)], 0.9), Expected);

        Assert.NotNull(validated);
        Assert.Equal("a", validated.Choice);
        Assert.Equal(0.9, validated.Confidence);
    }

    [Fact]
    public void ChoiceAcceptsASumAtTheExactTolerance()
    {
        // 0.5 + 0.49 is 0.01 away from 1, and in IEEE-754 that difference compares as greater than 0.01.
        Assert.NotNull(Answers.ValidateChoice(Choice("a", [("a", 0.5), ("b", 0.49)]), Expected));
    }

    [Fact]
    public void ChoiceAcceptsATieAtTheMaximum()
    {
        Assert.NotNull(Answers.ValidateChoice(Choice("a", [("a", 0.5), ("b", 0.5)]), Expected));
    }

    [Fact]
    public void ChoiceRejectsASumOutsideTolerance()
    {
        Assert.Null(Answers.ValidateChoice(Choice("a", [("a", 0.5), ("b", 0.4)]), Expected));
    }

    [Fact]
    public void ChoiceRejectsAMissingKey()
    {
        Assert.Null(Answers.ValidateChoice(Choice("a", [("a", 1.0)]), Expected));
    }

    [Fact]
    public void ChoiceRejectsAnUnexpectedKey()
    {
        Assert.Null(Answers.ValidateChoice(Choice("a", [("a", 0.5), ("z", 0.5)]), Expected));
    }

    [Fact]
    public void ChoiceRejectsAChoiceOutsideExpectedKeys()
    {
        Assert.Null(Answers.ValidateChoice(Choice("z", [("a", 0.5), ("b", 0.5)]), Expected));
    }

    [Fact]
    public void ChoiceRejectsAChoiceThatIsNotTheMaximum()
    {
        Assert.Null(Answers.ValidateChoice(Choice("a", [("a", 0.2), ("b", 0.8)]), Expected));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(-0.1)]
    [InlineData(1.1)]
    public void ChoiceRejectsAnInvalidProbability(double probability)
    {
        Assert.Null(Answers.ValidateChoice(Choice("a", [("a", probability), ("b", 0.3)]), Expected));
    }

    [Fact]
    public void ChoiceRejectsAnAnswerWithoutProbabilities()
    {
        var answer = new JevAnswer { Kind = JevAnswerKind.Choice, Choice = "a" };

        Assert.Null(Answers.ValidateChoice(answer, Expected));
        Assert.Null(Answers.ValidateChoice(null, Expected));
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(1.1)]
    [InlineData(double.NaN)]
    public void InvalidConfidenceBecomesUnknownWithoutInvalidatingTheAnswer(double confidence)
    {
        var validated = Answers.ValidateChoice(Choice("a", [("a", 0.7), ("b", 0.3)], confidence), Expected);

        Assert.NotNull(validated);
        Assert.Null(validated.Confidence);
    }

    [Fact]
    public void ZeroConfidenceIsAValidValueNotAbsence()
    {
        var validated = Answers.ValidateChoice(Choice("a", [("a", 0.7), ("b", 0.3)], 0), Expected);

        Assert.NotNull(validated);
        Assert.Equal(0d, validated.Confidence);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void ScoreAcceptsTheRubricRange(double score)
    {
        var validated = Answers.ValidateScore(new JevAnswer { Kind = JevAnswerKind.Score, Score = score });

        Assert.NotNull(validated);
        Assert.Equal(score, validated.Score);
        Assert.Null(validated.Confidence);
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(2.1)]
    [InlineData(double.NaN)]
    public void ScoreRejectsAValueOutsideTheRubric(double score)
    {
        Assert.Null(Answers.ValidateScore(new JevAnswer { Kind = JevAnswerKind.Score, Score = score }));
    }

    [Fact]
    public void ScoreRejectsAnAnswerWithoutAScore()
    {
        Assert.Null(Answers.ValidateScore(new JevAnswer { Kind = JevAnswerKind.Score }));
        Assert.Null(Answers.ValidateScore(null));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(0.5)]
    [InlineData(1)]
    public void NoulAcceptsAProbabilityInRangeIncludingZero(double noul)
    {
        Assert.Equal(noul, Answers.ValidateNoul(new JevAnswer { Kind = JevAnswerKind.Noul, Noul = noul }));
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(1.1)]
    [InlineData(double.NegativeInfinity)]
    public void NoulRejectsAProbabilityOutsideRange(double noul)
    {
        Assert.Null(Answers.ValidateNoul(new JevAnswer { Kind = JevAnswerKind.Noul, Noul = noul }));
    }

    [Fact]
    public void NoulRejectsAnAnswerWithoutNoul()
    {
        Assert.Null(Answers.ValidateNoul(new JevAnswer { Kind = JevAnswerKind.Noul }));
        Assert.Null(Answers.ValidateNoul(null));
    }
}
