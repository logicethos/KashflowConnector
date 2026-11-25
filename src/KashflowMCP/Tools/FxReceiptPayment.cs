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

    public sealed record Plan(
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

        var outstanding = ReceiptOutstanding.Balance(receipt);
        var fluctuation = decimal.Round(outstanding - bankAmountGbp, 2, MidpointRounding.AwayFromZero);
        var companionNet = decimal.Round(-fluctuation, 2, MidpointRounding.AwayFromZero);
        var label = ReceiptOutstanding.FormatReceiptLabel(receipt.ReceiptNumber);

        return new Plan(
            outstanding,
            decimal.Round(bankAmountGbp, 2, MidpointRounding.AwayFromZero),
            fluctuation,
            companionNet,
            companionNet,
            $"Loss/Gain on {label} due to exchange rate fluctuations",
            forexNominalId);
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
        Func<int, CancellationToken, Task> deletePayment,
        Func<int, CancellationToken, Task> deleteReceiptByNumber,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(sourceReceipt);
        ArgumentNullException.ThrowIfNull(insertPayment);
        ArgumentNullException.ThrowIfNull(insertReceipt);
        ArgumentNullException.ThrowIfNull(loadReceiptByNumber);
        ArgumentNullException.ThrowIfNull(deletePayment);
        ArgumentNullException.ThrowIfNull(deleteReceiptByNumber);

        int? bankPaymentId = null;
        int? fluctuationPaymentId = null;
        int? companionReceiptNumber = null;
        int? companionReceiptId = null;
        int? companionPaymentId = null;

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
                fluctuationPaymentId = await insertPayment(
                    ReceiptOutstanding.CreatePayment(
                        receiptNumber,
                        paymentDate,
                        plan.FluctuationAmount,
                        otherMethodId,
                        bankAccountId,
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
                        bankAccountId,
                        FluctuationNote,
                        allowNonPositive: true),
                    cancellationToken);
            }

            return new Result(
                bankPaymentId.Value,
                fluctuationPaymentId,
                companionReceiptNumber,
                companionReceiptId,
                companionPaymentId,
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
