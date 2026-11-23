using System.ComponentModel;
using KashFlow;
using ModelContextProtocol.Server;

namespace KashflowMCP.Tools;

[McpServerToolType]
public sealed class InvoiceTools(KashFlowSession session)
{
    [McpServerTool(Name = "get_invoice"), Description("Get an invoice by invoice number.")]
    public async Task<string> GetInvoice(
        [Description("Invoice number")] int invoiceNumber,
        CancellationToken cancellationToken)
        => JsonResults.Serialize(await session.Client.GetInvoiceAsync(invoiceNumber, cancellationToken));

    [McpServerTool(Name = "get_invoice_by_id"), Description("Get an invoice by database ID.")]
    public async Task<string> GetInvoiceById(
        [Description("Invoice database ID")] int invoiceId,
        CancellationToken cancellationToken)
        => JsonResults.Serialize(await session.Client.GetInvoiceByIDAsync(invoiceId, cancellationToken));

    [McpServerTool(Name = "get_invoices_recent"), Description("Get the most recent invoices.")]
    public async Task<string> GetInvoicesRecent(
        [Description("How many recent invoices to return")] int numberOfInvoices,
        CancellationToken cancellationToken)
        => JsonResults.Serialize(await session.Client.GetInvoices_RecentAsync(numberOfInvoices, cancellationToken));

    [McpServerTool(Name = "get_invoices_unpaid"), Description("List unpaid invoices.")]
    public async Task<string> GetInvoicesUnpaid(CancellationToken cancellationToken)
        => JsonResults.Serialize(await session.Client.GetInvoices_UnpaidAsync(cancellationToken));

    [McpServerTool(Name = "get_invoices_overdue"), Description("List overdue invoices.")]
    public async Task<string> GetInvoicesOverdue(CancellationToken cancellationToken)
        => JsonResults.Serialize(await session.Client.GetInvoices_OverdueAsync(cancellationToken));

    [McpServerTool(Name = "get_invoices_for_customer"), Description("List invoices for a customer ID.")]
    public async Task<string> GetInvoicesForCustomer(
        [Description("Customer ID")] int customerId,
        CancellationToken cancellationToken)
        => JsonResults.Serialize(await session.Client.GetInvoicesForCustomerAsync(customerId, cancellationToken));

    [McpServerTool(Name = "get_invoices_by_date_range"), Description("List invoices between two dates (ISO-8601).")]
    public async Task<string> GetInvoicesByDateRange(
        [Description("Start date")] DateTime startDate,
        [Description("End date")] DateTime endDate,
        CancellationToken cancellationToken)
        => JsonResults.Serialize(await session.Client.GetInvoicesByDateRangeAsync(startDate, endDate, cancellationToken));

    [McpServerTool(Name = "get_invoice_notes"), Description("Get notes for an invoice ID.")]
    public async Task<string> GetInvoiceNotes(
        [Description("Invoice ID")] int invoiceId,
        CancellationToken cancellationToken)
        => JsonResults.Serialize(await session.Client.GetInvoiceNotesAsync(invoiceId, cancellationToken));
}
