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

    [McpServerTool(Name = "search_bank_transactions"), Description("Find bank transactions across one or more bank accounts by amount and date range. Uses GetBankTransactions per account and filters client-side. Returns at most 200 matches.")]
    public async Task<string> SearchBankTransactions(
        [Description("Amount to match against moneyin and/or moneyout.")] decimal amount,
        [Description("Inclusive start date (ISO-8601).")] DateTime dateFrom,
        [Description("Inclusive end date (ISO-8601).")] DateTime dateTo,
        [Description("Allowed absolute difference from amount. Defaults to 0.")] decimal tolerance = 0,
        [Description("Which side to compare: in, out, or any (default).")] string? direction = null,
        [Description("Bank account ids from get_bank_accounts. Optional when bankAccountCodes is set or when searching all accounts.")] int[]? bankAccountIds = null,
        [Description("Bank account nominal codes from get_bank_accounts. Optional when bankAccountIds is set or when searching all accounts.")] int[]? bankAccountCodes = null,
        CancellationToken cancellationToken = default)
    {
        var dir = BankTransactionSearch.ParseDirection(direction);
        var accounts = BankTransactionSearch.ResolveAccounts(
            bankAccountIds,
            bankAccountCodes,
            await session.Client.GetBankAccountsAsync(cancellationToken));

        var remaining = BankTransactionSearch.DefaultMaxResults;
        var matches = new List<BankTransactionSearch.Match>();
        foreach (var account in accounts)
        {
            if (remaining <= 0)
                break;
            var transactions = await session.Client.GetBankTransactionsAsync(account.AccountID, cancellationToken);
            matches.AddRange(BankTransactionSearch.Filter(
                account,
                transactions,
                amount,
                tolerance,
                dir,
                dateFrom,
                dateTo,
                BankTransactionSearch.DefaultMaxResults,
                ref remaining));
        }

        var duplicateIds = BankTransactionSearch.DuplicateTransactionIds(matches);
        return JsonResults.Serialize(new
        {
            matchCount = matches.Count,
            truncated = remaining <= 0,
            // Same BankTransaction.ID matching two amounts usually means one API row has both
            // moneyIn and moneyOut set (not a search id-assignment bug).
            duplicateTransactionIds = duplicateIds,
            matches = matches.Select(match => new
            {
                bankAccountId = match.BankAccountId,
                bankAccountCode = match.BankAccountCode,
                bankAccountName = match.BankAccountName,
                transactionId = match.TransactionId,
                transactionDate = match.TransactionDate,
                moneyIn = match.MoneyIn,
                moneyOut = match.MoneyOut,
                comment = match.Comment,
                transactionType = match.TransactionType,
                hasBothMoneyInAndOut = match.HasBothMoneyInAndOut,
            }),
        });
    }
}
