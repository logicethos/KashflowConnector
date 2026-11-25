using KashFlow;
using KashflowMCP.Tools;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace KashflowMCP.Tests;

public class BankTransferTests
{
    [Fact]
    public void CreatePair_posts_money_out_of_the_source_and_into_the_destination()
    {
        var (outgoing, incoming) = BankTransfer.CreatePair(4, 9, 25m, new DateTime(2026, 5, 1), 7, "  float  ");

        Assert.Equal(4, outgoing.Accid);
        Assert.Equal(25m, outgoing.Moneyout);
        Assert.Equal(0m, outgoing.Moneyin);
        Assert.Equal(7, outgoing.TransactionType);
        Assert.Equal("float", outgoing.Comment);

        Assert.Equal(9, incoming.Accid);
        Assert.Equal(25m, incoming.Moneyin);
        Assert.Equal(0m, incoming.Moneyout);
        Assert.Equal(outgoing.Comment, incoming.Comment);
        Assert.Equal(outgoing.TransactionDate, incoming.TransactionDate);
    }

    [Fact]
    public void ResolveAccount_maps_nominal_codes_and_prefers_an_explicit_id()
    {
        var accounts = new List<BankAccount>
        {
            new() { AccountID = 4, AccountCode = 1206, AccountName = "Starling" },
            new() { AccountID = 9, AccountCode = 1209, AccountName = "Revolut" },
        };

        Assert.Equal(4, BankTransfer.ResolveAccountId(null, 1206, accounts, "from"));
        Assert.Equal(9, BankTransfer.ResolveAccountId(null, 1209, accounts, "to"));
        Assert.Equal(4, BankTransfer.ResolveAccountId(4, 1209, accounts, "from"));
        Assert.Throws<ArgumentException>(() => BankTransfer.ResolveAccountId(null, 9999, accounts, "from"));
    }

    [Fact]
    public void ResolveTransferType_prefers_bank_transfer_then_a_single_transfer_name()
    {
        var types = new List<BankTXType>
        {
            new() { TypeID = 1, TypeName = "Payment" },
            new() { TypeID = 6, TypeName = "Bank Transfer" },
        };

        Assert.Equal(6, BankTransfer.ResolveTransferTypeId(null, types));
        Assert.Equal(3, BankTransfer.ResolveTransferTypeId(3, types));
        Assert.Throws<ArgumentException>(() => BankTransfer.ResolveTransferTypeId(null, [new BankTXType { TypeID = 1, TypeName = "Payment" }]));
    }

    [Fact]
    public void CreatePair_rejects_the_same_account_and_a_zero_amount()
    {
        Assert.Throws<ArgumentException>(() => BankTransfer.CreatePair(4, 4, 10m, DateTime.Today, 1, null));
        Assert.Throws<ArgumentException>(() => BankTransfer.CreatePair(4, 9, 0m, DateTime.Today, 1, null));
    }

    [Fact]
    public async Task Post_removes_the_outgoing_transaction_when_the_destination_post_fails()
    {
        var deleted = new List<int>();
        var inserted = 0;

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => BankTransfer.PostAsync(
            4,
            9,
            10m,
            new DateTime(2026, 5, 1),
            6,
            "move",
            (_, _) =>
            {
                inserted++;
                if (inserted == 1)
                    return Task.FromResult(50);
                throw new InvalidOperationException("destination rejected");
            },
            (id, _) =>
            {
                deleted.Add(id);
                return Task.CompletedTask;
            },
            CancellationToken.None));

        Assert.Equal([50], deleted);
        Assert.Contains("was removed", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("password", error.ToString(), StringComparison.OrdinalIgnoreCase);
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
            toBankAccountId: 9,
            transactionTypeId: 6));

        Assert.Contains("ReadOnly", error.Message, StringComparison.Ordinal);
    }
}
