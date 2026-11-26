using KashFlow;
using KashflowMCP.Tools;
using Xunit;

namespace KashflowMCP.Tests;

public class ReceiptOutstandingGbpTests
{
    [Fact]
    public void BookValueGbp_divides_foreign_total_by_exchange_rate()
    {
        var receipt = new Receipt2
        {
            NetAmount = 21.49m,
            VATAmount = 0m,
            ExchangeRate = 1.1941m,
        };

        Assert.Equal(18.00m, ReceiptOutstanding.BookValueGbp(receipt));
        Assert.Equal(18.00m, ReceiptOutstanding.Balance(receipt));
    }

    [Fact]
    public void Balance_subtracts_gbp_amount_paid_from_book_value()
    {
        var receipt = new Receipt2
        {
            NetAmount = 21.49m,
            VATAmount = 0m,
            AmountPaid = 10m,
            ExchangeRate = 1.1941m,
        };

        Assert.Equal(8.00m, ReceiptOutstanding.Balance(receipt));
    }

    [Fact]
    public void BookValueGbp_without_exchange_rate_uses_net_plus_vat()
    {
        var receipt = new Receipt2
        {
            NetAmount = 40m,
            VATAmount = 8m,
            AmountPaid = 5m,
            ExchangeRate = 0m,
        };

        Assert.Equal(48m, ReceiptOutstanding.BookValueGbp(receipt));
        Assert.Equal(43m, ReceiptOutstanding.Balance(receipt));
    }
}
