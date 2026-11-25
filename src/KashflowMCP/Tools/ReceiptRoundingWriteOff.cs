using KashFlow;

namespace KashflowMCP.Tools;

/// <summary>
/// Posts a small balancing receipt payment so a near-zero outstanding balance shows as paid.
/// The balancing amount is attributed through a bank account whose <see cref="BankAccount.AccountCode"/>
/// matches the given nominal code (payment method Other).
/// </summary>
public static class ReceiptRoundingWriteOff
{
    public const decimal DefaultMaxAmount = 0.05m;

    public sealed record Plan(
        decimal Outstanding,
        decimal PaymentAmount,
        int NominalCode,
        int BankAccountId,
        int OtherMethodId);

    public static Plan BuildPlan(
        Receipt2 receipt,
        int nominalCode,
        decimal maxAmount,
        IReadOnlyList<BankAccount>? accounts,
        IReadOnlyList<PaymentMethod>? methods)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        if (receipt.ReceiptNumber <= 0)
            throw new ArgumentException("receiptNumber must be greater than 0.");
        if (nominalCode <= 0)
            throw new ArgumentException("nominal must be a nominal / bank account code greater than 0.");
        if (maxAmount < 0)
            throw new ArgumentException("maxAmount must be 0 or greater.");

        var outstanding = ReceiptOutstanding.Balance(receipt);
        if (outstanding == 0)
            throw new ArgumentException(
                $"Receipt {receipt.ReceiptNumber} already has a zero outstanding balance.");
        if (Math.Abs(outstanding) > maxAmount)
            throw new ArgumentException(
                $"Outstanding balance {outstanding} for receipt {receipt.ReceiptNumber} exceeds maxAmount {maxAmount}.");

        var bankAccountId = ReceiptPaymentMapper.ResolveBankAccountId(null, nominalCode, accounts);
        var otherMethodId = ReceiptOutstanding.ResolveOtherMethodId(methods);

        return new Plan(
            outstanding,
            outstanding,
            nominalCode,
            bankAccountId,
            otherMethodId);
    }
}
