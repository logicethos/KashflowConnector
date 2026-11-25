using KashFlow;

namespace KashflowMCP.Tools;

/// <summary>
/// A bank-to-bank transfer built from two KashFlow bank transactions.
/// The SOAP API has no transfer operation. One transaction can name only one account.
/// </summary>
public static class BankTransfer
{
    public sealed record Result(int FromTransactionId, int ToTransactionId);

    public static (BankTransaction Outgoing, BankTransaction Incoming) CreatePair(
        int fromBankAccountId,
        int toBankAccountId,
        decimal amount,
        DateTime transferDate,
        int transactionTypeId,
        string? comment)
    {
        if (fromBankAccountId <= 0 || toBankAccountId <= 0)
            throw new ArgumentException("Both bank accounts must be account ids from get_bank_accounts.");
        if (fromBankAccountId == toBankAccountId)
            throw new ArgumentException("The source and destination bank accounts are the same.");
        if (amount <= 0)
            throw new ArgumentException("amount must be greater than 0.");
        if (transactionTypeId <= 0)
            throw new ArgumentException("transactionTypeId must be a typeId from get_bank_tx_types.");

        var note = string.IsNullOrWhiteSpace(comment)
            ? $"Transfer from account {fromBankAccountId} to account {toBankAccountId}"
            : comment.Trim();

        var outgoing = new BankTransaction
        {
            Accid = fromBankAccountId,
            TransactionDate = transferDate,
            Moneyout = amount,
            Moneyin = 0,
            TransactionType = transactionTypeId,
            Comment = note,
        };
        var incoming = new BankTransaction
        {
            Accid = toBankAccountId,
            TransactionDate = transferDate,
            Moneyin = amount,
            Moneyout = 0,
            TransactionType = transactionTypeId,
            Comment = note,
        };
        return (outgoing, incoming);
    }

    public static int ResolveAccountId(
        int? bankAccountId,
        int? bankAccountCode,
        IReadOnlyList<BankAccount>? accounts,
        string side)
    {
        try
        {
            return ReceiptPaymentMapper.ResolveBankAccountId(bankAccountId, bankAccountCode, accounts);
        }
        catch (ArgumentException ex)
        {
            throw new ArgumentException(
                ex.Message
                    .Replace("bankAccountId", $"{side}BankAccountId", StringComparison.Ordinal)
                    .Replace("bankAccountCode", $"{side}BankAccountCode", StringComparison.Ordinal),
                ex);
        }
    }

    public static int ResolveTransferTypeId(int? transactionTypeId, IReadOnlyList<BankTXType>? types)
    {
        if (transactionTypeId is int id && id > 0)
            return id;

        var available = types ?? [];
        var exact = available.FirstOrDefault(type =>
            string.Equals(type.TypeName, "bank transfer", StringComparison.OrdinalIgnoreCase)
            || string.Equals(type.TypeName, "transfer", StringComparison.OrdinalIgnoreCase));
        if (exact is not null)
            return exact.TypeID;

        var named = available
            .Where(type => type.TypeName?.Contains("transfer", StringComparison.OrdinalIgnoreCase) == true)
            .ToList();
        if (named.Count == 1)
            return named[0].TypeID;
        if (named.Count == 0)
            throw new ArgumentException(
                "No bank transaction type looks like a transfer. Call get_bank_tx_types and pass transactionTypeId.");

        var list = string.Join(", ", named.Select(type => $"{type.TypeID}:{type.TypeName}"));
        throw new ArgumentException(
            $"More than one transaction type looks like a transfer ({list}). Pass transactionTypeId.");
    }

    public static async Task<Result> PostAsync(
        int fromBankAccountId,
        int toBankAccountId,
        decimal amount,
        DateTime transferDate,
        int transactionTypeId,
        string? comment,
        Func<BankTransaction, CancellationToken, Task<int>> insert,
        Func<int, CancellationToken, Task> delete,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(insert);
        ArgumentNullException.ThrowIfNull(delete);
        var (outgoing, incoming) = CreatePair(
            fromBankAccountId, toBankAccountId, amount, transferDate, transactionTypeId, comment);

        var fromTransactionId = await insert(outgoing, cancellationToken);
        try
        {
            var toTransactionId = await insert(incoming, cancellationToken);
            return new Result(fromTransactionId, toTransactionId);
        }
        catch (Exception incomingFailure)
        {
            try
            {
                await delete(fromTransactionId, cancellationToken);
            }
            catch (Exception deleteFailure)
            {
                throw new InvalidOperationException(
                    $"The destination side of the transfer failed, and outgoing bank transaction {fromTransactionId} is still on account {fromBankAccountId}. Remove that transaction before retrying.",
                    new AggregateException(incomingFailure, deleteFailure));
            }

            throw new InvalidOperationException(
                $"The destination side of the transfer failed. Outgoing bank transaction {fromTransactionId} was removed.",
                incomingFailure);
        }
    }
}
