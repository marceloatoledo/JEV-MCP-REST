using System.Text.Json;
using JevMcp.Tools;

namespace JevMcp.Tests.Tools;

public class EvidenceInputTests
{
    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();

    [Fact]
    public void PlainStringBecomesASingleDocumentNamedEvidence()
    {
        var items = EvidenceInput.Parse(Json("\"the sky is blue\""));

        var item = Assert.Single(items);
        Assert.Equal("evidence", item.Id);
        Assert.Equal("the sky is blue", item.Text);
    }

    [Fact]
    public void SingleObjectKeepsItsIdWhenItHasOne()
    {
        var items = EvidenceInput.Parse(Json("""{"id": "rfc-1", "text": "body"}"""));

        var item = Assert.Single(items);
        Assert.Equal("rfc-1", item.Id);
        Assert.Equal("body", item.Text);
    }

    [Fact]
    public void ObjectWithoutIdIsLeftForTheServiceToNumber()
    {
        var item = Assert.Single(EvidenceInput.Parse(Json("""{"text": "body"}""")));

        Assert.Null(item.Id);
    }

    [Fact]
    public void ArrayKeepsOrderAndPerItemIds()
    {
        var items = EvidenceInput.Parse(Json("""[{"id": "a", "text": "first"}, {"text": "second"}]"""));

        Assert.Equal(2, items.Count);
        Assert.Equal("a", items[0].Id);
        Assert.Equal("second", items[1].Text);
    }

    [Theory]
    [InlineData("42")]
    [InlineData("null")]
    [InlineData("""{"id": "a"}""")]
    [InlineData("""{"text": 7}""")]
    [InlineData("""[{"id": "a"}]""")]
    public void AnythingWithoutTextIsRejected(string json)
    {
        Assert.Throws<ArgumentException>(() => EvidenceInput.Parse(Json(json)));
    }
}
