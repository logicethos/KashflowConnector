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

    [Fact]
    public void Dual_amount_row_matches_both_searches_under_same_transaction_id()
    {
        // Live KF04195 observation: one GetBankTransactions row can carry moneyOut=148.11 and
        // moneyIn=1.80 under the same ID. Search reports that shared real id; it does not invent it.
        var starling = Account(767325, 1206, "Starling");
        var txs = new[]
        {
            new BankTransaction
            {
                ID = 154508988,
                TransactionDate = new DateTime(2026, 5, 1),
                Moneyin = 1.80m,
                Moneyout = 148.11m,
                Comment = "Currency Fluctuation",
                TransactionType = 2,
            },
        };

        Assert.True(BankTransactionSearch.HasBothMoneySides(txs[0]));

        var remaining = 200;
        var bank = BankTransactionSearch.Filter(
            starling, txs, 148.11m, 0m, BankTransactionSearch.Direction.Any,
            new DateTime(2026, 5, 1), new DateTime(2026, 5, 1), 200, ref remaining);
        remaining = 200;
        var fx = BankTransactionSearch.Filter(
            starling, txs, 1.80m, 0m, BankTransactionSearch.Direction.Any,
            new DateTime(2026, 5, 1), new DateTime(2026, 5, 1), 200, ref remaining);

        Assert.Single(bank);
        Assert.Single(fx);
        Assert.Equal(154508988, bank[0].TransactionId);
        Assert.Equal(154508988, fx[0].TransactionId);
        Assert.True(bank[0].HasBothMoneyInAndOut);
        Assert.True(fx[0].HasBothMoneyInAndOut);
        Assert.Equal([154508988], BankTransactionSearch.DuplicateTransactionIds(bank.Concat(fx)));
    }

    [Fact]
    public void MatchesAmount_also_compares_absolute_values()
    {
        var tx = Tx(1, new DateTime(2026, 5, 1), 0m, -1.80m, "signed out");
        Assert.True(BankTransactionSearch.MatchesAmount(tx, 1.80m, 0m, BankTransactionSearch.Direction.Out));
        Assert.True(BankTransactionSearch.MatchesAmount(tx, 1.80m, 0m, BankTransactionSearch.Direction.Any));
    }
}
