using KashFlow;

namespace KashflowMCP.Tools;

/// <summary>
/// A bank-to-bank transfer as KashFlow records it: one <see cref="BankTransaction"/> on the
/// source account with <see cref="BankTransaction.Moneyout"/> set and
/// <see cref="BankTransaction.TransactionType"/> equal to the destination bank account's
/// nominal id (<see cref="NominalCode.Id"/>). KashFlow mirrors that line onto the destination
/// feed. A second money-in insert would double the transfer.
/// </summary>
public static class BankTransfer
{
    public sealed record Result(int TransactionId, int FromBankAccountId, int ToNominalId);

    public static BankTransaction Create(
        int fromBankAccountId,
        int toNominalId,
        decimal amount,
        DateTime transferDate,
        string? comment)
    {
        if (fromBankAccountId <= 0)
            throw new ArgumentException("fromBankAccountId must be an accountId from get_bank_accounts.");
        if (toNominalId <= 0)
            throw new ArgumentException("toNominalId must be a nominal id (get_nominal_codes id) for the destination bank.");
        if (amount <= 0)
            throw new ArgumentException("amount must be greater than 0.");

        var note = string.IsNullOrWhiteSpace(comment)
            ? $"Transfer to nominal {toNominalId}"
            : comment.Trim();

        return new BankTransaction
        {
            Accid = fromBankAccountId,
            TransactionDate = transferDate,
            Moneyout = amount,
            Moneyin = 0,
            TransactionType = toNominalId,
            Comment = note,
        };
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

    /// <summary>
    /// Destination side of a transfer: either an explicit nominal id, or the
    /// <see cref="NominalCode.Id"/> whose <see cref="NominalCode.Code"/> matches the
    /// destination bank account's <see cref="BankAccount.AccountCode"/>.
    /// </summary>
    public static int ResolveDestinationNominalId(
        int? toNominalId,
        int? toBankAccountId,
        int? toBankAccountCode,
        IReadOnlyList<BankAccount>? accounts,
        IReadOnlyList<NominalCode>? nominals)
    {
        if (toNominalId is int explicitId && explicitId > 0)
        {
            var nominal = (nominals ?? []).FirstOrDefault(item => item.Id == explicitId);
            if (nominal is null && nominals is not null)
                throw new ArgumentException(
                    $"toNominalId {explicitId} is not in get_nominal_codes.");

            if (toBankAccountId is int accountId && accountId > 0
                || toBankAccountCode is int accountCode && accountCode > 0)
            {
                var expectedCode = ResolveDestinationAccountCode(toBankAccountId, toBankAccountCode, accounts);
                if (nominal is not null && nominal.Code != expectedCode)
                    throw new ArgumentException(
                        $"toNominalId {explicitId} is nominal code {nominal.Code}, but the destination bank account uses code {expectedCode}.");
            }

            return explicitId;
        }

        var code = ResolveDestinationAccountCode(toBankAccountId, toBankAccountCode, accounts);
        var matches = (nominals ?? [])
            .Where(item => item.Code == code)
            .ToList();
        if (matches.Count == 1)
            return matches[0].Id;
        if (matches.Count == 0)
            throw new ArgumentException(
                $"No nominal code has Code {code}. Call get_nominal_codes, or pass toNominalId.");

        var list = string.Join(", ", matches.Select(item => $"{item.Id}:{item.Name}"));
        throw new ArgumentException(
            $"More than one nominal has Code {code} ({list}). Pass toNominalId.");
    }

    public static int ResolveDestinationAccountCode(
        int? toBankAccountId,
        int? toBankAccountCode,
        IReadOnlyList<BankAccount>? accounts)
    {
        if (toBankAccountCode is int code && code > 0)
        {
            if (toBankAccountId is int accountId && accountId > 0)
            {
                var account = (accounts ?? []).FirstOrDefault(item => item.AccountID == accountId)
                    ?? throw new ArgumentException(
                        $"No bank account has accountId {accountId}. Call get_bank_accounts.");
                if (account.AccountCode != code)
                    throw new ArgumentException(
                        $"toBankAccountId {accountId} has accountCode {account.AccountCode}, not {code}.");
            }

            return code;
        }

        if (toBankAccountId is not int id || id <= 0)
            throw new ArgumentException(
                "Pass toNominalId, or toBankAccountId / toBankAccountCode so the destination nominal can be resolved.");

        var match = (accounts ?? []).FirstOrDefault(item => item.AccountID == id)
            ?? throw new ArgumentException(
                $"No bank account has accountId {id}. Call get_bank_accounts.");
        if (match.AccountCode <= 0)
            throw new ArgumentException(
                $"Bank account {id} has no accountCode to map to a nominal id. Pass toNominalId.");
        return match.AccountCode;
    }

    public static void EnsureDifferentAccounts(int fromBankAccountId, int? toBankAccountId)
    {
        if (toBankAccountId is int toId && toId > 0 && fromBankAccountId == toId)
            throw new ArgumentException("The source and destination bank accounts are the same.");
    }

    /// <summary>
    /// Finds the bank account whose <see cref="BankAccount.AccountCode"/> matches the
    /// <see cref="NominalCode.Code"/> for <paramref name="nominalId"/>, when that mapping is unique.
    /// </summary>
    public static int? FindBankAccountIdForNominal(
        int nominalId,
        IReadOnlyList<BankAccount>? accounts,
        IReadOnlyList<NominalCode>? nominals)
    {
        if (nominalId <= 0)
            return null;

        var nominal = (nominals ?? []).FirstOrDefault(item => item.Id == nominalId);
        if (nominal is null)
            return null;

        var matches = (accounts ?? [])
            .Where(account => account.AccountCode == nominal.Code)
            .ToList();
        return matches.Count == 1 ? matches[0].AccountID : null;
    }

    public static async Task<Result> PostAsync(
        int fromBankAccountId,
        int toNominalId,
        decimal amount,
        DateTime transferDate,
        string? comment,
        Func<BankTransaction, CancellationToken, Task<int>> insert,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(insert);
        var transaction = Create(fromBankAccountId, toNominalId, amount, transferDate, comment);
        var transactionId = await insert(transaction, cancellationToken);
        return new Result(transactionId, fromBankAccountId, toNominalId);
    }
}
