using System.ComponentModel;
using KashFlow;
using ModelContextProtocol.Server;

namespace KashflowMCP.Tools;

[McpServerToolType]
public sealed class PaymentTools(KashFlowSession session)
{
    [McpServerTool(Name = "get_invoice_payments"), Description("Get payments recorded against an invoice number.")]
    public async Task<string> GetInvoicePayments(
        [Description("Invoice number")] int invoiceNumber,
        CancellationToken cancellationToken)
        => JsonResults.Serialize(await session.Client.GetInvoicePaymentAsync(invoiceNumber, cancellationToken));

    [McpServerTool(Name = "get_invoice_payments_by_date_range"), Description("Get invoice payments in a date range (paged).")]
    public async Task<string> GetInvoicePaymentsByDateRange(
        [Description("Start date")] DateTime startDate,
        [Description("End date")] DateTime endDate,
        [Description("Page number")] int pageNumber,
        CancellationToken cancellationToken)
        => JsonResults.Serialize(await session.Client.GetInvoicePaymentsByDateRangeAsync(startDate, endDate, pageNumber, cancellationToken));

    [McpServerTool(Name = "get_invoice_payment_methods"), Description("List invoice payment methods.")]
    public async Task<string> GetInvoicePaymentMethods(CancellationToken cancellationToken)
        => JsonResults.Serialize(await session.Client.GetInvPayMethodsAsync(cancellationToken));

    [McpServerTool(Name = "get_customer_advance_payments"), Description("List advance payments for a customer ID.")]
    public async Task<string> GetCustomerAdvancePayments(
        [Description("Customer ID")] int customerId,
        CancellationToken cancellationToken)
        => JsonResults.Serialize(await session.Client.GetCustomerAdvancePaymentsAsync(customerId, cancellationToken));

    [McpServerTool(Name = "get_receipt_payments"), Description("List payments already recorded against a purchase receipt (SOAP GetReceiptPayment). Call this before insert_receipt_payment so the same receipt is not paid twice. payInvoice is the receipt number. payAccount is the bank account id (not the nominal code). payMethod is the payment method id. payId is the payment's own id.")]
    public async Task<string> GetReceiptPayments(
        [Description("Purchase receipt number")] int receiptNumber,
        CancellationToken cancellationToken)
        => JsonResults.Serialize(await session.Client.GetReceiptPaymentAsync(receiptNumber, cancellationToken));

    [McpServerTool(Name = "get_receipt_payment_methods"), Description("List payment methods for purchase-receipt payments (SOAP GetRecPayMethods). methodId is the value to pass as paymentMethodId to insert_receipt_payment. methodName is the label, for example Bank Transfer.")]
    public async Task<string> GetReceiptPaymentMethods(CancellationToken cancellationToken)
        => JsonResults.Serialize(await session.Client.GetRecPayMethodsAsync(cancellationToken));
}
