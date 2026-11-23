namespace KashFlow;

/// <summary>One page from a KashFlow method that returns a record count and page index.</summary>
public sealed record KashFlowPage<T>(IReadOnlyList<T> Items, int PageNumber, int TotalPages, int TotalRecords);
