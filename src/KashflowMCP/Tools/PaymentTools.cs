using System.ComponentModel;
using KashFlow;
using ModelContextProtocol.Server;

namespace KashflowMCP.Tools;

[McpServerToolType]
public sealed class PaymentTools(KashFlowClient client)
{
    [McpServerTool(Name = "get_invoice_payments"), Description("Get payments recorded against an invoice number.")]
    public async Task<string> GetInvoicePayments(
        [Description("Invoice number")] int invoiceNumber,
        CancellationToken cancellationToken)
        => JsonResults.Serialize(await client.GetInvoicePaymentAsync(invoiceNumber, cancellationToken));

    [McpServerTool(Name = "get_invoice_payments_by_date_range"), Description("Get invoice payments in a date range (paged).")]
    public async Task<string> GetInvoicePaymentsByDateRange(
        [Description("Start date")] DateTime startDate,
        [Description("End date")] DateTime endDate,
        [Description("Page number")] int pageNumber,
        CancellationToken cancellationToken)
        => JsonResults.Serialize(await client.GetInvoicePaymentsByDateRangeAsync(startDate, endDate, pageNumber, cancellationToken));

    [McpServerTool(Name = "get_invoice_payment_methods"), Description("List invoice payment methods.")]
    public async Task<string> GetInvoicePaymentMethods(CancellationToken cancellationToken)
        => JsonResults.Serialize(await client.GetInvPayMethodsAsync(cancellationToken));

    [McpServerTool(Name = "get_customer_advance_payments"), Description("List advance payments for a customer ID.")]
    public async Task<string> GetCustomerAdvancePayments(
        [Description("Customer ID")] int customerId,
        CancellationToken cancellationToken)
        => JsonResults.Serialize(await client.GetCustomerAdvancePaymentsAsync(customerId, cancellationToken));
}
