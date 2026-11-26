using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace KashflowMCP.Rest;

/// <summary>
/// Minimal KashFlow REST client (session token + purchases bulk payments).
/// Password and memorable word are never written to logs.
/// </summary>
public sealed class KashFlowRestClient : IDisposable
{
    public const string DefaultBaseAddress = "https://api.kashflow.com/v2/";
    public static readonly TimeSpan DefaultIdleLifetime = TimeSpan.FromMinutes(55);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = null,
    };

    private readonly HttpClient _http;
    private readonly bool _ownsHttp;
    private readonly string _userName;
    private readonly string _password;
    private readonly string _memorableWord;
    private readonly TimeSpan _idleLifetime;
    private readonly object _gate = new();
    private string? _sessionToken;
    private DateTimeOffset _sessionValidUntil = DateTimeOffset.MinValue;
    private bool _disposed;

    public KashFlowRestClient(
        string userName,
        string password,
        string memorableWord,
        HttpMessageHandler? handler = null,
        Uri? baseAddress = null,
        TimeSpan? idleLifetime = null)
    {
        if (string.IsNullOrWhiteSpace(userName))
            throw new ArgumentException("userName is required.", nameof(userName));
        if (string.IsNullOrWhiteSpace(password))
            throw new ArgumentException("password is required.", nameof(password));
        if (string.IsNullOrWhiteSpace(memorableWord))
            throw new ArgumentException("memorableWord is required.", nameof(memorableWord));

        _userName = userName.Trim();
        _password = password;
        _memorableWord = memorableWord;
        _idleLifetime = idleLifetime ?? DefaultIdleLifetime;
        _ownsHttp = handler is null;
        _http = handler is null
            ? new HttpClient { BaseAddress = baseAddress ?? new Uri(DefaultBaseAddress) }
            : new HttpClient(handler, disposeHandler: false)
            {
                BaseAddress = baseAddress ?? new Uri(DefaultBaseAddress),
            };
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public string? CachedSessionToken
    {
        get
        {
            lock (_gate)
                return _sessionToken;
        }
    }

    public async Task EnsureSessionAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (HasFreshSession())
            return;

        lock (_gate)
        {
            // Another waiter may have refreshed while we were outside the lock.
        }

        if (HasFreshSession())
            return;

        await CreateSessionAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task InvalidateSessionAsync()
    {
        lock (_gate)
        {
            _sessionToken = null;
            _sessionValidUntil = DateTimeOffset.MinValue;
        }

        await Task.CompletedTask.ConfigureAwait(false);
    }

    public async Task<BulkPurchasePaymentResponse> PostPurchasesBulkPaymentAsync(
        BulkPurchasePaymentRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ThrowIfDisposed();
        await EnsureSessionAsync(cancellationToken).ConfigureAwait(false);

        using var response = await SendAuthenticatedAsync(
            HttpMethod.Post,
            "purchases/bulk/payments",
            request,
            cancellationToken).ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            await InvalidateSessionAsync().ConfigureAwait(false);
            await EnsureSessionAsync(cancellationToken).ConfigureAwait(false);
            using var retry = await SendAuthenticatedAsync(
                HttpMethod.Post,
                "purchases/bulk/payments",
                request,
                cancellationToken).ConfigureAwait(false);
            return await ReadSuccessAsync<BulkPurchasePaymentResponse>(retry, cancellationToken).ConfigureAwait(false);
        }

        return await ReadSuccessAsync<BulkPurchasePaymentResponse>(response, cancellationToken).ConfigureAwait(false);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        if (_ownsHttp)
            _http.Dispose();
    }

    internal static List<RestMemorableWordEntry> FillMemorableWord(
        string memorableWord,
        IReadOnlyList<RestMemorableWordEntry> positions)
    {
        ArgumentNullException.ThrowIfNull(positions);
        if (string.IsNullOrEmpty(memorableWord))
            throw new ArgumentException("Memorable word is required.");

        var filled = new List<RestMemorableWordEntry>(positions.Count);
        foreach (var entry in positions)
        {
            if (entry.Position < 1 || entry.Position > memorableWord.Length)
            {
                throw new InvalidOperationException(
                    $"KashFlow asked for memorable-word character position {entry.Position}, " +
                    "which is outside the configured memorable word length.");
            }

            filled.Add(new RestMemorableWordEntry
            {
                Position = entry.Position,
                Value = memorableWord[entry.Position - 1].ToString(),
            });
        }

        return filled;
    }

    private async Task CreateSessionAsync(CancellationToken cancellationToken)
    {
        using var post = new HttpRequestMessage(HttpMethod.Post, "sessiontoken")
        {
            Content = JsonContent.Create(
                new RestSessionPostRequest { UserName = _userName, Password = _password },
                options: JsonOptions),
        };

        using var challengeResponse = await _http.SendAsync(post, cancellationToken).ConfigureAwait(false);
        var challenge = await ReadSuccessAsync<RestSessionChallengeResponse>(challengeResponse, cancellationToken)
            .ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(challenge.TemporaryToken))
            throw new KashFlowRestException("KashFlow REST login did not return a temporary token.");
        if (challenge.MemorableWordList is null || challenge.MemorableWordList.Count == 0)
            throw new KashFlowRestException("KashFlow REST login did not return memorable-word positions.");

        var filled = FillMemorableWord(_memorableWord, challenge.MemorableWordList);
        using var put = new HttpRequestMessage(HttpMethod.Put, "sessiontoken")
        {
            Content = JsonContent.Create(
                new RestSessionPutRequest
                {
                    TemporaryToken = challenge.TemporaryToken,
                    MemorableWordList = filled,
                },
                options: JsonOptions),
        };

        using var tokenResponse = await _http.SendAsync(put, cancellationToken).ConfigureAwait(false);
        var token = await ReadSuccessAsync<RestSessionTokenResponse>(tokenResponse, cancellationToken)
            .ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(token.SessionToken))
            throw new KashFlowRestException("KashFlow REST login did not return a session token.");

        lock (_gate)
        {
            _sessionToken = token.SessionToken;
            _sessionValidUntil = DateTimeOffset.UtcNow.Add(_idleLifetime);
        }
    }

    private bool HasFreshSession()
    {
        lock (_gate)
            return !string.IsNullOrEmpty(_sessionToken) && DateTimeOffset.UtcNow < _sessionValidUntil;
    }

    private async Task<HttpResponseMessage> SendAuthenticatedAsync<T>(
        HttpMethod method,
        string relativeUrl,
        T body,
        CancellationToken cancellationToken)
    {
        string token;
        lock (_gate)
        {
            token = _sessionToken
                ?? throw new KashFlowRestException("No KashFlow REST session token is available.");
            // Touch the idle timer on use.
            _sessionValidUntil = DateTimeOffset.UtcNow.Add(_idleLifetime);
        }

        var request = new HttpRequestMessage(method, relativeUrl)
        {
            Content = JsonContent.Create(body, options: JsonOptions),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("KfToken", token);
        return await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<T> ReadSuccessAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var payload = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var message = TryParseError(payload) ?? $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}";
            throw new KashFlowRestException(message, (int)response.StatusCode);
        }

        if (string.IsNullOrWhiteSpace(payload))
            throw new KashFlowRestException("KashFlow REST returned an empty success body.");

        var parsed = JsonSerializer.Deserialize<T>(payload, JsonOptions);
        if (parsed is null)
            throw new KashFlowRestException("KashFlow REST returned an unreadable JSON body.");
        return parsed;
    }

    private static string? TryParseError(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
            return null;
        try
        {
            var error = JsonSerializer.Deserialize<RestErrorBody>(payload, JsonOptions);
            if (error is null)
                return null;
            if (!string.IsNullOrWhiteSpace(error.Message))
                return error.Message;
            if (!string.IsNullOrWhiteSpace(error.Error))
                return error.Error;
        }
        catch (JsonException)
        {
            // Fall through to a truncated raw body (never include secrets — caller never put them here).
        }

        return payload.Length <= 300 ? payload : payload[..300];
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}

public sealed class KashFlowRestException : Exception
{
    public KashFlowRestException(string message, int? statusCode = null)
        : base(message)
    {
        StatusCode = statusCode;
    }

    public int? StatusCode { get; }
}
