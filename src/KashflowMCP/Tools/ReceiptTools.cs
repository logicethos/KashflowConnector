using System.ComponentModel;
using KashFlow;
using ModelContextProtocol.Server;

namespace KashflowMCP.Tools;

[McpServerToolType]
public sealed class ReceiptTools(KashFlowClient client)
{
    [McpServerTool(Name = "get_receipts2"), Description("List purchase receipts (v2).")]
    public async Task<string> GetReceipts2(CancellationToken cancellationToken)
        => JsonResults.Serialize(await client.GetReceipts2Async(cancellationToken));

    [McpServerTool(Name = "get_receipt2"), Description("Get a purchase receipt by number (v2).")]
    public async Task<string> GetReceipt2(
        [Description("Receipt number")] int receiptNumber,
        CancellationToken cancellationToken)
        => JsonResults.Serialize(await client.GetReceipt2Async(receiptNumber, cancellationToken));

    [McpServerTool(Name = "get_receipts_recent2"), Description("Get the most recent purchase receipts (v2).")]
    public async Task<string> GetReceiptsRecent2(
        [Description("How many recent receipts to return")] int numberOfReceipts,
        CancellationToken cancellationToken)
        => JsonResults.Serialize(await client.GetReceipts_Recent2Async(numberOfReceipts, cancellationToken));

    [McpServerTool(Name = "get_receipts_for_supplier2"), Description("List receipts for a supplier (v2).")]
    public async Task<string> GetReceiptsForSupplier2(
        [Description("Supplier ID")] int supplierId,
        CancellationToken cancellationToken)
        => JsonResults.Serialize(await client.GetReceiptForSupplier2Async(supplierId, cancellationToken));

    [McpServerTool(Name = "get_receipts_with_paging2"), Description("Paged purchase receipts (v2). filterBy examples: All, Unpaid, Paid.")]
    public async Task<string> GetReceiptsWithPaging2(
        [Description("Page number (1-based)")] int page,
        [Description("Page size")] int perPage,
        [Description("Filter (e.g. All, Unpaid, Paid)")] string filterBy,
        CancellationToken cancellationToken)
        => JsonResults.Serialize(await client.GetReceiptsWithPaging2Async(page, perPage, filterBy, cancellationToken));

    [McpServerTool(Name = "get_receipt_notes"), Description("Get notes for a receipt ID.")]
    public async Task<string> GetReceiptNotes(
        [Description("Receipt ID")] int receiptId,
        CancellationToken cancellationToken)
        => JsonResults.Serialize(await client.GetReceiptNotesAsync(receiptId, cancellationToken));
}
