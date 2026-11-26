using System.ComponentModel;
using System.Text.Json;
using KashFlow;
using KashflowMCP.Rest;
using ModelContextProtocol.Server;

namespace KashflowMCP.Tools;

/// <summary>
/// REST-backed tools. Registered only when <c>KashFlow__MemorableWord</c> (or
/// <c>KASHFLOW_MEMORABLE_WORD</c>) is configured. Writes still require
/// <c>KashFlow__ReadOnly=false</c> unless <c>dryRun=true</c>.
/// </summary>
[McpServerToolType]
public sealed class RestTools(KashFlowSession session, McpRuntimeOptions runtime)
{
    [McpServerTool(Name = "pay_purchases_bulk"), Description("Pay several purchase receipts as one bank-feed debit via KashFlow REST POST /v2/purchases/bulk/payments (UI New batch payment). SOAP InsertReceiptPayment cannot do this — each SOAP payment creates its own bank line. Validates that allocations sum exactly to totalAmount, receipt numbers are unique, each receipt exists, and no allocation exceeds that receipt's GBP outstanding (book GBP value minus AmountPaid; for FX receipts book value is (net+VAT)/ExchangeRate). Requires KashFlow__MemorableWord (REST session auth). Requires KashFlow__ReadOnly=false unless dryRun=true. dryRun returns the exact REST request body without writing. Example: Starling 1206, 2026-09-10, £91.28 = KF04199 £2.40 + KF04200 £88.88.")]
    public async Task<string> PayPurchasesBulkAsync(
        [Description("Payment date (ISO-8601). Sent as Date (yyyy-MM-dd).")] DateTime paymentDate,
        [Description("Bank amount being paid. Must equal the sum of allocations to the penny.")] decimal totalAmount,
        [Description("Per-receipt splits. Each amount must be greater than 0. Sent as PaymentItems[{ObjectNumber, Amount}].")] List<ReceiptPaymentAllocationInput> allocations,
        [Description("Bank account id from get_bank_accounts (accountId). Sent as AccountId. Optional when bankAccountCode is set.")] int? bankAccountId = null,
        [Description("Bank account nominal code from get_bank_accounts (accountCode), for example 1206 Starling. Used only when bankAccountId is omitted.")] int? bankAccountCode = null,
        [Description("Payment method id from get_receipt_payment_methods (methodId). Sent as Method. Optional when paymentMethodName is set.")] int? paymentMethodId = null,
        [Description("Payment method name from get_receipt_payment_methods. Used only when paymentMethodId is omitted.")] string? paymentMethodName = null,
        [Description("Optional note. Sent as Comment.")] string? note = null,
        [Description("When true, validate and return the exact REST request body without writing. Default false.")] bool dryRun = false,
        CancellationToken cancellationToken = default)
    {
        if (!dryRun)
            ToolGuard.EnsureWritable(runtime);
        ArgumentNullException.ThrowIfNull(allocations);

        var rest = session.RestClient
            ?? throw new InvalidOperationException(
                "pay_purchases_bulk needs KashFlow__MemorableWord (or KASHFLOW_MEMORABLE_WORD) for REST session auth. " +
                "SOAP username/password alone are not enough. The memorable word is never logged.");

        var methodId = paymentMethodId is int givenMethod && givenMethod > 0
            ? givenMethod
            : ReceiptPaymentMapper.ResolvePaymentMethodId(
                paymentMethodId,
                paymentMethodName,
                await session.Client.GetRecPayMethodsAsync(cancellationToken));

        var accountId = bankAccountId is int givenAccount && givenAccount > 0
            ? givenAccount
            : ReceiptPaymentMapper.ResolveBankAccountId(
                bankAccountId,
                bankAccountCode,
                await session.Client.GetBankAccountsAsync(cancellationToken));

        var receipts = new Dictionary<int, Receipt2>();
        foreach (var allocation in allocations)
        {
            if (allocation.ReceiptNumber <= 0 || receipts.ContainsKey(allocation.ReceiptNumber))
                continue;
            try
            {
                var receipt = await session.Client.GetReceipt2Async(allocation.ReceiptNumber, cancellationToken);
                if (receipt is not null)
                    receipts[allocation.ReceiptNumber] = receipt;
            }
            catch (KashFlowException)
            {
                // Validate reports missing receipts.
            }
        }

        var planned = PayPurchasesBulk.Build(
            paymentDate,
            totalAmount,
            allocations.Select(item => new PayPurchasesBulk.Allocation(item.ReceiptNumber, item.Amount)).ToList(),
            receipts,
            accountId,
            methodId,
            note);

        var requestBodyJson = PayPurchasesBulk.SerializeRequestBody(planned.Request);

        if (dryRun)
        {
            return JsonResults.Serialize(new
            {
                dryRun = true,
                endpoint = "POST https://api.kashflow.com/v2/purchases/bulk/payments",
                totalAmount,
                payAccount = accountId,
                payMethod = methodId,
                request = JsonSerializer.Deserialize<JsonElement>(requestBodyJson),
                requestBodyJson,
                allocations = planned.Planned.Select(item => new
                {
                    receiptNumber = item.ReceiptNumber,
                    amount = item.Amount,
                    outstandingBefore = item.OutstandingBefore,
                }),
            });
        }

        var response = await rest.PostPurchasesBulkPaymentAsync(planned.Request, cancellationToken);
        return JsonResults.Serialize(new
        {
            dryRun = false,
            bulkPaymentNumber = response.Number,
            totalAmount,
            payAccount = accountId,
            payMethod = methodId,
            date = response.Date ?? planned.Request.Date,
            comment = response.Comment,
            reconciled = response.Reconciled,
            paymentItems = response.PaymentItems?.Select(item => new
            {
                receiptNumber = item.ObjectNumber,
                amount = item.Amount,
            }),
            requestBodyJson,
        });
    }
}
