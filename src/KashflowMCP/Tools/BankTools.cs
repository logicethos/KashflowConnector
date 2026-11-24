using System.ComponentModel;
using KashFlow;
using ModelContextProtocol.Server;

namespace KashflowMCP.Tools;

[McpServerToolType]
public sealed class BankTools(KashFlowSession session)
{
    [McpServerTool(Name = "get_bank_accounts"), Description("List bank accounts. accountId is the internal id (SOAP BankAccount.AccountID) and is what payments store in payAccount. accountCode is the nominal code, for example 1206 for a Starling account or 1209 for Revolut. accountName is the display name. insert_receipt_payment accepts either accountId or accountCode.")]
    public async Task<string> GetBankAccounts(CancellationToken cancellationToken)
        => JsonResults.Serialize(await session.Client.GetBankAccountsAsync(cancellationToken));

    [McpServerTool(Name = "get_bank_overview"), Description("Get bank overview balances.")]
    public async Task<string> GetBankOverview(CancellationToken cancellationToken)
        => JsonResults.Serialize(await session.Client.GetBankOverviewAsync(cancellationToken));

    [McpServerTool(Name = "get_bank_balance"), Description("Get balance for a bank account on a date.")]
    public async Task<string> GetBankBalance(
        [Description("Bank account ID")] int accountId,
        [Description("Balance date")] DateTime balanceDate,
        CancellationToken cancellationToken)
        => JsonResults.Serialize(new { balance = await session.Client.GetBankBalanceAsync(accountId, balanceDate, cancellationToken) });

    [McpServerTool(Name = "get_bank_transactions"), Description("List bank transactions for an account.")]
    public async Task<string> GetBankTransactions(
        [Description("Bank account ID")] int accountId,
        CancellationToken cancellationToken)
        => JsonResults.Serialize(await session.Client.GetBankTransactionsAsync(accountId, cancellationToken));

    [McpServerTool(Name = "get_bank_tx_types"), Description("List bank transaction types.")]
    public async Task<string> GetBankTxTypes(CancellationToken cancellationToken)
        => JsonResults.Serialize(await session.Client.GetBankTxTypesAsync(cancellationToken));
}
