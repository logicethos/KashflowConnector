namespace KashFlow;

/// <summary>
/// A KashFlow SOAP call failed. The service returned a fault, a status other than OK,
/// or a body that does not match the published contract.
/// </summary>
public sealed class KashFlowException : Exception
{
    public KashFlowException(string operation, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Operation = operation;
    }

    public static KashFlowException FromStatus(string operation, string? status, string? statusDetail)
        => new(operation, FormatStatus(operation, status, statusDetail))
        {
            Status = status,
            StatusDetail = statusDetail,
        };

    /// <summary>SOAP operation that failed.</summary>
    public string Operation { get; }

    /// <summary>KashFlow status text, when the body carried one.</summary>
    public string? Status { get; init; }

    /// <summary>KashFlow status detail, when the body carried one.</summary>
    public string? StatusDetail { get; init; }

    /// <summary>SOAP fault code, when the body was a fault.</summary>
    public string? FaultCode { get; init; }

    private static string FormatStatus(string operation, string? status, string? statusDetail)
    {
        var detail = string.IsNullOrWhiteSpace(statusDetail) ? "" : " " + statusDetail.Trim();
        return $"KashFlow {operation} returned status {status ?? "unknown"}.{detail}";
    }
}
