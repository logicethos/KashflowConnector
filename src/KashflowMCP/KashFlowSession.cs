using KashFlow;
using Microsoft.Extensions.Configuration;
using ModelContextProtocol;

namespace KashflowMCP;

/// <summary>
/// Lazily opens the SOAP client on the first tool call so <c>tools/list</c> can run
/// before a marketplace reviewer has saved credentials.
/// </summary>
public sealed class KashFlowSession : IDisposable
{
    private readonly object _gate = new();
    private readonly KashFlowClientOptions? _options;
    private KashFlowClient? _client;

    private KashFlowSession(bool readOnly, bool credentialsConfigured, KashFlowClientOptions? options)
    {
        ReadOnly = readOnly;
        CredentialsConfigured = credentialsConfigured;
        _options = options;
    }

    public bool ReadOnly { get; }

    public bool CredentialsConfigured { get; }

    public KashFlowClient Client
    {
        get
        {
            if (_client is not null)
                return _client;

            lock (_gate)
            {
                if (_client is not null)
                    return _client;

                if (_options is null)
                {
                    throw new McpException(
                        "KashflowMCP needs KashFlow__UserName and KashFlow__Password " +
                        "(KashFlow SOAP API credentials, not the REST memorable word) before this tool can run. " +
                        "Listing tools does not require credentials. The password is never logged.");
                }

                return _client = new KashFlowClient(_options);
            }
        }
    }

    public static KashFlowSession FromConfiguration(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var section = configuration.GetSection("KashFlow");
        var readOnly = ParseReadOnly(section["ReadOnly"]);
        var userName = section["UserName"];
        var password = section["Password"];

        if (IsUnset(userName) || IsUnset(password))
            return new KashFlowSession(readOnly, credentialsConfigured: false, options: null);

        var timeoutSeconds = 100;
        if (int.TryParse(section["TimeoutSeconds"], out var parsedTimeout) && parsedTimeout > 0)
            timeoutSeconds = parsedTimeout;

        var endpoint = new Uri(KashFlowClientOptions.DefaultEndpoint);
        if (Uri.TryCreate(section["Endpoint"], UriKind.Absolute, out var parsedEndpoint))
            endpoint = parsedEndpoint;

        var options = new KashFlowClientOptions(userName!, password!)
        {
            ReadOnly = readOnly,
            Timeout = TimeSpan.FromSeconds(timeoutSeconds),
            Endpoint = endpoint,
        };

        return new KashFlowSession(readOnly, credentialsConfigured: true, options);
    }

    public static bool ParseReadOnly(string? value)
    {
        if (IsUnset(value))
            return true;

        value = value!.Trim();
        if (bool.TryParse(value, out var parsed))
            return parsed;

        if (value.Equals("1", StringComparison.OrdinalIgnoreCase)
            || value.Equals("yes", StringComparison.OrdinalIgnoreCase)
            || value.Equals("y", StringComparison.OrdinalIgnoreCase))
            return true;

        if (value.Equals("0", StringComparison.OrdinalIgnoreCase)
            || value.Equals("no", StringComparison.OrdinalIgnoreCase)
            || value.Equals("n", StringComparison.OrdinalIgnoreCase))
            return false;

        // Unknown values stay read-only. That is the safe default.
        return true;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _client?.Dispose();
            _client = null;
        }
    }

    private static bool IsUnset(string? value)
        => string.IsNullOrWhiteSpace(value)
            || (value.StartsWith("${", StringComparison.Ordinal) && value.EndsWith('}'));
}
