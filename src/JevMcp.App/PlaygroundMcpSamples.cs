using JevMcp.Tools;

namespace JevMcp.App;

/// <summary>
/// Default JSON arguments for each playground tool, used by MCP integration tests
/// (same payloads as <see cref="Components.Pages.Playground"/>).
/// </summary>
internal static class PlaygroundMcpSamples
{
    public static IReadOnlyList<string> ToolNames { get; } =
    [
        JevTools.Extract, JevTools.Screen, JevTools.Verify, JevTools.Find, JevTools.Classify,
        JevTools.Decide, JevTools.Rerank, JevTools.Compare, JevTools.Review, JevTools.Gate,
    ];

    public static string ArgumentsJson(string tool) => tool switch
    {
        JevTools.Screen => """
            {
              "text": "A paragraph of ordinary documentation.",
              "block_at": 0.75,
              "review_at": 0.25
            }
            """,
        JevTools.Verify => """
            {
              "claims": ["The sky is blue."],
              "evidence": "The sky appears blue during the day.",
              "auto_accept": 0.8
            }
            """,
        JevTools.Find => """
            {
              "query": "authentication",
              "candidates": [
                {"id": "a", "text": "Login uses cookies."},
                {"id": "b", "text": "The office has three floors."}
              ],
              "top_k": 5
            }
            """,
        JevTools.Rerank => """
            {
              "query": "authentication",
              "candidates": [
                {"id": "a", "text": "Login uses cookies."},
                {"id": "b", "text": "The office has three floors."}
              ],
              "top_k": 5
            }
            """,
        JevTools.Classify => """
            {
              "items": [{"id": "1", "text": "Reset the password by email."}],
              "classes": [
                {"id": "auth", "description": "Authentication and credentials"},
                {"id": "other", "description": "Anything else"}
              ],
              "auto_accept": 0.8,
              "minimum_margin": 0.5
            }
            """,
        JevTools.Decide => """
            {
              "decision": "Which option should we ship?",
              "evidence": "Option A is cheaper. Option B is safer.",
              "priorities": "Prefer safety over cost.",
              "candidates": [
                {"id": "a", "description": "Ship A"},
                {"id": "b", "description": "Ship B"}
              ]
            }
            """,
        JevTools.Compare => """
            {
              "passage_a": "The service retries three times.",
              "passage_b": "The service does not retry.",
              "auto_accept": 0.8,
              "minimum_margin": 0.5
            }
            """,
        JevTools.Extract => """
            {
              "document": "INVOICE #INV-2024-0842\nBill To: Acme Corp (billing@acme.example)\nIssued: 2024-11-05\n\nSubtotal: USD 1,240.00\nTax (8%): USD 99.20\nTotal due: USD 1,339.20\n\nBudget note: prior quote capped at USD 1,200.00 — not the billed amount.\nPayment reference: REF-8842-ACME\n",
              "purpose": "Post this invoice to accounts payable.",
              "fields": [
                {"id": "invoice_id", "pattern": "INV-\\d{4}-\\d{4}", "description": "Official invoice number in the header"},
                {"id": "total_due", "pattern": "USD [0-9,]+\\.[0-9]{2}", "description": "Final amount the customer must pay (not subtotal, tax line, or budget notes)"},
                {"id": "billing_email", "pattern": "[a-z0-9._%+-]+@[a-z0-9.-]+\\.[a-z]{2,}", "description": "Billing contact email"},
                {"id": "payment_ref", "pattern": "REF-[A-Z0-9-]+", "description": "Payment reference code for remittance"}
              ],
              "auto_accept": 0.8,
              "minimum_margin": 0.5
            }
            """,
        JevTools.Review => """
            {
              "request": "Add a logout button.",
              "diff": "--- a/ui\n+++ b/ui\n@@\n+Logout\n",
              "auto_accept": 0.8,
              "review_at": 0.25,
              "composite_floor": 0.5
            }
            """,
        JevTools.Gate => """
            {
              "request": "Add a logout button.",
              "diff": "--- a/ui\n+++ b/ui\n@@\n+Logout\n",
              "claims": ["The sky is blue."],
              "evidence": "The sky appears blue during the day.",
              "auto_accept": 0.8,
              "review_at": 0.25,
              "composite_floor": 0.5
            }
            """,
        _ => throw new ArgumentOutOfRangeException(nameof(tool), tool, "Unknown playground tool."),
    };
}
