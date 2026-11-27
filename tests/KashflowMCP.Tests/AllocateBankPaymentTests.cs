using KashFlow;
using KashflowMCP.Tools;
using Xunit;

namespace KashflowMCP.Tests;

public class AllocateBankPaymentTests
{
    private static Receipt2 Receipt(int number, decimal net, decimal vat, decimal paid)
        => new()
        {
            ReceiptNumber = number,
            NetAmount = net,
            VATAmount = vat,
            AmountPaid = paid,
        };

    [Fact]
    public void Validate_rejects_mismatched_total_duplicate_and_over_allocation()
    {
        var receipts = new Dictionary<int, Receipt2>
        {
            [10] = Receipt(10, 40m, 0m, 0m),
            [11] = Receipt(11, 30m, 0m, 0m),
        };

        var mismatch = Assert.Throws<ArgumentException>(() => AllocateBankPayment.Validate(
            50m,
            [new(10, 20m), new(11, 20m)],
            receipts));
        Assert.Contains("equal totalAmount", mismatch.Message, StringComparison.Ordinal);

        var duplicate = Assert.Throws<ArgumentException>(() => AllocateBankPayment.Validate(
            40m,
            [new(10, 20m), new(10, 20m)],
            receipts));
        Assert.Contains("Duplicate", duplicate.Message, StringComparison.Ordinal);

        var over = Assert.Throws<ArgumentException>(() => AllocateBankPayment.Validate(
            50m,
            [new(10, 50m)],
            receipts));
        Assert.Contains("exceeds outstanding", over.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_accepts_a_full_split_to_the_penny()
    {
        var receipts = new Dictionary<int, Receipt2>
        {
            [10] = Receipt(10, 25.10m, 0m, 5.10m),
            [11] = Receipt(11, 10m, 0m, 0m),
        };

        var planned = AllocateBankPayment.Validate(
            30m,
            [new(10, 20m), new(11, 10m)],
            receipts);

        Assert.Equal(2, planned.Count);
        Assert.Equal(20m, planned[0].OutstandingBefore);
        Assert.Equal(10m, planned[1].OutstandingBefore);
    }

    [Fact]
    public void Validate_accepts_a_purchase_credit_note_and_rejects_zero_and_over_credit()
    {
        var receipts = new Dictionary<int, Receipt2>
        {
            [4026] = Receipt(4026, -11.98m, 0m, 0m),
            [100] = Receipt(100, 100m, 0m, 0m),
        };

        var credit = AllocateBankPayment.Validate(
            -11.98m,
            [new(4026, -11.98m)],
            receipts);
        Assert.Equal(-11.98m, Assert.Single(credit).Amount);
        Assert.Equal(-11.98m, credit[0].OutstandingBefore);

        var mixed = AllocateBankPayment.Validate(
            88.02m,
            [new(100, 100m), new(4026, -11.98m)],
            receipts);
        Assert.Equal(-11.98m, mixed[1].Amount);

        var zero = Assert.Throws<ArgumentException>(() => AllocateBankPayment.Validate(
            0m,
            [new(4026, 0m)],
            receipts));
        Assert.Contains("non-zero", zero.Message, StringComparison.Ordinal);

        var overCredit = Assert.Throws<ArgumentException>(() => AllocateBankPayment.Validate(
            -20m,
            [new(4026, -20m)],
            receipts));
        Assert.Contains("exceeds outstanding", overCredit.Message, StringComparison.Ordinal);

        var wrongSign = Assert.Throws<ArgumentException>(() => AllocateBankPayment.Validate(
            5m,
            [new(4026, 5m)],
            receipts));
        Assert.Contains("same sign", wrongSign.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Post_passes_a_negative_pay_amount_through_to_insert()
    {
        Payment? sent = null;
        var (succeeded, failure) = await AllocateBankPayment.PostAsync(
            [new(4026, -11.98m, -11.98m)],
            new DateTime(2026, 4, 2),
            3,
            8,
            "KF04026",
            (payment, _) =>
            {
                sent = payment;
                return Task.FromResult(146449956);
            },
            CancellationToken.None);

        Assert.Null(failure);
        var posted = Assert.Single(succeeded);
        Assert.Equal(-11.98m, posted.Amount);
        Assert.Equal(146449956, posted.ReceiptPaymentId);
        Assert.NotNull(sent);
        Assert.Equal(4026, sent.PayInvoice);
        Assert.Equal(-11.98m, sent.PayAmount);
        Assert.Equal(8, sent.PayAccount);
    }

    [Fact]
    public async Task Post_keeps_earlier_successes_when_a_later_insert_fails()
    {
        var planned = new List<AllocateBankPayment.PlannedPayment>
        {
            new(10, 5m, 5m),
            new(11, 5m, 5m),
        };
        var inserts = 0;

        var (succeeded, failure) = await AllocateBankPayment.PostAsync(
            planned,
            new DateTime(2026, 5, 1),
            2,
            4,
            "split",
            (_, _) =>
            {
                inserts++;
                if (inserts == 1)
                    return Task.FromResult(100);
                throw new InvalidOperationException("second failed");
            },
            CancellationToken.None);

        Assert.NotNull(failure);
        Assert.Single(succeeded);
        Assert.Equal(10, succeeded[0].ReceiptNumber);
        Assert.Equal(100, succeeded[0].ReceiptPaymentId);
    }
}
