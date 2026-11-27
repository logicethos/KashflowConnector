using System.Globalization;

namespace KashflowMCP.Tools;

/// <summary>
/// Bank-feed direction of a purchase-receipt payment.
/// KashFlow records a positive <c>PayAmount</c> as PaidOut (money out) and a negative
/// <c>PayAmount</c> as PaidIn (money in). Purchase credit notes use the negative form
/// (SOAP precedent: receipt KF04026, payID 146449956, PayAmount -11.98).
/// </summary>
public static class ReceiptPaymentBankLine
{
    public const string PaidIn = "PaidIn";
    public const string PaidOut = "PaidOut";

    public const string NonZeroAmountMessage =
        "amount must be non-zero. Use a negative amount for a purchase credit note (PaidIn / money in bank line).";

    public sealed record Preview(decimal Amount, string BankLine, string MoneyDirection, string BankLineNote);

    public static void EnsureNonZero(decimal amount)
    {
        if (amount == 0)
            throw new ArgumentException(NonZeroAmountMessage);
    }

    public static Preview Describe(decimal amount)
    {
        EnsureNonZero(amount);
        var paidIn = amount < 0;
        var formatted = amount.ToString(CultureInfo.InvariantCulture);
        return new Preview(
            amount,
            paidIn ? PaidIn : PaidOut,
            paidIn ? "in" : "out",
            paidIn
                ? $"Amount {formatted} is negative, so KashFlow records a PaidIn (money in) bank line. Purchase credit notes use a negative amount."
                : $"Amount {formatted} is positive, so KashFlow records a PaidOut (money out) bank line.");
    }

    /// <summary>
    /// One bank line for a bulk purchase payment. The line follows the signed total:
    /// positive is PaidOut, negative is PaidIn. A negative allocation is a credit note
    /// inside that total; it does not create a second bank line.
    /// </summary>
    public static Preview DescribeBatch(decimal totalAmount)
    {
        var effect = Describe(totalAmount);
        var formatted = totalAmount.ToString(CultureInfo.InvariantCulture);
        return effect with
        {
            BankLineNote =
                $"The batch creates one bank line for {formatted}. {effect.BankLineNote} A negative allocation is a purchase credit note inside that total.",
        };
    }
}
