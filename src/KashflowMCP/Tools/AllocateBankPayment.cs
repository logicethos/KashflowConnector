using KashFlow;

namespace KashflowMCP.Tools;

/// <summary>
/// Splits one bank amount across several purchase receipts (add-only InsertReceiptPayment calls).
/// Each insert creates a separate bank-feed line (positive PaidOut, negative PaidIn for a credit note).
/// SOAP cannot mirror UI batch payment (one bank line for many receipts).
/// </summary>
public static class AllocateBankPayment
{
    public sealed record Allocation(int ReceiptNumber, decimal Amount);

    public sealed record PlannedPayment(
        int ReceiptNumber,
        decimal Amount,
        decimal OutstandingBefore);

    public sealed record PostedPayment(
        int ReceiptNumber,
        decimal Amount,
        int ReceiptPaymentId);

    public static IReadOnlyList<PlannedPayment> Validate(
        decimal totalAmount,
        IReadOnlyList<Allocation> allocations,
        IReadOnlyDictionary<int, Receipt2> receiptsByNumber)
    {
        ArgumentNullException.ThrowIfNull(allocations);
        ArgumentNullException.ThrowIfNull(receiptsByNumber);

        if (totalAmount == 0)
            throw new ArgumentException(
                "totalAmount must be non-zero. A negative total is a PaidIn (money in) bank line (purchase credit notes).");
        if (allocations.Count == 0)
            throw new ArgumentException("allocations must contain at least one receipt.");

        var seen = new HashSet<int>();
        var planned = new List<PlannedPayment>(allocations.Count);
        decimal sum = 0;

        foreach (var allocation in allocations)
        {
            if (allocation.ReceiptNumber <= 0)
                throw new ArgumentException("allocations[].receiptNumber must be greater than 0.");
            if (allocation.Amount == 0)
                throw new ArgumentException(
                    $"Allocation for receipt {allocation.ReceiptNumber} must be non-zero. Use a negative amount for a purchase credit note (PaidIn / money in).");
            if (!seen.Add(allocation.ReceiptNumber))
                throw new ArgumentException(
                    $"Duplicate receipt number {allocation.ReceiptNumber} in allocations.");

            if (!receiptsByNumber.TryGetValue(allocation.ReceiptNumber, out var receipt))
                throw new ArgumentException(
                    $"Purchase receipt {allocation.ReceiptNumber} was not found.");

            var outstanding = ReceiptOutstanding.Balance(receipt);
            EnsureWithinOutstanding(allocation.ReceiptNumber, allocation.Amount, outstanding);

            sum += allocation.Amount;
            planned.Add(new PlannedPayment(allocation.ReceiptNumber, allocation.Amount, outstanding));
        }

        sum = decimal.Round(sum, 2, MidpointRounding.AwayFromZero);
        var expected = decimal.Round(totalAmount, 2, MidpointRounding.AwayFromZero);
        if (sum != expected)
            throw new ArgumentException(
                $"allocations sum to {sum}, which must equal totalAmount {expected} to the penny.");

        return planned;
    }

    private static void EnsureWithinOutstanding(int receiptNumber, decimal amount, decimal outstanding)
    {
        if (outstanding == 0)
            throw new ArgumentException($"Receipt {receiptNumber} has no outstanding balance.");

        var sameDirection = (amount > 0 && outstanding > 0) || (amount < 0 && outstanding < 0);
        if (!sameDirection)
            throw new ArgumentException(
                $"Allocation {amount} for receipt {receiptNumber} must have the same sign as outstanding balance {outstanding}. Purchase credit notes use a negative amount (PaidIn / money in).");
        if (Math.Abs(amount) > Math.Abs(outstanding))
            throw new ArgumentException(
                $"Allocation {amount} for receipt {receiptNumber} exceeds outstanding balance {outstanding}.");
    }

    public static async Task<(IReadOnlyList<PostedPayment> Succeeded, Exception? Failure)> PostAsync(
        IReadOnlyList<PlannedPayment> planned,
        DateTime paymentDate,
        int paymentMethodId,
        int bankAccountId,
        string? note,
        Func<Payment, CancellationToken, Task<int>> insert,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(planned);
        ArgumentNullException.ThrowIfNull(insert);

        var succeeded = new List<PostedPayment>();
        foreach (var item in planned)
        {
            try
            {
                var payment = ReceiptOutstanding.CreatePayment(
                    item.ReceiptNumber,
                    paymentDate,
                    item.Amount,
                    paymentMethodId,
                    bankAccountId,
                    note);
                var id = await insert(payment, cancellationToken);
                succeeded.Add(new PostedPayment(item.ReceiptNumber, item.Amount, id));
            }
            catch (Exception ex)
            {
                return (succeeded, ex);
            }
        }

        return (succeeded, null);
    }
}
