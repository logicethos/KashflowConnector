namespace KashFlow.Messages;

internal interface IKashFlowStatus
{
    string? Status { get; }

    string? StatusDetail { get; }
}
