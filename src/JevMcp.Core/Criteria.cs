using System.Collections.ObjectModel;

namespace JevMcp.Core;

/// <summary>
/// Criterion catalogs sent to the model. Insertion order is meaningful:
/// it defines option order in the question, and changing the order changes the question.
/// </summary>
public static class Criteria
{
    /// <summary>jev_verify evidence relation mapped to the verdict (citation cookbook).</summary>
    public static readonly IReadOnlyDictionary<string, ClaimVerdict> RelationToVerdict = Ordered(
        ("supports", ClaimVerdict.Verified),
        ("contradicts", ClaimVerdict.Contradicted),
        ("says_nothing", ClaimVerdict.Unsupported));

    /// <summary>Escape exits appended to the jev_decide Choice so the model can refuse the choice.</summary>
    public static readonly IReadOnlyDictionary<string, string> DecideEscapeHatches = Ordered(
        ("ask_user", "A consequential user preference or requirement is missing; ask instead of inventing it"),
        ("investigate", "Gather missing technical or factual evidence before selecting a candidate"),
        ("none", "None of the supplied candidates fits the known requirements"));

    /// <summary>The three pairwise relations jev_compare judges overall.</summary>
    public static readonly IReadOnlyDictionary<string, string> CompareRelations = Ordered(
        ("same_fact", "Both passages state the same underlying fact or claim"),
        ("contradicts", "The passages state opposing facts about the same subject"),
        ("different_facts", "The passages discuss different subjects or make non-overlapping claims"));

    /// <summary>
    /// The same three relations per aspect. At that grain the third result often means
    /// one of the passages does not address the aspect, so the criterion says so
    /// explicitly instead of relying on the label.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> AspectRelations = Ordered(
        ("same_fact", "Both passages make comparable assertions about this aspect and they agree"),
        ("contradicts", "Both passages address this aspect and their assertions conflict"),
        ("different_facts",
            "The passages do not both make a comparable assertion about this aspect: at least one does not address it, or their mentions do not overlap"));

    /// <summary>The three jev_gate claim verdicts, mirroring the jev_verify evidence relation.</summary>
    public static readonly IReadOnlyDictionary<string, string> VerifyClaimCriteria = Ordered(
        ("verified", "The evidence clearly supports the claim"),
        ("contradicted", "The evidence contradicts the claim"),
        ("unsupported", "The evidence neither supports nor contradicts the claim"));

    private static ReadOnlyDictionary<string, TValue> Ordered<TValue>(params (string Key, TValue Value)[] entries)
    {
        var ordered = new OrderedDictionary<string, TValue>(entries.Length);
        foreach (var (key, value) in entries)
        {
            ordered.Add(key, value);
        }

        return new ReadOnlyDictionary<string, TValue>(ordered);
    }
}
