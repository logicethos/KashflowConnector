using KashFlow;

namespace KashflowMCP.Tools;

/// <summary>Client-side search over <see cref="BankTransaction"/> rows from GetBankTransactions.</summary>
public static class BankTransactionSearch
{
    public const int DefaultMaxResults = 200;

    public enum Direction
    {
        Any,
        In,
        Out,
    }

    public sealed record Match(
        int BankAccountId,
        int BankAccountCode,
        string? BankAccountName,
        int TransactionId,
        DateTime? TransactionDate,
        decimal MoneyIn,
        decimal MoneyOut,
        string? Comment,
        int TransactionType);

    public static Direction ParseDirection(string? direction)
    {
        if (string.IsNullOrWhiteSpace(direction) || direction.Equals("any", StringComparison.OrdinalIgnoreCase))
            return Direction.Any;
        if (direction.Equals("in", StringComparison.OrdinalIgnoreCase))
            return Direction.In;
        if (direction.Equals("out", StringComparison.OrdinalIgnoreCase))
            return Direction.Out;
        throw new ArgumentException("direction must be in, out, or any.");
    }

    public static IReadOnlyList<BankAccount> ResolveAccounts(
        IReadOnlyList<int>? bankAccountIds,
        IReadOnlyList<int>? bankAccountCodes,
        IReadOnlyList<BankAccount> allAccounts)
    {
        ArgumentNullException.ThrowIfNull(allAccounts);

        var ids = (bankAccountIds ?? []).Where(id => id > 0).Distinct().ToHashSet();
        var codes = (bankAccountCodes ?? []).Where(code => code > 0).Distinct().ToHashSet();
        if (ids.Count == 0 && codes.Count == 0)
            return allAccounts.ToList();

        var matches = allAccounts
            .Where(account => ids.Contains(account.AccountID) || codes.Contains(account.AccountCode))
            .GroupBy(account => account.AccountID)
            .Select(group => group.First())
            .ToList();

        foreach (var id in ids)
        {
            if (matches.All(account => account.AccountID != id))
                throw new ArgumentException($"No bank account has accountId {id}.");
        }

        foreach (var code in codes)
        {
            if (matches.All(account => account.AccountCode != code))
                throw new ArgumentException($"No bank account has accountCode {code}.");
        }

        return matches;
    }

    public static bool MatchesAmount(BankTransaction transaction, decimal amount, decimal tolerance, Direction direction)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        if (tolerance < 0)
            throw new ArgumentException("tolerance must be 0 or greater.");

        bool Close(decimal value) => Math.Abs(value - amount) <= tolerance;

        return direction switch
        {
            Direction.In => Close(transaction.Moneyin),
            Direction.Out => Close(transaction.Moneyout),
            _ => Close(transaction.Moneyin) || Close(transaction.Moneyout),
        };
    }

    public static bool MatchesDate(BankTransaction transaction, DateTime dateFrom, DateTime dateTo)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        if (dateTo.Date < dateFrom.Date)
            throw new ArgumentException("dateTo must be on or after dateFrom.");
        if (transaction.TransactionDate is not DateTime when)
            return false;
        var day = when.Date;
        return day >= dateFrom.Date && day <= dateTo.Date;
    }

    public static IReadOnlyList<Match> Filter(
        BankAccount account,
        IEnumerable<BankTransaction> transactions,
        decimal amount,
        decimal tolerance,
        Direction direction,
        DateTime dateFrom,
        DateTime dateTo,
        int maxResults,
        ref int remaining)
    {
        ArgumentNullException.ThrowIfNull(account);
        ArgumentNullException.ThrowIfNull(transactions);
        if (maxResults <= 0)
            throw new ArgumentException("maxResults must be greater than 0.");

        var matches = new List<Match>();
        foreach (var transaction in transactions)
        {
            if (remaining <= 0)
                break;
            if (!MatchesDate(transaction, dateFrom, dateTo))
                continue;
            if (!MatchesAmount(transaction, amount, tolerance, direction))
                continue;

            matches.Add(new Match(
                account.AccountID,
                account.AccountCode,
                account.AccountName,
                transaction.ID,
                transaction.TransactionDate,
                transaction.Moneyin,
                transaction.Moneyout,
                transaction.Comment,
                transaction.TransactionType));
            remaining--;
        }

        return matches;
    }
}
