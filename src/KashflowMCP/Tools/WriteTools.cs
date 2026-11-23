using System.ComponentModel;
using KashFlow;
using ModelContextProtocol.Server;

namespace KashflowMCP.Tools;

/// <summary>Write tools — registered only when KashFlow__ReadOnly is false.</summary>
[McpServerToolType]
public sealed class WriteTools(KashFlowSession session, McpRuntimeOptions runtime)
{
    [McpServerTool(Name = "insert_customer"), Description("Insert a customer. Requires KashFlow__ReadOnly=false. Pass structured customer fields, or customerJson with a full Customer payload (including extra text and checkbox fields). When customerJson is set, the structured fields are ignored.")]
    public async Task<string> InsertCustomer(
        [Description("Structured customer. Ignored when customerJson is set.")] InsertCustomerInput? customer = null,
        [Description("Legacy full Customer JSON string. When set, this is used instead of the structured customer.")] string? customerJson = null,
        CancellationToken cancellationToken = default)
    {
        ToolGuard.EnsureWritable(runtime);
        var mapped = WriteInputMapper.ResolveCustomer(customer, customerJson);
        var id = await session.Client.InsertCustomerAsync(mapped, cancellationToken);
        return JsonResults.Serialize(new { customerId = id });
    }

    [McpServerTool(Name = "insert_supplier"), Description("Insert a supplier. Requires KashFlow__ReadOnly=false. Pass structured supplier fields, or supplierJson with a full Supplier payload. When supplierJson is set, the structured fields are ignored.")]
    public async Task<string> InsertSupplier(
        [Description("Structured supplier. Ignored when supplierJson is set.")] InsertSupplierInput? supplier = null,
        [Description("Legacy full Supplier JSON string. When set, this is used instead of the structured supplier.")] string? supplierJson = null,
        CancellationToken cancellationToken = default)
    {
        ToolGuard.EnsureWritable(runtime);
        var mapped = WriteInputMapper.ResolveSupplier(supplier, supplierJson);
        var id = await session.Client.InsertSupplierAsync(mapped, cancellationToken);
        return JsonResults.Serialize(new { supplierId = id });
    }

    [McpServerTool(Name = "insert_invoice"), Description("Insert a sales invoice. Requires KashFlow__ReadOnly=false. Pass structured invoice fields (customer and lines), or invoiceJson with a full Invoice payload. When invoiceJson is set, the structured fields are ignored. Net and VAT totals are summed from the lines when omitted.")]
    public async Task<string> InsertInvoice(
        [Description("Structured invoice. Ignored when invoiceJson is set.")] InsertInvoiceInput? invoice = null,
        [Description("Legacy full Invoice JSON string. When set, this is used instead of the structured invoice.")] string? invoiceJson = null,
        CancellationToken cancellationToken = default)
    {
        ToolGuard.EnsureWritable(runtime);
        var mapped = WriteInputMapper.ResolveInvoice(invoice, invoiceJson);
        var id = await session.Client.InsertInvoiceAsync(mapped, cancellationToken);
        return JsonResults.Serialize(new { invoiceId = id });
    }

    [McpServerTool(Name = "insert_receipt2"), Description("Insert a purchase receipt (Receipt2). Requires KashFlow__ReadOnly=false. Pass structured receipt fields (supplier and lines), or receiptJson with a full Receipt2 payload. When receiptJson is set, the structured fields are ignored.")]
    public async Task<string> InsertReceipt2(
        [Description("Structured receipt. Ignored when receiptJson is set.")] InsertReceiptInput? receipt = null,
        [Description("Legacy full Receipt2 JSON string. When set, this is used instead of the structured receipt.")] string? receiptJson = null,
        CancellationToken cancellationToken = default)
    {
        ToolGuard.EnsureWritable(runtime);
        var mapped = WriteInputMapper.ResolveReceipt(receipt, receiptJson);
        var id = await session.Client.InsertReceipt2Async(mapped, cancellationToken);
        return JsonResults.Serialize(new { receiptId = id });
    }

    [McpServerTool(Name = "insert_journal"), Description("Insert a journal. Requires KashFlow__ReadOnly=false. Pass structured journal fields (date and balancing lines), or journalJson with a full JournalEntry payload. When journalJson is set, the structured fields are ignored.")]
    public async Task<string> InsertJournal(
        [Description("Structured journal. Ignored when journalJson is set.")] InsertJournalInput? journal = null,
        [Description("Legacy full JournalEntry JSON string. When set, this is used instead of the structured journal.")] string? journalJson = null,
        CancellationToken cancellationToken = default)
    {
        ToolGuard.EnsureWritable(runtime);
        var mapped = WriteInputMapper.ResolveJournal(journal, journalJson);
        var id = await session.Client.InsertJournalAsync(mapped, cancellationToken);
        return JsonResults.Serialize(new { journalId = id });
    }
}
