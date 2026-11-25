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
    public void Validate_uses_gbp_book_outstanding_for_foreign_currency_receipts()
    {
        // €21.49 at 1.1941 → book £18.00; allocating the foreign total must fail,
        // while allocating the GBP book value is allowed.
        var receipts = new Dictionary<int, Receipt2>
        {
            [4189] = new()
            {
                ReceiptNumber = 4189,
                NetAmount = 21.49m,
                VATAmount = 0m,
                AmountPaid = 0m,
                CurrencyCode = "EUR",
                ExchangeRate = 1.1941m,
            },
        };

        var over = Assert.Throws<ArgumentException>(() => AllocateBankPayment.Validate(
            21.49m,
            [new(4189, 21.49m)],
            receipts));
        Assert.Contains("exceeds outstanding", over.Message, StringComparison.Ordinal);

        var planned = AllocateBankPayment.Validate(
            18.00m,
            [new(4189, 18.00m)],
            receipts);
        Assert.Single(planned);
        Assert.Equal(18.00m, planned[0].OutstandingBefore);
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
