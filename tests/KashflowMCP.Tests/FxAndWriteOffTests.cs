using KashFlow;
using KashflowMCP.Tools;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace KashflowMCP.Tests;

public class FxAndWriteOffTests
{
    private static Receipt2 SourceReceipt()
        => new()
        {
            ReceiptNumber = 4151,
            Supplier = "ACME",
            SupplierId = 22,
            NetAmount = 100m,
            VATAmount = 0m,
            AmountPaid = 0m,
            CurrencyCode = "USD",
            // ExchangeRate unset (0): book value falls back to net+VAT for non-FX fixtures.
        };

    /// <summary>Hetzner KF04189: €21.49 at 1.1941 → book £18.00.</summary>
    private static Receipt2 HetznerReceipt()
        => new()
        {
            ReceiptNumber = 4189,
            Supplier = "Hetzner",
            SupplierId = 30,
            NetAmount = 21.49m,
            VATAmount = 0m,
            AmountPaid = 0m,
            CurrencyCode = "EUR",
            ExchangeRate = 1.1941m,
        };

    /// <summary>Cursor KF04195: $192 at 1.3123 → book £146.31.</summary>
    private static Receipt2 CursorReceipt()
        => new()
        {
            ReceiptNumber = 4195,
            Supplier = "Cursor",
            SupplierId = 31,
            NetAmount = 192m,
            VATAmount = 0m,
            AmountPaid = 0m,
            CurrencyCode = "USD",
            ExchangeRate = 1.3123m,
        };

    /// <summary>Precedent KF04151: €21.49 at 1.1686 → book £18.39.</summary>
    private static Receipt2 PrecedentReceipt()
        => new()
        {
            ReceiptNumber = 4151,
            Supplier = "Precedent",
            SupplierId = 32,
            NetAmount = 21.49m,
            VATAmount = 0m,
            AmountPaid = 0m,
            CurrencyCode = "EUR",
            ExchangeRate = 1.1686m,
        };

    [Theory]
    [InlineData(21.49, 0, 1.1941, 18.00)]   // Hetzner
    [InlineData(192, 0, 1.3123, 146.31)]     // Cursor
    [InlineData(21.49, 0, 1.1686, 18.39)]    // Precedent
    public void Book_value_gbp_is_foreign_total_divided_by_exchange_rate(
        decimal net,
        decimal vat,
        decimal rate,
        decimal expectedBook)
    {
        var receipt = new Receipt2
        {
            NetAmount = net,
            VATAmount = vat,
            ExchangeRate = rate,
            CurrencyCode = "EUR",
        };
        Assert.Equal(expectedBook, ReceiptOutstanding.BookValueGbp(receipt));
        Assert.Equal(expectedBook, ReceiptOutstanding.Balance(receipt));
    }

    [Fact]
    public void Hetzner_KF04189_bank_18_41_yields_fluctuation_minus_0_41_and_companion_loss()
    {
        var plan = FxReceiptPayment.BuildPlan(HetznerReceipt(), bankAmountGbp: 18.41m, forexNominalId: 956);
        Assert.Equal(18.00m, plan.BookValueGbp);
        Assert.Equal(18.00m, plan.OutstandingBefore);
        Assert.Equal(18.41m, plan.BankAmountGbp);
        Assert.Equal(-0.41m, plan.FluctuationAmount);
        Assert.Equal(0.41m, plan.CompanionNetAmount);
        Assert.Equal(0.41m, plan.CompanionPaymentAmount);
        Assert.Equal("Loss on KF04189 due to exchange rate fluctuations", plan.CompanionDescription);
    }

    [Fact]
    public void Cursor_KF04195_bank_148_11_yields_fluctuation_minus_1_80_and_companion_loss()
    {
        var plan = FxReceiptPayment.BuildPlan(CursorReceipt(), bankAmountGbp: 148.11m, forexNominalId: 956);
        Assert.Equal(146.31m, plan.BookValueGbp);
        Assert.Equal(146.31m, plan.OutstandingBefore);
        Assert.Equal(148.11m, plan.BankAmountGbp);
        Assert.Equal(-1.80m, plan.FluctuationAmount);
        Assert.Equal(1.80m, plan.CompanionNetAmount);
        Assert.Equal(1.80m, plan.CompanionPaymentAmount);
        Assert.Equal("Loss on KF04195 due to exchange rate fluctuations", plan.CompanionDescription);
    }

    [Fact]
    public void Precedent_KF04151_bank_18_61_yields_fluctuation_minus_0_22_and_companion_loss()
    {
        var plan = FxReceiptPayment.BuildPlan(PrecedentReceipt(), bankAmountGbp: 18.61m, forexNominalId: 956);
        Assert.Equal(18.39m, plan.BookValueGbp);
        Assert.Equal(18.39m, plan.OutstandingBefore);
        Assert.Equal(18.61m, plan.BankAmountGbp);
        Assert.Equal(-0.22m, plan.FluctuationAmount);
        Assert.Equal(0.22m, plan.CompanionNetAmount);
        Assert.Equal(0.22m, plan.CompanionPaymentAmount);
        Assert.Equal("Loss on KF04151 due to exchange rate fluctuations", plan.CompanionDescription);
    }

    [Fact]
    public void Balance_subtracts_gbp_amount_paid_from_book_value()
    {
        var receipt = HetznerReceipt();
        receipt.AmountPaid = 10m;
        Assert.Equal(18.00m, ReceiptOutstanding.BookValueGbp(receipt));
        Assert.Equal(8.00m, ReceiptOutstanding.Balance(receipt));
    }

    [Fact]
    public void Fx_plan_uses_outstanding_minus_bank_amount_and_companion_loss_or_gain_wording()
    {
        var plan = FxReceiptPayment.BuildPlan(SourceReceipt(), bankAmountGbp: 95m, forexNominalId: 999);
        Assert.Equal(100m, plan.BookValueGbp);
        Assert.Equal(100m, plan.OutstandingBefore);
        Assert.Equal(5m, plan.FluctuationAmount);
        Assert.Equal(-5m, plan.CompanionNetAmount);
        Assert.Equal(-5m, plan.CompanionPaymentAmount);
        Assert.Equal("Gain on KF04151 due to exchange rate fluctuations", plan.CompanionDescription);

        var loss = FxReceiptPayment.BuildPlan(SourceReceipt(), bankAmountGbp: 105m, forexNominalId: 999);
        Assert.Equal(-5m, loss.FluctuationAmount);
        Assert.Equal(5m, loss.CompanionNetAmount);
        Assert.Equal(5m, loss.CompanionPaymentAmount);
        Assert.Equal("Loss on KF04151 due to exchange rate fluctuations", loss.CompanionDescription);
    }

    [Fact]
    public void Other_payments_use_non_bank_pay_account_zero()
    {
        var payment = ReceiptOutstanding.CreatePayment(
            4195,
            new DateTime(2026, 5, 1),
            -1.80m,
            paymentMethodId: 2,
            bankAccountId: FxReceiptPayment.NonBankPayAccount,
            FxReceiptPayment.FluctuationNote,
            allowNonPositive: true);
        Assert.Equal(0, payment.PayAccount);
        Assert.Equal(-1.80m, payment.PayAmount);
        Assert.Equal(FxReceiptPayment.FluctuationNote, payment.PayNote);
    }

    [Fact]
    public void Mark_paid_sets_receipt_paid_flag_to_one()
    {
        var receipt = CursorReceipt();
        Assert.Equal(0, receipt.Paid);
        FxReceiptPayment.MarkPaid(receipt);
        Assert.Equal(1, receipt.Paid);
    }

    [Fact]
    public async Task Fx_post_uses_pay_account_zero_for_other_marks_paid_and_cleans_safe_bank_rows()
    {
        var plan = FxReceiptPayment.BuildPlan(CursorReceipt(), 148.11m, 999);
        var payments = new List<Payment>();
        var updated = new List<Receipt2>();
        var deletedBankTx = new List<int>();
        var bankTxs = new List<BankTransaction>
        {
            new()
            {
                ID = 154508988,
                TransactionDate = new DateTime(2026, 5, 1),
                Moneyin = 0m,
                Moneyout = 148.11m,
                Comment = "bank",
            },
        };

        var result = await FxReceiptPayment.PostAsync(
            plan,
            4195,
            new DateTime(2026, 5, 1),
            bankMethodId: 1,
            otherMethodId: 2,
            bankAccountId: 767325,
            note: null,
            CursorReceipt(),
            (payment, _) =>
            {
                payments.Add(payment);
                return Task.FromResult(500 + payments.Count);
            },
            (_, _) => Task.FromResult(4235),
            (number, _) => Task.FromResult<Receipt2?>(new Receipt2
            {
                ReceiptNumber = number,
                ReceiptDBId = number == 4195 ? 900 : 901,
                Paid = 0,
                NetAmount = number == 4195 ? 192m : 1.80m,
                Lines = [],
            }),
            (receipt, _) =>
            {
                updated.Add(receipt);
                return Task.CompletedTask;
            },
            (_, _) =>
            {
                // After Other inserts, a stray single-sided Currency Fluctuation line appears.
                if (payments.Count >= 2 && bankTxs.All(tx => tx.ID != 99))
                {
                    bankTxs.Add(new BankTransaction
                    {
                        ID = 99,
                        TransactionDate = new DateTime(2026, 5, 1),
                        Moneyin = 1.80m,
                        Moneyout = 0m,
                        Comment = FxReceiptPayment.FluctuationNote,
                    });
                }

                return Task.FromResult<IReadOnlyList<BankTransaction>>(bankTxs.ToList());
            },
            (txId, _) =>
            {
                deletedBankTx.Add(txId);
                bankTxs.RemoveAll(tx => tx.ID == txId);
                return Task.CompletedTask;
            },
            (_, _) => Task.CompletedTask,
            (_, _) => Task.CompletedTask,
            CancellationToken.None);

        Assert.Equal(3, payments.Count);
        Assert.Equal(767325, payments[0].PayAccount);
        Assert.Equal(FxReceiptPayment.NonBankPayAccount, payments[1].PayAccount);
        Assert.Equal(FxReceiptPayment.NonBankPayAccount, payments[2].PayAccount);
        Assert.Equal(FxReceiptPayment.FluctuationNote, payments[1].PayNote);
        Assert.Equal([99], result.DeletedBankTransactionIds.ToArray());
        Assert.Contains(99, deletedBankTx);
        Assert.True(result.SourceReceiptMarkedPaid);
        Assert.True(result.CompanionReceiptMarkedPaid);
        Assert.Equal(2, updated.Count);
        Assert.All(updated, receipt => Assert.Equal(1, receipt.Paid));
    }

    [Fact]
    public void Bank_feed_cleanup_refuses_dual_amount_shared_id_rows()
    {
        var txs = new[]
        {
            new BankTransaction
            {
                ID = 154508988,
                TransactionDate = new DateTime(2026, 5, 1),
                Moneyin = 1.80m,
                Moneyout = 148.11m,
                Comment = FxReceiptPayment.FluctuationNote,
            },
            new BankTransaction
            {
                ID = 100,
                TransactionDate = new DateTime(2026, 5, 1),
                Moneyin = 1.80m,
                Moneyout = 0m,
                Comment = FxReceiptPayment.FluctuationNote,
            },
        };

        var candidates = FxBankFeedCleanup.FindCandidates(
            txs,
            new DateTime(2026, 5, 1),
            -1.80m,
            FxReceiptPayment.FluctuationNote);

        Assert.Equal(2, candidates.Count);
        Assert.False(candidates.Single(c => c.TransactionId == 154508988).SafeToDelete);
        Assert.True(candidates.Single(c => c.TransactionId == 100).SafeToDelete);
    }

    [Fact]
    public async Task Fx_post_rolls_back_created_rows_when_a_later_step_fails()
    {
        var plan = FxReceiptPayment.BuildPlan(SourceReceipt(), 95m, 999);
        var deletedPayments = new List<int>();
        var deletedReceipts = new List<int>();
        var inserts = 0;

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => FxReceiptPayment.PostAsync(
            plan,
            4151,
            new DateTime(2026, 5, 1),
            bankMethodId: 1,
            otherMethodId: 2,
            bankAccountId: 4,
            note: null,
            SourceReceipt(),
            (_, _) =>
            {
                inserts++;
                return Task.FromResult(inserts switch
                {
                    1 => 501,
                    2 => 502,
                    _ => throw new InvalidOperationException("companion payment failed"),
                });
            },
            (_, _) => Task.FromResult(4165),
            (number, _) => Task.FromResult<Receipt2?>(new Receipt2 { ReceiptNumber = number, ReceiptDBId = 900 }),
            (_, _) => Task.CompletedTask,
            (_, _) => Task.FromResult<IReadOnlyList<BankTransaction>>([]),
            (_, _) => Task.CompletedTask,
            (id, _) =>
            {
                deletedPayments.Add(id);
                return Task.CompletedTask;
            },
            (number, _) =>
            {
                deletedReceipts.Add(number);
                return Task.CompletedTask;
            },
            CancellationToken.None));

        Assert.Contains("Rolled back", error.Message, StringComparison.Ordinal);
        Assert.Contains(501, deletedPayments);
        Assert.Contains(502, deletedPayments);
        Assert.Contains(4165, deletedReceipts);
    }

    [Fact]
    public void Write_off_rejects_zero_and_over_max_and_accepts_a_small_balance()
    {
        var accounts = new List<BankAccount>
        {
            new() { AccountID = 3, AccountCode = 790, AccountName = "Rounding" },
        };
        var methods = new List<PaymentMethod>
        {
            new() { MethodID = 9, MethodName = "Other" },
        };

        Assert.Throws<ArgumentException>(() => ReceiptRoundingWriteOff.BuildPlan(
            new Receipt2 { ReceiptNumber = 1, NetAmount = 10m, AmountPaid = 10m },
            790,
            0.05m,
            accounts,
            methods));

        Assert.Throws<ArgumentException>(() => ReceiptRoundingWriteOff.BuildPlan(
            new Receipt2 { ReceiptNumber = 1, NetAmount = 10m, AmountPaid = 9.90m },
            790,
            0.05m,
            accounts,
            methods));

        var plan = ReceiptRoundingWriteOff.BuildPlan(
            new Receipt2 { ReceiptNumber = 1, NetAmount = 10m, AmountPaid = 9.97m },
            790,
            0.05m,
            accounts,
            methods);
        Assert.Equal(0.03m, plan.PaymentAmount);
        Assert.Equal(3, plan.BankAccountId);
        Assert.Equal(9, plan.OtherMethodId);
    }

    [Fact]
    public void Write_off_uses_gbp_outstanding_for_foreign_currency_receipt()
    {
        var accounts = new List<BankAccount>
        {
            new() { AccountID = 3, AccountCode = 790, AccountName = "Rounding" },
        };
        var methods = new List<PaymentMethod>
        {
            new() { MethodID = 9, MethodName = "Other" },
        };

        // Book £18.00, paid £17.97 → GBP outstanding £0.03 (not foreign € remainder).
        var receipt = HetznerReceipt();
        receipt.AmountPaid = 17.97m;
        var plan = ReceiptRoundingWriteOff.BuildPlan(receipt, 790, 0.05m, accounts, methods);
        Assert.Equal(0.03m, plan.Outstanding);
        Assert.Equal(0.03m, plan.PaymentAmount);

        // Foreign remainder without GBP conversion would look huge and must be rejected.
        Assert.Throws<ArgumentException>(() => ReceiptRoundingWriteOff.BuildPlan(
            HetznerReceipt(),
            790,
            0.05m,
            accounts,
            methods));
    }

    [Fact]
    public async Task Write_tools_are_blocked_while_read_only()
    {
        using var session = KashFlowSession.FromConfiguration(new ConfigurationBuilder().Build());
        var tools = new WriteTools(session, new McpRuntimeOptions(true));

        await Assert.ThrowsAsync<InvalidOperationException>(() => tools.AllocateBankPaymentToReceipts(
            DateTime.Today,
            10m,
            [new ReceiptPaymentAllocationInput { ReceiptNumber = 1, Amount = 10m }],
            bankAccountId: 4,
            paymentMethodId: 1));

        await Assert.ThrowsAsync<InvalidOperationException>(() => tools.InsertFxReceiptPayment(
            4151,
            DateTime.Today,
            10m,
            bankAccountId: 4,
            paymentMethodId: 1));

        await Assert.ThrowsAsync<InvalidOperationException>(() => tools.WriteOffReceiptRounding(
            1,
            DateTime.Today,
            nominal: 790));

        await Assert.ThrowsAsync<InvalidOperationException>(() => tools.CleanupFxReceiptBankLines(
            bankAccountId: 4,
            paymentDate: DateTime.Today,
            fluctuationAmount: -1.80m,
            dryRun: false));
    }
}
