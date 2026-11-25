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
        };

    [Fact]
    public void Fx_plan_uses_outstanding_minus_bank_amount_and_companion_opposite_sign()
    {
        var plan = FxReceiptPayment.BuildPlan(SourceReceipt(), bankAmountGbp: 95m, forexNominalId: 999);
        Assert.Equal(100m, plan.OutstandingBefore);
        Assert.Equal(5m, plan.FluctuationAmount);
        Assert.Equal(-5m, plan.CompanionNetAmount);
        Assert.Equal(-5m, plan.CompanionPaymentAmount);
        Assert.Equal("Loss/Gain on KF04151 due to exchange rate fluctuations", plan.CompanionDescription);

        var gain = FxReceiptPayment.BuildPlan(SourceReceipt(), bankAmountGbp: 105m, forexNominalId: 999);
        Assert.Equal(-5m, gain.FluctuationAmount);
        Assert.Equal(5m, gain.CompanionNetAmount);
        Assert.Equal(5m, gain.CompanionPaymentAmount);
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
    }
}
