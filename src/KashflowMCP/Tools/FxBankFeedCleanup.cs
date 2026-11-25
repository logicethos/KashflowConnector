using KashFlow;

namespace KashflowMCP.Tools;

/// <summary>
/// Finds and removes bank-feed lines created by SOAP <c>InsertReceiptPayment</c> with method Other
/// and note <see cref="FxReceiptPayment.FluctuationNote"/>. UI-booked Other lines do not appear on
/// GetBankTransactions; SOAP with a real PayAccount does — see README.
/// </summary>
public static class FxBankFeedCleanup
{
    public sealed record Candidate(
        int TransactionId,
        DateTime? TransactionDate,
        decimal MoneyIn,
        decimal MoneyOut,
        string? Comment,
        bool SafeToDelete,
        string Reason);

    public sealed record Result(
        IReadOnlyList<Candidate> Candidates,
        IReadOnlyList<int> DeletedTransactionIds,
        IReadOnlyList<string> Skipped);

    /// <summary>
    /// A row is a cleanup candidate when date, comment, and absolute money-in or money-out match the
    /// fluctuation amount. Rows that carry both money-in and money-out are never deleted (shared /
    /// polluted bank rows — deleting the id would remove the real bank payment too).
    /// </summary>
    public static IReadOnlyList<Candidate> FindCandidates(
        IEnumerable<BankTransaction> transactions,
        DateTime paymentDate,
        decimal fluctuationAmount,
        string comment,
        IReadOnlySet<int>? onlyNewIds = null)
    {
        ArgumentNullException.ThrowIfNull(transactions);
        if (string.IsNullOrWhiteSpace(comment))
            throw new ArgumentException("comment is required.");

        var absAmount = Math.Abs(decimal.Round(fluctuationAmount, 2, MidpointRounding.AwayFromZero));
        if (absAmount == 0)
            return [];

        var wantedComment = comment.Trim();
        var day = paymentDate.Date;
        var list = new List<Candidate>();

        foreach (var tx in transactions)
        {
            if (onlyNewIds is not null && !onlyNewIds.Contains(tx.ID))
                continue;
            if (tx.TransactionDate is not DateTime when || when.Date != day)
                continue;
            if (!string.Equals(tx.Comment?.Trim(), wantedComment, StringComparison.Ordinal))
                continue;

            var inAbs = Math.Abs(tx.Moneyin);
            var outAbs = Math.Abs(tx.Moneyout);
            var amountMatches = inAbs == absAmount || outAbs == absAmount;
            if (!amountMatches)
                continue;

            // Dual-amount row: same BankTransaction.ID matched both the bank payment and the
            // fluctuation in search (live KF04195 observation). Never delete by that id.
            if (tx.Moneyin != 0 && tx.Moneyout != 0)
            {
                list.Add(new Candidate(
                    tx.ID,
                    tx.TransactionDate,
                    tx.Moneyin,
                    tx.Moneyout,
                    tx.Comment,
                    SafeToDelete: false,
                    Reason: "Row has both moneyIn and moneyOut; refusing delete so the bank payment is not removed."));
                continue;
            }

            list.Add(new Candidate(
                tx.ID,
                tx.TransactionDate,
                tx.Moneyin,
                tx.Moneyout,
                tx.Comment,
                SafeToDelete: true,
                Reason: "Matches Currency Fluctuation note, payment date, and absolute amount; single-sided row."));
        }

        return list;
    }

    public static async Task<Result> DeleteSafeCandidatesAsync(
        IReadOnlyList<Candidate> candidates,
        Func<int, CancellationToken, Task> deleteBankTransaction,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(deleteBankTransaction);

        var deleted = new List<int>();
        var skipped = new List<string>();
        foreach (var candidate in candidates)
        {
            if (!candidate.SafeToDelete)
            {
                skipped.Add($"transactionId={candidate.TransactionId}: {candidate.Reason}");
                continue;
            }

            await deleteBankTransaction(candidate.TransactionId, cancellationToken);
            deleted.Add(candidate.TransactionId);
        }

        return new Result(candidates, deleted, skipped);
    }
}
