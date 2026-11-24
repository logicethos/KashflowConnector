using KashFlow;

namespace KashflowMCP.Tools;

/// <summary>
/// Maps a purchase-receipt payment onto KashFlow <see cref="Payment"/>.
/// <see cref="Payment.PayAccount"/> is the bank account's <see cref="BankAccount.AccountID"/>,
/// not the nominal <see cref="BankAccount.AccountCode"/> (for example 1206).
/// </summary>
public static class ReceiptPaymentMapper
{
    public static Payment Create(
        int receiptNumber,
        DateTime paymentDate,
        decimal amount,
        int paymentMethodId,
        int bankAccountId,
        string? note)
    {
        if (receiptNumber <= 0)
            throw new ArgumentException("receiptNumber must be the purchase receipt number (greater than 0).");
        if (amount <= 0)
            throw new ArgumentException("amount must be greater than 0.");
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

    public static int ResolveBankAccountId(
        int? bankAccountId,
        int? bankAccountCode,
        IReadOnlyList<BankAccount>? accounts)
    {
        if (bankAccountId is int id && id > 0)
            return id;

        if (bankAccountCode is not int code || code <= 0)
            throw new ArgumentException(
                "Pass bankAccountId (get_bank_accounts accountId) or bankAccountCode (the nominal code, for example 1206).");

        var matches = (accounts ?? [])
            .Where(account => account.AccountCode == code)
            .ToList();
        if (matches.Count == 1)
            return matches[0].AccountID;
        if (matches.Count == 0)
            throw new ArgumentException(
                $"No bank account has accountCode {code}. Call get_bank_accounts and use accountId or a listed accountCode.");

        var names = string.Join(", ", matches.Select(account => $"{account.AccountID}:{account.AccountName}"));
        throw new ArgumentException(
            $"More than one bank account has accountCode {code} ({names}). Pass bankAccountId.");
    }

    public static int ResolvePaymentMethodId(
        int? paymentMethodId,
        string? paymentMethodName,
        IReadOnlyList<PaymentMethod>? methods)
    {
        if (paymentMethodId is int id && id > 0)
            return id;

        if (string.IsNullOrWhiteSpace(paymentMethodName))
            throw new ArgumentException(
                "Pass paymentMethodId or paymentMethodName from get_receipt_payment_methods.");

        var wanted = paymentMethodName.Trim();
        var matches = (methods ?? [])
            .Where(method => string.Equals(method.MethodName, wanted, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (matches.Count == 1)
            return matches[0].MethodID;
        if (matches.Count == 0)
            throw new ArgumentException(
                $"No receipt payment method is named '{wanted}'. Call get_receipt_payment_methods and use methodId.");

        var ids = string.Join(", ", matches.Select(method => method.MethodID));
        throw new ArgumentException(
            $"More than one payment method is named '{wanted}' (methodIds {ids}). Pass paymentMethodId.");
    }
}
