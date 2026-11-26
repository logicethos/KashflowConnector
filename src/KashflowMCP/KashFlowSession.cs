using KashFlow;
using KashflowMCP.Rest;
using Microsoft.Extensions.Configuration;
using ModelContextProtocol;

namespace KashflowMCP;

/// <summary>
/// Lazily opens the SOAP client (and optional REST client) on the first tool call so
/// <c>tools/list</c> can run before a marketplace reviewer has saved credentials.
/// </summary>
public sealed class KashFlowSession : IDisposable
{
    private readonly object _gate = new();
    private readonly KashFlowClientOptions? _options;
    private readonly string? _restUserName;
    private readonly string? _restPassword;
    private readonly string? _memorableWord;
    private readonly Uri _restBaseAddress;
    private KashFlowClient? _client;
    private KashFlowRestClient? _restClient;

    private KashFlowSession(
        bool readOnly,
        bool credentialsConfigured,
        bool restConfigured,
        KashFlowClientOptions? options,
        string? restUserName,
        string? restPassword,
        string? memorableWord,
        Uri restBaseAddress)
    {
        ReadOnly = readOnly;
        CredentialsConfigured = credentialsConfigured;
        RestConfigured = restConfigured;
        _options = options;
        _restUserName = restUserName;
        _restPassword = restPassword;
        _memorableWord = memorableWord;
        _restBaseAddress = restBaseAddress;
    }

    public bool ReadOnly { get; }

    public bool CredentialsConfigured { get; }

    /// <summary>True when SOAP credentials and a memorable word are both configured.</summary>
    public bool RestConfigured { get; }

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
                        "(KashFlow SOAP API credentials) before this tool can run. " +
                        "Listing tools does not require credentials. The password is never logged.");
                }

                return _client = new KashFlowClient(_options);
            }
        }
    }

    /// <summary>
    /// REST client when <see cref="RestConfigured"/>; otherwise null.
    /// Created lazily; password and memorable word are never logged.
    /// </summary>
    public KashFlowRestClient? RestClient
    {
        get
        {
            if (!RestConfigured
                || string.IsNullOrEmpty(_restUserName)
                || string.IsNullOrEmpty(_restPassword)
                || string.IsNullOrEmpty(_memorableWord))
                return null;

            if (_restClient is not null)
                return _restClient;

            lock (_gate)
            {
                return _restClient ??= new KashFlowRestClient(
                    _restUserName,
                    _restPassword,
                    _memorableWord,
                    baseAddress: _restBaseAddress);
            }
        }
    }

    /// <summary>
    /// Username used for REST session login (optional <c>RestUserName</c>, else SOAP username).
    /// Exposed for tests; never logged by the server.
    /// </summary>
    internal string? RestUserNameForTests => _restUserName;

    public static KashFlowSession FromConfiguration(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var section = configuration.GetSection("KashFlow");
        var readOnly = ParseReadOnly(section["ReadOnly"]);
        var userName = section["UserName"];
        var password = section["Password"];
        var memorableWord = FirstNonEmpty(
            section["MemorableWord"],
            configuration["KASHFLOW_MEMORABLE_WORD"]);
        // REST login often needs the web username/password; SOAP API creds can differ.
        var restUserName = FirstNonEmpty(
            section["RestUserName"],
            configuration["KASHFLOW_REST_USERNAME"],
            userName);
        var restPassword = FirstNonEmpty(
            section["RestPassword"],
            configuration["KASHFLOW_REST_PASSWORD"],
            password);

        var restBase = new Uri(KashFlowRestClient.DefaultBaseAddress);
        if (Uri.TryCreate(section["RestBaseAddress"], UriKind.Absolute, out var parsedRest))
            restBase = parsedRest.AbsoluteUri.EndsWith('/')
                ? parsedRest
                : new Uri(parsedRest.AbsoluteUri.TrimEnd('/') + "/");

        if (IsUnset(userName) || IsUnset(password))
        {
            return new KashFlowSession(
                readOnly,
                credentialsConfigured: false,
                restConfigured: false,
                options: null,
                restUserName: null,
                restPassword: null,
                memorableWord: null,
                restBaseAddress: restBase);
        }

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

        var restConfigured = !IsUnset(memorableWord);
        return new KashFlowSession(
            readOnly,
            credentialsConfigured: true,
            restConfigured,
            options,
            restConfigured ? restUserName!.Trim() : null,
            restConfigured ? restPassword : null,
            restConfigured ? memorableWord!.Trim() : null,
            restBase);
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
            _restClient?.Dispose();
            _restClient = null;
        }
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!IsUnset(value))
                return value;
        }

        return null;
    }

    private static bool IsUnset(string? value)
        => string.IsNullOrWhiteSpace(value)
            || (value.StartsWith("${", StringComparison.Ordinal) && value.EndsWith('}'));
}
