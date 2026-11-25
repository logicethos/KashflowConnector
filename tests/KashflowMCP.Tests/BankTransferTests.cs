using KashFlow;
using KashflowMCP.Tools;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace KashflowMCP.Tests;

public class BankTransferTests
{
    // Example ids from a real company — fixtures only, not hard-coded in the tool.
    private static readonly List<BankAccount> SampleAccounts =
    [
        new() { AccountID = 4, AccountCode = 1206, AccountName = "Starling" },
        new() { AccountID = 9, AccountCode = 1209, AccountName = "Revolut" },
        new() { AccountID = 2, AccountCode = 1203, AccountName = "Directors Loan" },
    ];

    private static readonly List<NominalCode> SampleNominals =
    [
        new() { Id = 27897974, Code = 1206, Name = "Starling" },
        new() { Id = 32769661, Code = 1209, Name = "Revolut" },
        new() { Id = 1575947, Code = 1203, Name = "Directors Loan" },
    ];

    [Fact]
    public void Create_posts_a_single_money_out_with_destination_nominal_as_transaction_type()
    {
        var transaction = BankTransfer.Create(4, 32769661, 25m, new DateTime(2026, 5, 1), "  float  ");

        Assert.Equal(4, transaction.Accid);
        Assert.Equal(25m, transaction.Moneyout);
        Assert.Equal(0m, transaction.Moneyin);
        Assert.Equal(32769661, transaction.TransactionType);
        Assert.Equal("float", transaction.Comment);
        Assert.Equal(new DateTime(2026, 5, 1), transaction.TransactionDate);
    }

    [Fact]
    public void ResolveAccount_maps_nominal_codes_and_prefers_an_explicit_id()
    {
        Assert.Equal(4, BankTransfer.ResolveAccountId(null, 1206, SampleAccounts, "from"));
        Assert.Equal(9, BankTransfer.ResolveAccountId(null, 1209, SampleAccounts, "to"));
        Assert.Equal(4, BankTransfer.ResolveAccountId(4, 1209, SampleAccounts, "from"));
        Assert.Throws<ArgumentException>(() => BankTransfer.ResolveAccountId(null, 9999, SampleAccounts, "from"));
    }

    [Fact]
    public void ResolveDestinationNominal_maps_bank_code_to_nominal_id()
    {
        Assert.Equal(
            32769661,
            BankTransfer.ResolveDestinationNominalId(null, null, 1209, SampleAccounts, SampleNominals));
        Assert.Equal(
            27897974,
            BankTransfer.ResolveDestinationNominalId(null, 4, null, SampleAccounts, SampleNominals));
        Assert.Equal(
            1575947,
            BankTransfer.ResolveDestinationNominalId(1575947, null, null, SampleAccounts, SampleNominals));
    }

    [Fact]
    public void ResolveDestinationNominal_prefers_toNominalId_and_rejects_a_mismatch()
    {
        Assert.Equal(
            32769661,
            BankTransfer.ResolveDestinationNominalId(32769661, 9, 1209, SampleAccounts, SampleNominals));

        var error = Assert.Throws<ArgumentException>(() =>
            BankTransfer.ResolveDestinationNominalId(27897974, 9, null, SampleAccounts, SampleNominals));
        Assert.Contains("27897974", error.Message, StringComparison.Ordinal);
        Assert.Contains("1209", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Create_rejects_a_zero_amount_and_EnsureDifferentAccounts_rejects_the_same_account()
    {
        Assert.Throws<ArgumentException>(() => BankTransfer.Create(4, 32769661, 0m, DateTime.Today, null));
        Assert.Throws<ArgumentException>(() => BankTransfer.EnsureDifferentAccounts(4, 4));
        BankTransfer.EnsureDifferentAccounts(4, 9);
    }

    [Fact]
    public void FindBankAccountIdForNominal_maps_example_ids()
    {
        Assert.Equal(4, BankTransfer.FindBankAccountIdForNominal(27897974, SampleAccounts, SampleNominals));
        Assert.Equal(9, BankTransfer.FindBankAccountIdForNominal(32769661, SampleAccounts, SampleNominals));
        Assert.Null(BankTransfer.FindBankAccountIdForNominal(999, SampleAccounts, SampleNominals));
    }

    [Fact]
    public async Task Post_inserts_a_single_transaction_and_does_not_delete()
    {
        BankTransaction? posted = null;
        var result = await BankTransfer.PostAsync(
            4,
            32769661,
            10m,
            new DateTime(2026, 5, 1),
            "move",
            (transaction, _) =>
            {
                posted = transaction;
                return Task.FromResult(50);
            },
            CancellationToken.None);

        Assert.Equal(50, result.TransactionId);
        Assert.Equal(4, result.FromBankAccountId);
        Assert.Equal(32769661, result.ToNominalId);
        Assert.NotNull(posted);
        Assert.Equal(4, posted!.Accid);
        Assert.Equal(10m, posted.Moneyout);
        Assert.Equal(0m, posted.Moneyin);
        Assert.Equal(32769661, posted.TransactionType);
    }

    [Fact]
    public async Task Transfer_tool_is_blocked_while_read_only()
    {
        using var session = KashFlowSession.FromConfiguration(new ConfigurationBuilder().Build());
        var tools = new WriteTools(session, new McpRuntimeOptions(true));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => tools.TransferBetweenBankAccounts(
            10m,
            new DateTime(2026, 5, 1),
            fromBankAccountId: 4,
            toBankAccountId: 9));

        Assert.Contains("ReadOnly", error.Message, StringComparison.Ordinal);
    }
}
