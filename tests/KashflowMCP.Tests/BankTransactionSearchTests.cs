using KashFlow;
using KashflowMCP.Tools;
using Xunit;

namespace KashflowMCP.Tests;

public class BankTransactionSearchTests
{
    private static BankAccount Account(int id, int code, string name)
        => new() { AccountID = id, AccountCode = code, AccountName = name };

    private static BankTransaction Tx(int id, DateTime date, decimal moneyIn, decimal moneyOut, string? comment = null)
        => new()
        {
            ID = id,
            TransactionDate = date,
            Moneyin = moneyIn,
            Moneyout = moneyOut,
            Comment = comment,
            TransactionType = 7,
        };

    [Fact]
    public void Filter_matches_tolerance_direction_date_and_account()
    {
        var starling = Account(4, 1206, "Starling");
        var txs = new[]
        {
            Tx(1, new DateTime(2026, 5, 1), 0m, 100.00m, "exact out"),
            Tx(2, new DateTime(2026, 5, 2), 0m, 100.04m, "near out"),
            Tx(3, new DateTime(2026, 5, 3), 100.00m, 0m, "in"),
            Tx(4, new DateTime(2026, 4, 30), 0m, 100.00m, "too early"),
            Tx(5, new DateTime(2026, 5, 10), 0m, 100.00m, "too late"),
        };

        var remaining = 200;
        var outs = BankTransactionSearch.Filter(
            starling,
            txs,
            amount: 100m,
            tolerance: 0.05m,
            BankTransactionSearch.Direction.Out,
            new DateTime(2026, 5, 1),
            new DateTime(2026, 5, 5),
            200,
            ref remaining);

        Assert.Equal(2, outs.Count);
        Assert.Equal([1, 2], outs.Select(match => match.TransactionId).ToArray());
        Assert.All(outs, match => Assert.Equal(1206, match.BankAccountCode));

        remaining = 200;
        var ins = BankTransactionSearch.Filter(
            starling,
            txs,
            100m,
            0m,
            BankTransactionSearch.Direction.In,
            new DateTime(2026, 5, 1),
            new DateTime(2026, 5, 5),
            200,
            ref remaining);
        Assert.Single(ins);
        Assert.Equal(3, ins[0].TransactionId);
    }

    [Fact]
    public void ResolveAccounts_filters_by_id_or_code_or_returns_all()
    {
        var all = new List<BankAccount>
        {
            Account(4, 1206, "Starling"),
            Account(9, 1209, "Revolut"),
        };

        Assert.Equal(2, BankTransactionSearch.ResolveAccounts(null, null, all).Count);
        Assert.Equal([4], BankTransactionSearch.ResolveAccounts([4], null, all).Select(a => a.AccountID));
        Assert.Equal([9], BankTransactionSearch.ResolveAccounts(null, [1209], all).Select(a => a.AccountID));
        Assert.Throws<ArgumentException>(() => BankTransactionSearch.ResolveAccounts([99], null, all));
    }
}
