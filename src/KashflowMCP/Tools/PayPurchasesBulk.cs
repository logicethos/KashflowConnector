using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using KashFlow;
using KashflowMCP.Rest;

namespace KashflowMCP.Tools;

/// <summary>
/// Builds and validates REST <c>POST /v2/purchases/bulk/payments</c> requests
/// (UI “New batch payment”: one bank debit for many receipts).
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
}
