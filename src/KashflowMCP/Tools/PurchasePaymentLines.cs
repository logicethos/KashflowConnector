using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using KashflowMCP.Rest;

namespace KashflowMCP.Tools;

/// <summary>
/// Read-modify-write helpers for <c>PUT /v2/purchases/{number}</c> payment lines.
/// Copies non-payment fields from the GET response so only PaymentLines change.
/// </summary>
public static class PurchasePaymentLines
{
    private static readonly JsonSerializerOptions RequestJsonOptions = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = null,
    };

    public sealed class PaymentLineEdit
    {
        public long? Id { get; set; }
        public DateTime? Date { get; set; }
        public decimal? Amount { get; set; }
        public int? AccountId { get; set; }
        public long? Method { get; set; }
        public string? Note { get; set; }
        public long? BankTransactionId { get; set; }
        public long? BFSTransactionId { get; set; }
        public int? BulkId { get; set; }
        public int? BankReconciliationId { get; set; }
        public bool? ResetBankReconciliation { get; set; }
    }

    public static PurchaseRequest BuildUpdateRequest(
        PurchaseResponse existing,
        IReadOnlyList<PaymentLineEdit> edits,
        bool replaceAll)
    {
        ArgumentNullException.ThrowIfNull(existing);
        ArgumentNullException.ThrowIfNull(edits);

        var request = ToRequestPreservingNonPaymentFields(existing);
        var current = (existing.PaymentLines ?? [])
            .Select(ToRequestLine)
            .ToList();

        if (replaceAll)
        {
            if (edits.Count == 0)
                throw new ArgumentException("replaceAll requires at least one payment line.");
            request.PaymentLines = edits.Select(ToNewLine).ToList();
            return request;
        }

        foreach (var edit in edits)
        {
            if (edit.Id is long id && id > 0)
            {
                var index = current.FindIndex(line => line.Id == id);
                if (index < 0)
                    throw new ArgumentException($"Payment line id {id} was not found on purchase {existing.Number}.");
                current[index] = ApplyEdit(current[index], edit);
            }
            else
            {
                current.Add(ToNewLine(edit));
            }
        }

        request.PaymentLines = current;
        return request;
    }

    public static string SerializeRequestBody(PurchaseRequest request)
        => JsonSerializer.Serialize(request ?? throw new ArgumentNullException(nameof(request)), RequestJsonOptions);

    public static PurchaseRequest ToRequestPreservingNonPaymentFields(PurchaseResponse existing)
    {
        ArgumentNullException.ThrowIfNull(existing);
        return new PurchaseRequest
        {
            Number = existing.Number,
            IssuedDate = existing.IssuedDate,
            DueDate = existing.DueDate,
            SupplierCode = existing.SupplierCode,
            SupplierReference = existing.SupplierReference,
            Currency = existing.Currency is null
                ? null
                : new RestCurrencyRequest
                {
                    Code = existing.Currency.Code,
                    ExchangeRate = existing.Currency.ExchangeRate,
                },
            HomeCurrencyGrossAmount = existing.HomeCurrencyGrossAmount,
            LineItems = (existing.LineItems ?? [])
                .Select(line =>
                {
                    var requestLine = new PurchaseLineItemRequest
                    {
                        Number = line.Number,
                        Description = line.Description,
                        Quantity = line.Quantity,
                        Rate = line.Rate,
                        HomeCurrencyRate = line.HomeCurrencyRate,
                        VATLevel = line.VATLevel,
                        VATExempt = line.VATExempt,
                        VATAmount = line.VATAmount,
                        HomeCurrencyVATAmount = line.HomeCurrencyVATAmount,
                        NominalCode = line.NominalCode,
                        NominalName = line.NominalName,
                        ProductCode = line.ProductCode,
                        TaxCode = line.TaxCode,
                        Disallowed = line.Disallowed,
                        ProjectNumber = line.ProjectNumber,
                        ImportDuty = line.ImportDuty,
                    };
                    FillHomeCurrency(requestLine, existing.Currency);
                    return requestLine;
                })
                .ToList(),
            PaymentLines = (existing.PaymentLines ?? []).Select(ToRequestLine).ToList(),
            ProjectNumber = existing.ProjectNumber,
            AdditionalFieldValue = existing.AdditionalFieldValue,
            IsCISReverseCharge = existing.IsCISReverseCharge,
            Type = existing.Type,
        };
    }

    /// <summary>
    /// REST line <c>Rate</c> is quote currency and <c>HomeCurrencyRate</c> is GBP.
    /// A payment-line PUT must send the home rate or KashFlow can lose the sterling amount.
    /// An existing home rate is kept. A missing one is quote / exchange rate (foreign units per £1).
    /// </summary>
    private static void FillHomeCurrency(PurchaseLineItemRequest line, RestCurrencyResponse? currency)
    {
        var code = currency?.Code;
        var foreign = !string.IsNullOrWhiteSpace(code)
            && !code.Trim().Equals("GBP", StringComparison.OrdinalIgnoreCase);
        var rate = currency?.ExchangeRate;
        if (!foreign || rate is null or <= 0m)
        {
            line.HomeCurrencyRate ??= line.Rate;
            line.HomeCurrencyVATAmount ??= line.VATAmount;
            return;
        }

        if (line.HomeCurrencyRate is null && line.Rate is decimal quoteRate)
            line.HomeCurrencyRate = decimal.Round(quoteRate / rate.Value, 2, MidpointRounding.AwayFromZero);
        if (line.HomeCurrencyVATAmount is null && line.VATAmount is decimal quoteVat)
            line.HomeCurrencyVATAmount = decimal.Round(quoteVat / rate.Value, 2, MidpointRounding.AwayFromZero);
    }

    private static PurchasePaymentLineRequest ToRequestLine(PurchasePaymentLineResponse line)
        => new()
        {
            Id = line.Id,
            Date = line.Date,
            Amount = line.Amount,
            AccountId = line.AccountId,
            Method = line.Method,
            Note = line.Note,
            BankTransactionId = line.BankTransactionId,
            BFSTransactionId = line.BFSTransactionId,
            BulkId = line.BulkId,
            BankReconciliationId = line.BankReconciliationId,
            PaymentProcessor = line.PaymentProcessor,
        };

    private static PurchasePaymentLineRequest ToNewLine(PaymentLineEdit edit)
    {
        if (edit.Amount is null || edit.Amount == 0)
            throw new ArgumentException(
                "New payment lines require a non-zero amount. Use a negative amount for a purchase credit note (PaidIn / money in bank line).");
        if (edit.AccountId is null || edit.AccountId <= 0)
            throw new ArgumentException("New payment lines require accountId (bank account id).");
        if (edit.Method is null || edit.Method <= 0)
            throw new ArgumentException("New payment lines require method (payment method id).");
        if (edit.Date is null)
            throw new ArgumentException("New payment lines require date.");

        return new PurchasePaymentLineRequest
        {
            Id = edit.Id is long id && id > 0 ? id : null,
            Date = edit.Date.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            Amount = RoundAmount(edit.Amount.Value),
            AccountId = edit.AccountId,
            Method = edit.Method,
            Note = string.IsNullOrWhiteSpace(edit.Note) ? null : edit.Note.Trim(),
            BankTransactionId = edit.BankTransactionId,
            BFSTransactionId = edit.BFSTransactionId,
            BulkId = edit.BulkId,
            BankReconciliationId = edit.BankReconciliationId,
            ResetBankReconciliation = edit.ResetBankReconciliation,
        };
    }

    private static PurchasePaymentLineRequest ApplyEdit(
        PurchasePaymentLineRequest current,
        PaymentLineEdit edit)
    {
        if (edit.Date is DateTime date)
            current.Date = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        if (edit.Amount is decimal amount)
            current.Amount = RoundAmount(amount);

        if (edit.AccountId is int accountId)
            current.AccountId = accountId;
        if (edit.Method is long method)
            current.Method = method;
        if (edit.Note is not null)
            current.Note = string.IsNullOrWhiteSpace(edit.Note) ? null : edit.Note.Trim();
        if (edit.BankTransactionId is long bankTx)
            current.BankTransactionId = bankTx;
        if (edit.BFSTransactionId is long bfs)
            current.BFSTransactionId = bfs;
        if (edit.BulkId is int bulkId)
            current.BulkId = bulkId;
        if (edit.BankReconciliationId is int recon)
            current.BankReconciliationId = recon;
        if (edit.ResetBankReconciliation is bool reset)
            current.ResetBankReconciliation = reset;
        return current;
    }

    private static decimal RoundAmount(decimal amount)
    {
        var rounded = decimal.Round(amount, 2, MidpointRounding.AwayFromZero);
        if (rounded == 0)
            throw new ArgumentException(
                "amount must be non-zero after rounding to 2 decimal places. Use a negative amount for a purchase credit note (PaidIn / money in bank line).");
        return rounded;
    }

    /// <summary>Dry-run JSON for <c>update_purchase_payment_lines</c>, including PaidIn / PaidOut.</summary>
    public static string SerializeDryRun(int receiptNumber, bool replaceAll, string endpoint, PurchaseRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var requestBodyJson = SerializeRequestBody(request);
        var bankLines = (request.PaymentLines ?? [])
            .Where(line => line.Amount is decimal amount && amount != 0)
            .Select(line => ReceiptPaymentBankLine.Describe(line.Amount!.Value))
            .ToList();

        return JsonResults.Serialize(new
        {
            dryRun = true,
            endpoint,
            receiptNumber,
            replaceAll,
            request = JsonSerializer.Deserialize<JsonElement>(requestBodyJson),
            requestBodyJson,
            bankLines,
        });
    }
}
