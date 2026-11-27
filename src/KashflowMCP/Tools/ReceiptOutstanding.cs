using KashFlow;

namespace KashflowMCP.Tools;

/// <summary>Helpers for purchase-receipt balances and payment construction.</summary>
public static class ReceiptOutstanding
{
    /// <summary>
    /// GBP book value of a purchase receipt. SOAP payment lines are in home currency (GBP).
    /// For FX receipts, KashFlow stores <see cref="Receipt2.ExchangeRate"/> as foreign units per GBP,
    /// so book value is <c>(net + VAT) / ExchangeRate</c>. Writes keep header net and VAT in the
    /// document currency and send line rates in GBP. When ExchangeRate is unset (0), net+VAT
    /// is treated as already in GBP.
    /// </summary>
    public static decimal BookValueGbp(Receipt2 receipt)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        var foreignTotal = receipt.NetAmount + receipt.VATAmount;
        if (receipt.ExchangeRate > 0)
            return decimal.Round(foreignTotal / receipt.ExchangeRate, 2, MidpointRounding.AwayFromZero);
        return decimal.Round(foreignTotal, 2, MidpointRounding.AwayFromZero);
    }

    /// <summary>GBP outstanding = book GBP value minus GBP <see cref="Receipt2.AmountPaid"/>.</summary>
    public static decimal Balance(Receipt2 receipt)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        return decimal.Round(BookValueGbp(receipt) - receipt.AmountPaid, 2, MidpointRounding.AwayFromZero);
    }

    public static string FormatReceiptLabel(int receiptNumber)
        => $"KF{receiptNumber:D5}";

    public static Payment CreatePayment(
        int receiptNumber,
        DateTime paymentDate,
        decimal amount,
        int paymentMethodId,
        int bankAccountId,
        string? note,
        bool allowNonPositive = false)
    {
        if (receiptNumber <= 0)
            throw new ArgumentException("receiptNumber must be the purchase receipt number (greater than 0).");
        if (!allowNonPositive && amount <= 0)
            throw new ArgumentException("amount must be greater than 0.");
        if (allowNonPositive && amount == 0)
            throw new ArgumentException("amount must be non-zero.");
        if (paymentMethodId <= 0)
            throw new ArgumentException("paymentMethodId must be a methodId from get_receipt_payment_methods.");
        if (bankAccountId <= 0)
            throw new ArgumentException("bankAccountId must be an accountId from get_bank_accounts.");

        return new Payment
        {
            PayInvoice = receiptNumber,
            PayDate = paymentDate,
            PayAmount = amount,
            PayMethod = paymentMethodId,
            PayAccount = bankAccountId,
            PayNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
        };
    }

    public static int ResolveOtherMethodId(IReadOnlyList<PaymentMethod>? methods)
        => ReceiptPaymentMapper.ResolvePaymentMethodId(null, "Other", methods);

    public static int ResolveNominalIdByCode(int code, IReadOnlyList<NominalCode>? nominals)
    {
        if (code <= 0)
            throw new ArgumentException("nominal code must be greater than 0.");

        var matches = (nominals ?? []).Where(item => item.Code == code).ToList();
        if (matches.Count == 1)
            return matches[0].Id;
        if (matches.Count == 0)
            throw new ArgumentException(
                $"No nominal has Code {code}. Call get_nominal_codes.");

        var list = string.Join(", ", matches.Select(item => $"{item.Id}:{item.Name}"));
        throw new ArgumentException(
            $"More than one nominal has Code {code} ({list}).");
    }
}
