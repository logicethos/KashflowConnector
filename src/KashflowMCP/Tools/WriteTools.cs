using System.ComponentModel;
using System.Text.Json;
using KashFlow;
using ModelContextProtocol.Server;

namespace KashflowMCP.Tools;

/// <summary>Write tools — registered only when KashFlow__ReadOnly is false.</summary>
[McpServerToolType]
public sealed class WriteTools(KashFlowClient client, McpRuntimeOptions runtime)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    [McpServerTool(Name = "insert_customer"), Description("Insert a customer. Requires ReadOnly=false. Pass customer JSON matching the Customer type.")]
    public async Task<string> InsertCustomer(
        [Description("Customer JSON")] string customerJson,
        CancellationToken cancellationToken)
    {
        ToolGuard.EnsureWritable(runtime);
        var customer = JsonSerializer.Deserialize<Customer>(customerJson, JsonOptions)
            ?? throw new ArgumentException("customerJson could not be deserialized as Customer.");
        var id = await client.InsertCustomerAsync(customer, cancellationToken);
        return JsonResults.Serialize(new { customerId = id });
    }

    [McpServerTool(Name = "insert_supplier"), Description("Insert a supplier. Requires ReadOnly=false. Pass supplier JSON matching the Supplier type.")]
    public async Task<string> InsertSupplier(
        [Description("Supplier JSON")] string supplierJson,
        CancellationToken cancellationToken)
    {
        ToolGuard.EnsureWritable(runtime);
        var supplier = JsonSerializer.Deserialize<Supplier>(supplierJson, JsonOptions)
            ?? throw new ArgumentException("supplierJson could not be deserialized as Supplier.");
        var id = await client.InsertSupplierAsync(supplier, cancellationToken);
        return JsonResults.Serialize(new { supplierId = id });
    }

    [McpServerTool(Name = "insert_invoice"), Description("Insert an invoice. Requires ReadOnly=false. Pass invoice JSON matching the Invoice type.")]
    public async Task<string> InsertInvoice(
        [Description("Invoice JSON")] string invoiceJson,
        CancellationToken cancellationToken)
    {
        ToolGuard.EnsureWritable(runtime);
        var invoice = JsonSerializer.Deserialize<Invoice>(invoiceJson, JsonOptions)
            ?? throw new ArgumentException("invoiceJson could not be deserialized as Invoice.");
        var id = await client.InsertInvoiceAsync(invoice, cancellationToken);
        return JsonResults.Serialize(new { invoiceId = id });
    }

    [McpServerTool(Name = "insert_receipt2"), Description("Insert a purchase receipt (v2). Requires ReadOnly=false. Pass Receipt2 JSON.")]
    public async Task<string> InsertReceipt2(
        [Description("Receipt2 JSON")] string receiptJson,
        CancellationToken cancellationToken)
    {
        ToolGuard.EnsureWritable(runtime);
        var receipt = JsonSerializer.Deserialize<Receipt2>(receiptJson, JsonOptions)
            ?? throw new ArgumentException("receiptJson could not be deserialized as Receipt2.");
        var id = await client.InsertReceipt2Async(receipt, cancellationToken);
        return JsonResults.Serialize(new { receiptId = id });
    }

    [McpServerTool(Name = "insert_journal"), Description("Insert a journal entry. Requires ReadOnly=false. Pass JournalEntry JSON.")]
    public async Task<string> InsertJournal(
        [Description("JournalEntry JSON")] string journalJson,
        CancellationToken cancellationToken)
    {
        ToolGuard.EnsureWritable(runtime);
        var journal = JsonSerializer.Deserialize<JournalEntry>(journalJson, JsonOptions)
            ?? throw new ArgumentException("journalJson could not be deserialized as JournalEntry.");
        var id = await client.InsertJournalAsync(journal, cancellationToken);
        return JsonResults.Serialize(new { journalId = id });
    }
}
