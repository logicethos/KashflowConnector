using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using ModelContextProtocol;

namespace KashflowMCP.Rest;

/// <summary>
/// KashFlow REST client (session token + purchases/bank helpers).
/// Password and memorable word are never written to logs.
/// </summary>
public sealed class KashFlowRestClient : IDisposable
{
    public const string DefaultBaseAddress = "https://api.kashflow.com/v2/";
    public static readonly TimeSpan DefaultIdleLifetime = TimeSpan.FromMinutes(55);
    public const long MaxUploadBytes = 10 * 1024 * 1024;

    internal static readonly JsonSerializerOptions JsonOptions = new()
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

    public Task<BulkPurchasePaymentResponse> PostPurchasesBulkPaymentAsync(
        BulkPurchasePaymentRequest request,
        CancellationToken cancellationToken = default)
        => SendJsonWithAuthRetryAsync<BulkPurchasePaymentRequest, BulkPurchasePaymentResponse>(
            HttpMethod.Post,
            "purchases/bulk/payments",
            request,
            cancellationToken);

    public Task<BulkPurchasePaymentResponse> GetPurchasesBulkPaymentAsync(
        int bulkPaymentNumber,
        CancellationToken cancellationToken = default)
    {
        if (bulkPaymentNumber <= 0)
            throw new ArgumentException("bulkPaymentNumber must be greater than 0.");
        return SendJsonWithAuthRetryAsync<object?, BulkPurchasePaymentResponse>(
            HttpMethod.Get,
            $"purchases/bulk/payments/{bulkPaymentNumber}",
            body: null,
            cancellationToken);
    }

    public Task<BulkPurchasePaymentResponse> PutPurchasesBulkPaymentAsync(
        int bulkPaymentNumber,
        BulkPurchasePaymentRequest request,
        CancellationToken cancellationToken = default)
    {
        if (bulkPaymentNumber <= 0)
            throw new ArgumentException("bulkPaymentNumber must be greater than 0.");
        ArgumentNullException.ThrowIfNull(request);
        return SendJsonWithAuthRetryAsync<BulkPurchasePaymentRequest, BulkPurchasePaymentResponse>(
            HttpMethod.Put,
            $"purchases/bulk/payments/{bulkPaymentNumber}",
            request,
            cancellationToken);
    }

    public Task DeletePurchasesBulkPaymentAsync(
        int bulkPaymentNumber,
        CancellationToken cancellationToken = default)
    {
        if (bulkPaymentNumber <= 0)
            throw new ArgumentException("bulkPaymentNumber must be greater than 0.");
        return SendEmptyWithAuthRetryAsync(
            HttpMethod.Delete,
            $"purchases/bulk/payments/{bulkPaymentNumber}",
            cancellationToken);
    }

    public Task<BankTransactionBulkAssignResponse> PutBankTransactionListAsync(
        int bankAccountId,
        BankTransactionBulkAssignRequest request,
        CancellationToken cancellationToken = default)
    {
        if (bankAccountId <= 0)
            throw new ArgumentException("bankAccountId must be greater than 0.");
        ArgumentNullException.ThrowIfNull(request);
        return SendJsonWithAuthRetryAsync<BankTransactionBulkAssignRequest, BankTransactionBulkAssignResponse>(
            HttpMethod.Put,
            $"bankaccounts/{bankAccountId}/transactionlist",
            request,
            cancellationToken);
    }

    public Task<PurchaseResponse> GetPurchaseAsync(
        int purchaseNumber,
        CancellationToken cancellationToken = default)
    {
        if (purchaseNumber <= 0)
            throw new ArgumentException("purchaseNumber must be greater than 0.");
        return SendJsonWithAuthRetryAsync<object?, PurchaseResponse>(
            HttpMethod.Get,
            $"purchases/{purchaseNumber}",
            body: null,
            cancellationToken);
    }

    public Task<PurchaseResponse> PutPurchaseAsync(
        int purchaseNumber,
        PurchaseRequest request,
        CancellationToken cancellationToken = default)
    {
        if (purchaseNumber <= 0)
            throw new ArgumentException("purchaseNumber must be greater than 0.");
        ArgumentNullException.ThrowIfNull(request);
        return SendJsonWithAuthRetryAsync<PurchaseRequest, PurchaseResponse>(
            HttpMethod.Put,
            $"purchases/{purchaseNumber}",
            request,
            cancellationToken);
    }

    public Task<JsonElement> GetBankAccountTransactionsAsync(
        int bankAccountId,
        BankTransactionListQuery? query = null,
        CancellationToken cancellationToken = default)
    {
        if (bankAccountId <= 0)
            throw new ArgumentException("bankAccountId must be greater than 0.");
        return SendJsonWithAuthRetryAsync<object?, JsonElement>(
            HttpMethod.Get,
            BuildBankTransactionsUrl(bankAccountId, query),
            body: null,
            cancellationToken);
    }

    public Task<JsonElement> GetBankFeedTransactionDetailsAsync(
        int bankAccountId,
        CancellationToken cancellationToken = default)
    {
        if (bankAccountId <= 0)
            throw new ArgumentException("bankAccountId must be greater than 0.");
        return SendJsonWithAuthRetryAsync<object?, JsonElement>(
            HttpMethod.Get,
            $"bankaccounts/{bankAccountId}/transactions/bfstransactions",
            body: null,
            cancellationToken);
    }

    public Task<JsonElement> GetBankReconciliationsAsync(
        int bankAccountId,
        int? page = null,
        int? perPage = null,
        int? sortOrder = null,
        CancellationToken cancellationToken = default)
    {
        if (bankAccountId <= 0)
            throw new ArgumentException("bankAccountId must be greater than 0.");
        var parts = new List<string>();
        if (page is int p)
            parts.Add("page=" + Uri.EscapeDataString(p.ToString(CultureInfo.InvariantCulture)));
        if (perPage is int pp)
            parts.Add("perPage=" + Uri.EscapeDataString(pp.ToString(CultureInfo.InvariantCulture)));
        if (sortOrder is int so)
            parts.Add("sortOrder=" + Uri.EscapeDataString(so.ToString(CultureInfo.InvariantCulture)));
        var url = parts.Count == 0
            ? $"bankaccounts/{bankAccountId}/reconciliations"
            : $"bankaccounts/{bankAccountId}/reconciliations?{string.Join("&", parts)}";
        return SendJsonWithAuthRetryAsync<object?, JsonElement>(
            HttpMethod.Get,
            url,
            body: null,
            cancellationToken);
    }

    public Task<BankReconciliationMetadata> GetBankReconciliationMetadataAsync(
        int bankAccountId,
        CancellationToken cancellationToken = default)
    {
        if (bankAccountId <= 0)
            throw new ArgumentException("bankAccountId must be greater than 0.");
        return SendJsonWithAuthRetryAsync<object?, BankReconciliationMetadata>(
            HttpMethod.Get,
            $"bankaccounts/{bankAccountId}/reconciliations/metadata",
            body: null,
            cancellationToken);
    }

    public Task<JsonElement> GetBankReconciliationAsync(
        int bankAccountId,
        int reconciliationId,
        bool? excludeTransactions = null,
        CancellationToken cancellationToken = default)
    {
        if (bankAccountId <= 0)
            throw new ArgumentException("bankAccountId must be greater than 0.");
        if (reconciliationId <= 0)
            throw new ArgumentException("reconciliationId must be greater than 0.");
        var url = $"bankaccounts/{bankAccountId}/reconciliations/{reconciliationId}";
        if (excludeTransactions is bool exclude)
            url += $"?excludetransations={exclude.ToString().ToLowerInvariant()}";
        return SendJsonWithAuthRetryAsync<object?, JsonElement>(
            HttpMethod.Get,
            url,
            body: null,
            cancellationToken);
    }

    public Task<JsonElement> ListPurchaseFilesAsync(
        int purchaseNumber,
        string? thumbnailSize = null,
        CancellationToken cancellationToken = default)
    {
        if (purchaseNumber <= 0)
            throw new ArgumentException("purchaseNumber must be greater than 0.");
        var url = $"purchases/{purchaseNumber}/files";
        if (!string.IsNullOrWhiteSpace(thumbnailSize))
            url += $"?thumbnailSize={Uri.EscapeDataString(thumbnailSize.Trim())}";
        return SendJsonWithAuthRetryAsync<object?, JsonElement>(
            HttpMethod.Get,
            url,
            body: null,
            cancellationToken);
    }

    public async Task<JsonElement> UploadAndAttachPurchaseFileAsync(
        int purchaseNumber,
        string fileName,
        byte[] content,
        string? contentType = null,
        CancellationToken cancellationToken = default)
    {
        if (purchaseNumber <= 0)
            throw new ArgumentException("purchaseNumber must be greater than 0.");
        ValidateUpload(fileName, content);

        // Swagger: POST /files?objectType=… (upload) then POST /{objectType}/{objectNumber}/files (attach).
        // Body shape is undocumented (empty consumes); multipart field name "file" is the usual ASP.NET pattern.
        var upload = await SendMultipartWithAuthRetryAsync(
            HttpMethod.Post,
            "files?objectType=purchases",
            fileName,
            content,
            contentType,
            cancellationToken).ConfigureAwait(false);

        var attachBody = BuildAttachBodyFromUpload(upload, fileName);
        return await SendJsonWithAuthRetryAsync<object, JsonElement>(
            HttpMethod.Post,
            $"purchases/{purchaseNumber}/files",
            attachBody,
            cancellationToken).ConfigureAwait(false);
    }

    public Task DeletePurchaseFileAsync(
        int purchaseNumber,
        string fileName,
        CancellationToken cancellationToken = default)
    {
        if (purchaseNumber <= 0)
            throw new ArgumentException("purchaseNumber must be greater than 0.");
        if (string.IsNullOrWhiteSpace(fileName))
            throw new ArgumentException("fileName is required.");
        return SendEmptyWithAuthRetryAsync(
            HttpMethod.Delete,
            $"purchases/{purchaseNumber}/file?fileName={Uri.EscapeDataString(fileName.Trim())}",
            cancellationToken);
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

    internal static void ValidateUpload(string fileName, byte[] content)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            throw new ArgumentException("fileName is required.");
        if (fileName.IndexOfAny(['/', '\\', '\0']) >= 0)
            throw new ArgumentException("fileName must be a bare file name, not a path.");
        ArgumentNullException.ThrowIfNull(content);
        if (content.Length == 0)
            throw new ArgumentException("File content is empty.");
        if (content.Length > MaxUploadBytes)
        {
            throw new ArgumentException(
                $"File is {content.Length} bytes; max upload size is {MaxUploadBytes} bytes (10 MiB).");
        }
    }

    internal static string BuildBankTransactionsUrl(int bankAccountId, BankTransactionListQuery? query)
    {
        var parts = new List<string>();
        if (query is not null)
        {
            if (query.Page is int page)
                parts.Add("page=" + Uri.EscapeDataString(page.ToString(CultureInfo.InvariantCulture)));
            if (query.PerPage is int perPage)
                parts.Add("perpage=" + Uri.EscapeDataString(perPage.ToString(CultureInfo.InvariantCulture)));
            if (query.StartDate is DateTime start)
                parts.Add("startdate=" + Uri.EscapeDataString(start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));
            if (query.EndDate is DateTime end)
                parts.Add("enddate=" + Uri.EscapeDataString(end.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));
            if (query.SortColumn is int sort)
                parts.Add("sortcolumn=" + Uri.EscapeDataString(sort.ToString(CultureInfo.InvariantCulture)));
            if (!string.IsNullOrWhiteSpace(query.Order))
                parts.Add("order=" + Uri.EscapeDataString(query.Order.Trim()));
            if (!string.IsNullOrWhiteSpace(query.FilterBy))
                parts.Add("filterby=" + Uri.EscapeDataString(query.FilterBy.Trim()));
            if (query.IncludeBulk is bool includeBulk)
                parts.Add("includebulk=" + (includeBulk ? "true" : "false"));
            if (!string.IsNullOrWhiteSpace(query.SearchText))
                parts.Add("searchtext=" + Uri.EscapeDataString(query.SearchText.Trim()));
            if (query.IncludeFeedMatched is bool includeFeed)
                parts.Add("includefeedmatched=" + (includeFeed ? "true" : "false"));
            if (query.IncludeJournals is bool includeJournals)
                parts.Add("includejournals=" + (includeJournals ? "true" : "false"));
            if (query.IncludeNominalCodeBankTransactions is bool includeNominal)
                parts.Add("includenominalcodebanktransactions=" + (includeNominal ? "true" : "false"));
            if (query.Reconciled is bool reconciled)
                parts.Add("reconciled=" + (reconciled ? "true" : "false"));
        }

        return parts.Count == 0
            ? $"bankaccounts/{bankAccountId}/transactions"
            : $"bankaccounts/{bankAccountId}/transactions?{string.Join("&", parts)}";
    }

    private static object BuildAttachBodyFromUpload(JsonElement upload, string fileName)
    {
        // Prefer echoing the upload response when it looks like an object; otherwise send a filename hint.
        if (upload.ValueKind == JsonValueKind.Object)
            return JsonSerializer.Deserialize<JsonElement>(upload.GetRawText());
        return new { FileName = fileName, Upload = upload };
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
        RestSessionChallengeResponse challenge;
        try
        {
            challenge = await ReadSuccessAsync<RestSessionChallengeResponse>(challengeResponse, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (KashFlowRestException ex)
        {
            throw EnrichLoginFailure(ex, stage: "POST /v2/sessiontoken");
        }

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
        RestSessionTokenResponse token;
        try
        {
            token = await ReadSuccessAsync<RestSessionTokenResponse>(tokenResponse, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (KashFlowRestException ex)
        {
            throw EnrichLoginFailure(ex, stage: "PUT /v2/sessiontoken");
        }

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

    private async Task<TResponse> SendJsonWithAuthRetryAsync<TRequest, TResponse>(
        HttpMethod method,
        string relativeUrl,
        TRequest? body,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        await EnsureSessionAsync(cancellationToken).ConfigureAwait(false);

        using var response = await SendAuthenticatedAsync(method, relativeUrl, body, cancellationToken)
            .ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            await InvalidateSessionAsync().ConfigureAwait(false);
            await EnsureSessionAsync(cancellationToken).ConfigureAwait(false);
            using var retry = await SendAuthenticatedAsync(method, relativeUrl, body, cancellationToken)
                .ConfigureAwait(false);
            return await ReadSuccessAsync<TResponse>(retry, cancellationToken).ConfigureAwait(false);
        }

        return await ReadSuccessAsync<TResponse>(response, cancellationToken).ConfigureAwait(false);
    }

    private async Task SendEmptyWithAuthRetryAsync(
        HttpMethod method,
        string relativeUrl,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        await EnsureSessionAsync(cancellationToken).ConfigureAwait(false);

        using var response = await SendAuthenticatedAsync<object?>(method, relativeUrl, null, cancellationToken)
            .ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            await InvalidateSessionAsync().ConfigureAwait(false);
            await EnsureSessionAsync(cancellationToken).ConfigureAwait(false);
            using var retry = await SendAuthenticatedAsync<object?>(method, relativeUrl, null, cancellationToken)
                .ConfigureAwait(false);
            await EnsureSuccessOrEmptyAsync(retry, cancellationToken).ConfigureAwait(false);
            return;
        }

        await EnsureSuccessOrEmptyAsync(response, cancellationToken).ConfigureAwait(false);
    }

    private async Task<JsonElement> SendMultipartWithAuthRetryAsync(
        HttpMethod method,
        string relativeUrl,
        string fileName,
        byte[] content,
        string? contentType,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        await EnsureSessionAsync(cancellationToken).ConfigureAwait(false);

        using var response = await SendAuthenticatedMultipartAsync(
            method, relativeUrl, fileName, content, contentType, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            await InvalidateSessionAsync().ConfigureAwait(false);
            await EnsureSessionAsync(cancellationToken).ConfigureAwait(false);
            using var retry = await SendAuthenticatedMultipartAsync(
                method, relativeUrl, fileName, content, contentType, cancellationToken).ConfigureAwait(false);
            return await ReadSuccessAsync<JsonElement>(retry, cancellationToken).ConfigureAwait(false);
        }

        return await ReadSuccessAsync<JsonElement>(response, cancellationToken).ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> SendAuthenticatedAsync<T>(
        HttpMethod method,
        string relativeUrl,
        T? body,
        CancellationToken cancellationToken)
    {
        string token;
        lock (_gate)
        {
            token = _sessionToken
                ?? throw new KashFlowRestException("No KashFlow REST session token is available.");
            _sessionValidUntil = DateTimeOffset.UtcNow.Add(_idleLifetime);
        }

        var request = new HttpRequestMessage(method, relativeUrl);
        if (body is not null || method == HttpMethod.Post || method == HttpMethod.Put)
        {
            // GET/DELETE with no body stay body-less. PUT/POST with null still send empty JSON object only when body typed as object; here null means no content.
            if (body is not null)
                request.Content = JsonContent.Create(body, options: JsonOptions);
        }

        request.Headers.Authorization = new AuthenticationHeaderValue("KfToken", token);
        return await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> SendAuthenticatedMultipartAsync(
        HttpMethod method,
        string relativeUrl,
        string fileName,
        byte[] content,
        string? contentType,
        CancellationToken cancellationToken)
    {
        string token;
        lock (_gate)
        {
            token = _sessionToken
                ?? throw new KashFlowRestException("No KashFlow REST session token is available.");
            _sessionValidUntil = DateTimeOffset.UtcNow.Add(_idleLifetime);
        }

        var multipart = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(content);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(
            string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType.Trim());
        multipart.Add(fileContent, "file", fileName);

        var request = new HttpRequestMessage(method, relativeUrl) { Content = multipart };
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

        if (typeof(T) == typeof(JsonElement))
        {
            if (string.IsNullOrWhiteSpace(payload))
            {
                object empty = default(JsonElement);
                return (T)empty;
            }

            using var doc = JsonDocument.Parse(payload);
            object element = doc.RootElement.Clone();
            return (T)element;
        }

        if (string.IsNullOrWhiteSpace(payload))
            throw new KashFlowRestException("KashFlow REST returned an empty success body.");

        var parsed = JsonSerializer.Deserialize<T>(payload, JsonOptions);
        if (parsed is null)
            throw new KashFlowRestException("KashFlow REST returned an unreadable JSON body.");
        return parsed;
    }

    private static async Task EnsureSuccessOrEmptyAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var payload = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (response.IsSuccessStatusCode)
            return;

        var message = TryParseError(payload) ?? $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}";
        throw new KashFlowRestException(message, (int)response.StatusCode);
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

    /// <summary>
    /// Keeps KashFlow's error text (no secrets) and adds a short REST-vs-SOAP credential hint on login failures.
    /// </summary>
    internal static KashFlowRestException EnrichLoginFailure(KashFlowRestException ex, string stage)
    {
        ArgumentNullException.ThrowIfNull(ex);
        var detail = string.IsNullOrWhiteSpace(ex.Message) ? "login failed" : ex.Message.Trim();
        var status = ex.StatusCode is int code ? $" HTTP {code}" : "";
        var hint =
            " SOAP API UserName/Password often differ from the web login REST uses; " +
            "set KashFlow__RestUserName / KashFlow__RestPassword (or KASHFLOW_REST_USERNAME / KASHFLOW_REST_PASSWORD) when needed. " +
            "Passwords are never logged.";
        return new KashFlowRestException(
            $"KashFlow REST {stage} failed{status}: {detail}.{hint}",
            ex.StatusCode);
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}

/// <summary>
/// REST API failure. Derives from <see cref="McpException"/> so the MCP host surfaces the message
/// (including KashFlow's error text) instead of a generic "An error occurred invoking …".
/// </summary>
public sealed class KashFlowRestException : McpException
{
    public KashFlowRestException(string message, int? statusCode = null)
        : base(message)
    {
        StatusCode = statusCode;
    }

    public int? StatusCode { get; }
}
