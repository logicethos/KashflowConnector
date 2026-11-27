using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using KashFlow;
using KashflowMCP.Rest;

namespace KashflowMCP.Tools;

/// <summary>
/// Builds and validates REST <c>POST /v2/purchases/bulk/payments</c> requests
/// (UI “New batch payment”: one bank line for many receipts).
/// A positive total is PaidOut; a negative total is PaidIn (purchase credit notes).
/// </summary>
public static class PayPurchasesBulk
{
    private static readonly JsonSerializerOptions RequestJsonOptions = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = null,
    };

    public sealed record Allocation(int ReceiptNumber, decimal Amount);

    public sealed record PlannedBulkPayment(
        BulkPurchasePaymentRequest Request,
        IReadOnlyList<AllocateBankPayment.PlannedPayment> Planned);

    public static PlannedBulkPayment Build(
        DateTime paymentDate,
        decimal totalAmount,
        IReadOnlyList<Allocation> allocations,
        IReadOnlyDictionary<int, Receipt2> receiptsByNumber,
        int bankAccountId,
        int paymentMethodId,
        string? note)
    {
        if (bankAccountId <= 0)
            throw new ArgumentException("bankAccountId must be an accountId from get_bank_accounts.");
        if (paymentMethodId <= 0)
            throw new ArgumentException("paymentMethodId must be a methodId from get_receipt_payment_methods.");

        var planned = AllocateBankPayment.Validate(
            totalAmount,
            allocations.Select(item => new AllocateBankPayment.Allocation(item.ReceiptNumber, item.Amount)).ToList(),
            receiptsByNumber);

        var request = new BulkPurchasePaymentRequest
        {
            Date = paymentDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            Comment = string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
            AccountId = bankAccountId,
            Method = paymentMethodId,
            PaymentItems = planned
                .Select(item => new BulkPurchasePaymentItem
                {
                    ObjectNumber = item.ReceiptNumber,
                    Amount = decimal.Round(item.Amount, 2, MidpointRounding.AwayFromZero),
                })
                .ToList(),
        };

        return new PlannedBulkPayment(request, planned);
    }

    /// <summary>Exact JSON body that would be POSTed to KashFlow REST.</summary>
    public static string SerializeRequestBody(BulkPurchasePaymentRequest request)
        => JsonSerializer.Serialize(request ?? throw new ArgumentNullException(nameof(request)), RequestJsonOptions);

    /// <summary>
    /// Dry-run JSON for <c>pay_purchases_bulk</c>. The bank line follows the signed total
    /// (negative total is PaidIn / money in).
    /// </summary>
    public static string SerializeDryRun(PlannedBulkPayment planned, decimal totalAmount)
    {
        ArgumentNullException.ThrowIfNull(planned);
        var requestBodyJson = SerializeRequestBody(planned.Request);
        var bankAmount = planned.Request.PaymentItems.Sum(item => item.Amount);
        var effect = bankAmount == 0 ? null : ReceiptPaymentBankLine.DescribeBatch(bankAmount);
        return JsonResults.Serialize(new
        {
            dryRun = true,
            endpoint = "POST https://api.kashflow.com/v2/purchases/bulk/payments",
            totalAmount,
            bankLineAmount = bankAmount,
            payAccount = planned.Request.AccountId,
            payMethod = planned.Request.Method,
            bankLine = effect?.BankLine,
            moneyDirection = effect?.MoneyDirection,
            bankLineNote = effect?.BankLineNote
                ?? "Payment items sum to 0, so this bulk payment has no PaidIn or PaidOut bank line.",
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
}
