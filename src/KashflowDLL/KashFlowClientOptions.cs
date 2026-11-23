namespace KashFlow;

/// <summary>Credentials, endpoint, and timeout for <see cref="KashFlowClient"/>.</summary>
public sealed class KashFlowClientOptions
{
    public const string DefaultEndpoint = "https://securedwebapp.com/api/service.asmx";

    public KashFlowClientOptions(string userName, string password)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userName);
        ArgumentException.ThrowIfNullOrWhiteSpace(password);
        UserName = userName;
        Password = password;
    }

    /// <summary>KashFlow API user name.</summary>
    public string UserName { get; }

    /// <summary>KashFlow API password.</summary>
    public string Password { get; }

    /// <summary>SOAP endpoint. Defaults to the hosted KashFlow service.</summary>
    public Uri Endpoint { get; init; } = new(DefaultEndpoint);

    /// <summary>Maximum time to wait for a single call.</summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(100);

    /// <summary>
    /// When set, the client refuses every operation that can change KashFlow.
    /// Reads (Get, Is, and Search) still run.
    /// </summary>
    public bool ReadOnly { get; init; }
}
