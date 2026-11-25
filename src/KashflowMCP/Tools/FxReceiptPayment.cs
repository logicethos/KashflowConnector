using KashFlow;

namespace KashflowMCP.Tools;

/// <summary>
/// Books a foreign-currency purchase receipt paid from a GBP bank account, matching the
/// Logic Ethos pattern (bank payment + Currency Fluctuation Other line + companion 956 receipt).
/// </summary>
public static class FxReceiptPayment
{
    public const int ForexNominalCode = 956;
    public const string FluctuationNote = "Currency Fluctuation";

    /// <summary>
    /// PayAccount for Other / Currency Fluctuation lines. Official Payment docs only describe a bank
    /// account from GetBankAccounts; SOAP InsertReceiptPayment with a real PayAccount creates bank-feed
    /// rows that UI-booked Other lines do not. PayAccount=0 is the non-bank convention used here so
    /// fluctuation and companion lines do not hit the bank feed.
    /// </summary>
    public const int NonBankPayAccount = 0;

    public sealed record Plan(
        decimal BookValueGbp,
        decimal OutstandingBefore,
        decimal BankAmountGbp,
        decimal FluctuationAmount,
        decimal CompanionNetAmount,
        decimal CompanionPaymentAmount,
        string CompanionDescription,
        int ForexNominalId);

    public sealed record Result(
        int BankPaymentId,
        int? FluctuationPaymentId,
        int? CompanionReceiptNumber,
        int? CompanionReceiptId,
        int? CompanionPaymentId,
        IReadOnlyList<int> DeletedBankTransactionIds,
        IReadOnlyList<string> BankCleanupSkipped,
        bool SourceReceiptMarkedPaid,
        bool? CompanionReceiptMarkedPaid,
        Plan Plan);

    public static Plan BuildPlan(
        Receipt2 receipt,
        decimal bankAmountGbp,
        int forexNominalId)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        if (receipt.ReceiptNumber <= 0)
            throw new ArgumentException("receiptNumber must be greater than 0.");
        if (bankAmountGbp <= 0)
            throw new ArgumentException("bankAmountGbp must be greater than 0.");
        if (forexNominalId <= 0)
            throw new ArgumentException("forexNominalId must be a get_nominal_codes id.");

        // Outstanding is GBP book value (foreign total / ExchangeRate) minus GBP already paid —
        // never the foreign net+VAT figure. Bank and fluctuation lines are also in GBP.
        var bookValueGbp = ReceiptOutstanding.BookValueGbp(receipt);
        var outstanding = ReceiptOutstanding.Balance(receipt);
        var fluctuation = decimal.Round(outstanding - bankAmountGbp, 2, MidpointRounding.AwayFromZero);
        var companionNet = decimal.Round(-fluctuation, 2, MidpointRounding.AwayFromZero);
        var label = ReceiptOutstanding.FormatReceiptLabel(receipt.ReceiptNumber);
        var description = CompanionDescription(label, companionNet);

        return new Plan(
            bookValueGbp,
            outstanding,
            decimal.Round(bankAmountGbp, 2, MidpointRounding.AwayFromZero),
            fluctuation,
            companionNet,
            companionNet,
            description,
            forexNominalId);
    }

    /// <summary>
    /// Precedent wording: "Loss on KF0xxxx due to exchange rate fluctuations" when the bank paid more
    /// than book (positive companion net), "Gain on …" when the bank paid less (negative companion net).
    /// </summary>
    public static string CompanionDescription(string receiptLabel, decimal companionNetAmount)
    {
        if (string.IsNullOrWhiteSpace(receiptLabel))
            throw new ArgumentException("receiptLabel is required.");
        var kind = companionNetAmount >= 0 ? "Loss" : "Gain";
        return $"{kind} on {receiptLabel.Trim()} due to exchange rate fluctuations";
    }

    public static Receipt2 CreateCompanionReceipt(
        Receipt2 source,
        Plan plan,
        DateTime paymentDate)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(plan);
        if (plan.CompanionNetAmount == 0)
            throw new ArgumentException("Companion receipt is not required when the fluctuation is zero.");

        return new Receipt2
        {
            Supplier = source.Supplier,
            SupplierId = source.SupplierId,
            ReceiptDate = paymentDate,
            DueDate = paymentDate,
            CurrencyCode = "GBP",
            ExchangeRate = 1,
            NetAmount = plan.CompanionNetAmount,
            VATAmount = 0,
            Lines =
            [
                new ReceiptLine2
                {
                    Quantity = 1,
                    Description = plan.CompanionDescription,
                    Rate = plan.CompanionNetAmount,
                    ChargeType = plan.ForexNominalId,
                    VatRate = 0,
                    VatAmount = 0,
                },
            ],
        };
    }

    /// <summary>Sets <see cref="Receipt2.Paid"/> to 1 on a loaded receipt (for UpdateReceipt2).</summary>
    public static Receipt2 MarkPaid(Receipt2 receipt)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        receipt.Paid = 1;
        return receipt;
    }

    public static async Task<Result> PostAsync(
        Plan plan,
        int receiptNumber,
        DateTime paymentDate,
        int bankMethodId,
        int otherMethodId,
        int bankAccountId,
        string? note,
        Receipt2 sourceReceipt,
        Func<Payment, CancellationToken, Task<int>> insertPayment,
        Func<Receipt2, CancellationToken, Task<int>> insertReceipt,
        Func<int, CancellationToken, Task<Receipt2?>> loadReceiptByNumber,
        Func<Receipt2, CancellationToken, Task> updateReceipt,
        Func<int, CancellationToken, Task<IReadOnlyList<BankTransaction>>> getBankTransactions,
        Func<int, CancellationToken, Task> deleteBankTransaction,
        Func<int, CancellationToken, Task> deletePayment,
        Func<int, CancellationToken, Task> deleteReceiptByNumber,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(sourceReceipt);
        ArgumentNullException.ThrowIfNull(insertPayment);
        ArgumentNullException.ThrowIfNull(insertReceipt);
        ArgumentNullException.ThrowIfNull(loadReceiptByNumber);
        ArgumentNullException.ThrowIfNull(updateReceipt);
        ArgumentNullException.ThrowIfNull(getBankTransactions);
        ArgumentNullException.ThrowIfNull(deleteBankTransaction);
        ArgumentNullException.ThrowIfNull(deletePayment);
        ArgumentNullException.ThrowIfNull(deleteReceiptByNumber);

        int? bankPaymentId = null;
        int? fluctuationPaymentId = null;
        int? companionReceiptNumber = null;
        int? companionReceiptId = null;
        int? companionPaymentId = null;
        IReadOnlyList<int> deletedBankTxIds = [];
        IReadOnlyList<string> bankCleanupSkipped = [];
        var sourceMarkedPaid = false;
        bool? companionMarkedPaid = null;

        try
        {
            bankPaymentId = await insertPayment(
                ReceiptOutstanding.CreatePayment(
                    receiptNumber,
                    paymentDate,
                    plan.BankAmountGbp,
                    bankMethodId,
                    bankAccountId,
                    note),
                cancellationToken);

            if (plan.FluctuationAmount != 0)
            {
                // Snapshot bank-feed ids before Other inserts so cleanup only touches new rows.
                var before = await getBankTransactions(bankAccountId, cancellationToken);
                var beforeIds = before.Select(tx => tx.ID).ToHashSet();

                fluctuationPaymentId = await insertPayment(
                    ReceiptOutstanding.CreatePayment(
                        receiptNumber,
                        paymentDate,
                        plan.FluctuationAmount,
                        otherMethodId,
                        NonBankPayAccount,
                        FluctuationNote,
                        allowNonPositive: true),
                    cancellationToken);

                var companion = CreateCompanionReceipt(sourceReceipt, plan, paymentDate);
                companionReceiptNumber = await insertReceipt(companion, cancellationToken);
                var loaded = await loadReceiptByNumber(companionReceiptNumber.Value, cancellationToken);
                companionReceiptId = loaded?.ReceiptDBId is int dbid && dbid > 0
                    ? dbid
                    : companionReceiptNumber;

                companionPaymentId = await insertPayment(
                    ReceiptOutstanding.CreatePayment(
                        companionReceiptNumber.Value,
                        paymentDate,
                        plan.CompanionPaymentAmount,
                        otherMethodId,
                        NonBankPayAccount,
                        FluctuationNote,
                        allowNonPositive: true),
                    cancellationToken);

                // Fallback: if SOAP still created Currency Fluctuation bank lines (e.g. PayAccount=0
                // was remapped to a default bank), delete only safe single-sided new rows.
                var after = await getBankTransactions(bankAccountId, cancellationToken);
                var newIds = after.Select(tx => tx.ID).Where(id => !beforeIds.Contains(id)).ToHashSet();
                var candidates = FxBankFeedCleanup.FindCandidates(
                    after,
                    paymentDate,
                    plan.FluctuationAmount,
                    FluctuationNote,
                    newIds);
                // Also consider rows that match criteria even if id was already present (shared-id case
                // is refused inside FindCandidates when both moneyIn and moneyOut are set).
                if (candidates.Count == 0)
                {
                    candidates = FxBankFeedCleanup.FindCandidates(
                        after,
                        paymentDate,
                        plan.FluctuationAmount,
                        FluctuationNote,
                        onlyNewIds: null);
                }

                var cleanup = await FxBankFeedCleanup.DeleteSafeCandidatesAsync(
                    candidates,
                    deleteBankTransaction,
                    cancellationToken);
                deletedBankTxIds = cleanup.DeletedTransactionIds;
                bankCleanupSkipped = cleanup.Skipped;

                if (loaded is not null)
                {
                    await updateReceipt(MarkPaid(loaded), cancellationToken);
                    companionMarkedPaid = true;
                }
                else
                {
                    companionMarkedPaid = false;
                }
            }

            var sourceAfter = await loadReceiptByNumber(receiptNumber, cancellationToken)
                ?? throw new InvalidOperationException($"Purchase receipt {receiptNumber} was not found after payment.");
            await updateReceipt(MarkPaid(sourceAfter), cancellationToken);
            sourceMarkedPaid = true;

            return new Result(
                bankPaymentId.Value,
                fluctuationPaymentId,
                companionReceiptNumber,
                companionReceiptId,
                companionPaymentId,
                deletedBankTxIds,
                bankCleanupSkipped,
                sourceMarkedPaid,
                companionMarkedPaid,
                plan);
        }
        catch (Exception failure)
        {
            var rollbackErrors = new List<Exception>();
            await TryDelete(companionPaymentId, deletePayment, rollbackErrors, cancellationToken);
            if (companionReceiptNumber is int number)
                await TryDelete(number, deleteReceiptByNumber, rollbackErrors, cancellationToken);
            await TryDelete(fluctuationPaymentId, deletePayment, rollbackErrors, cancellationToken);
            await TryDelete(bankPaymentId, deletePayment, rollbackErrors, cancellationToken);

            var created = new List<string>();
            if (bankPaymentId is int bp)
                created.Add($"bankPaymentId={bp}");
            if (fluctuationPaymentId is int fp)
                created.Add($"fluctuationPaymentId={fp}");
            if (companionReceiptNumber is int cr)
                created.Add($"companionReceiptNumber={cr}");
            if (companionPaymentId is int cp)
                created.Add($"companionPaymentId={cp}");

            var detail = created.Count == 0
                ? "No KashFlow rows were left behind."
                : rollbackErrors.Count == 0
                    ? $"Rolled back created rows ({string.Join(", ", created)})."
                    : $"Partial rollback for ({string.Join(", ", created)}). Undo remaining rows with delete_receipt_payment / delete_receipt.";

            throw new InvalidOperationException(
                $"insert_fx_receipt_payment failed. {detail}",
                rollbackErrors.Count == 0 ? failure : new AggregateException(new[] { failure }.Concat(rollbackErrors)));
        }
    }

    private static async Task TryDelete(
        int? id,
        Func<int, CancellationToken, Task> delete,
        List<Exception> errors,
        CancellationToken cancellationToken)
    {
        if (id is not int value)
            return;
        try
        {
            await delete(value, cancellationToken);
        }
        catch (Exception ex)
        {
            errors.Add(ex);
        }
    }
}
