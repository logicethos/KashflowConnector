using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Serialization;
using KashFlow.Messages;

namespace KashFlow;

/// <summary>
/// Typed client for every operation on the KashFlow SOAP service.
/// </summary>
/// <remarks>
/// Money values are <see cref="decimal"/>. Instants and calendar dates are <see cref="DateTime"/>.
/// Unset dates on data objects are omitted, so a new invoice does not send year 1 for a date you left blank.
/// Legacy receipt methods (those without a "2" suffix) use the invoice document shape, because that is the published contract.
/// The client uses <see cref="XmlSerializer"/> and is not safe for trimmed single-file publishing.
/// </remarks>
/// <example>
/// <code>
/// await using var kashflow = new KashFlowClient("api-user", "api-password");
/// IReadOnlyList&lt;Supplier&gt; suppliers = await kashflow.GetSuppliersAsync();
/// </code>
/// </example>
public sealed partial class KashFlowClient : IDisposable, IAsyncDisposable
{
    private static readonly XNamespace SoapNamespace = "http://schemas.xmlsoap.org/soap/envelope/";
    private static readonly ConcurrentDictionary<Type, XmlSerializer> Serializers = new();

    private readonly string _userName;
    private readonly string _password;
    private readonly HttpClient _httpClient;
    private readonly bool _disposeClient;
    private readonly Uri _endpoint;
    private readonly TimeSpan _timeout;
    private readonly bool _readOnly;

    public KashFlowClient(string userName, string password)
        : this(new KashFlowClientOptions(userName, password), httpClient: null)
    {
    }

    public KashFlowClient(KashFlowClientOptions options, HttpClient? httpClient = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!options.Endpoint.IsAbsoluteUri)
            throw new ArgumentException("The KashFlow endpoint must be an absolute URI.", nameof(options));
        if (options.Timeout <= TimeSpan.Zero)
            throw new ArgumentException("Timeout must be positive.", nameof(options));

        _userName = options.UserName;
        _password = options.Password;
        _endpoint = options.Endpoint;
        _timeout = options.Timeout;
        _readOnly = options.ReadOnly;
        _disposeClient = httpClient is null;
        _httpClient = httpClient ?? CreateHttpClient();
    }

    private static bool IsReadOperation(string operation)
        => operation.StartsWith("Get", StringComparison.Ordinal)
            || operation.StartsWith("Is", StringComparison.Ordinal)
            || operation.StartsWith("is", StringComparison.Ordinal)
            || operation.StartsWith("Search", StringComparison.Ordinal);

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("KashFlowClient", "1.0"));
        return client;
    }

    public void Dispose()
    {
        if (_disposeClient)
            _httpClient.Dispose();
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    private async Task<TResult> SendAsync<TRequest, TResponse, TResult>(
        string operation,
        TRequest request,
        Func<TResponse, TResult> project,
        CancellationToken cancellationToken)
        where TRequest : class
        where TResponse : class, IKashFlowStatus
    {
        var response = await PostAsync<TRequest, TResponse>(operation, request, cancellationToken).ConfigureAwait(false);
        EnsureOk(operation, response);
        return project(response);
    }

    private async Task SendAsync<TRequest, TResponse>(
        string operation,
        TRequest request,
        CancellationToken cancellationToken)
        where TRequest : class
        where TResponse : class, IKashFlowStatus
    {
        var response = await PostAsync<TRequest, TResponse>(operation, request, cancellationToken).ConfigureAwait(false);
        EnsureOk(operation, response);
    }

    private async Task<TResponse> PostAsync<TRequest, TResponse>(
        string operation,
        TRequest request,
        CancellationToken cancellationToken)
        where TRequest : class
        where TResponse : class
    {
        if (_readOnly && !IsReadOperation(operation))
            throw new KashFlowException(operation, $"KashFlow {operation} is blocked because this connection is read-only.");

        request = PrepareReceiptCurrency(request);

        using var timeout = new CancellationTokenSource(_timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, _endpoint)
        {
            Content = new StringContent(CreateEnvelope(request), Encoding.UTF8, "text/xml"),
        };
        httpRequest.Headers.TryAddWithoutValidation("SOAPAction", $"\"KashFlow/{operation}\"");
        httpRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/xml"));

        HttpResponseMessage httpResponse;
        try
        {
            httpResponse = await _httpClient.SendAsync(httpRequest, linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"KashFlow {operation} timed out after {_timeout}.");
        }
        catch (HttpRequestException ex)
        {
            throw new KashFlowException(operation, $"KashFlow {operation} could not be sent.", ex);
        }

        using (httpResponse)
        {
            var body = await httpResponse.Content.ReadAsStringAsync(linked.Token).ConfigureAwait(false);
            return ReadResponse<TResponse>(operation, (int)httpResponse.StatusCode, body);
        }
    }

    /// <summary>
    /// Purchase-receipt SOAP line amounts are home currency. Copy the caller's document-currency
    /// receipt and send GBP line rates. The caller's object is not modified. Sales invoices are left alone.
    /// </summary>
    private static TRequest PrepareReceiptCurrency<TRequest>(TRequest request)
        where TRequest : class
    {
        switch (request)
        {
            case InsertReceipt2Request insert when insert.Receipt is not null:
                insert.Receipt = ReceiptCurrency.ToSoap(insert.Receipt);
                break;
            case UpdateReceipt2Request update when update.Receipt is not null:
                update.Receipt = ReceiptCurrency.ToSoap(update.Receipt);
                break;
            case InsertReceiptRequest insert when insert.Inv is not null:
                insert.Inv = ReceiptCurrency.ToSoap(insert.Inv);
                break;
            case UpdateReceiptRequest update when update.Receipt is not null:
                update.Receipt = ReceiptCurrency.ToSoap(update.Receipt);
                break;
            case UpdateReceiptHeaderRequest update when update.Receipt is not null:
                update.Receipt = ReceiptCurrency.ToSoap(update.Receipt);
                break;
            case InsertReceipt_TypeDefinedRequest insert when insert.Inv_TD is not null:
                insert.Inv_TD = ReceiptCurrency.ToSoap(insert.Inv_TD);
                break;
            case UpdateReceipt_TypeDefinedRequest update when update.Receipt_TD is not null:
                update.Receipt_TD = ReceiptCurrency.ToSoap(update.Receipt_TD);
                break;
        }

        return request;
    }

    private static void EnsureOk(string operation, IKashFlowStatus response)
    {
        if (string.IsNullOrWhiteSpace(response.Status))
            return;

        if (response.Status.Trim().Equals("OK", StringComparison.OrdinalIgnoreCase))
            return;

        throw KashFlowException.FromStatus(operation, response.Status.Trim(), response.StatusDetail);
    }

    private static TResponse ReadResponse<TResponse>(string operation, int statusCode, string body)
        where TResponse : class
    {
        if (string.IsNullOrWhiteSpace(body))
            throw new KashFlowException(operation, $"KashFlow {operation} returned HTTP {statusCode} with an empty body.");

        XDocument document;
        try
        {
            document = XDocument.Parse(body, LoadOptions.None);
        }
        catch (XmlException ex)
        {
            throw new KashFlowException(operation, $"KashFlow {operation} returned HTTP {statusCode} with a body that is not XML. {Preview(body)}", ex);
        }

        var soapBody = document.Root?.Element(SoapNamespace + "Body");
        if (soapBody is null)
            throw new KashFlowException(operation, $"KashFlow {operation} returned HTTP {statusCode} without a SOAP body. {Preview(body)}");

        var payload = soapBody.Elements().FirstOrDefault();
        if (payload is null)
            throw new KashFlowException(operation, $"KashFlow {operation} returned HTTP {statusCode} with an empty SOAP body.");

        if (payload.Name.LocalName == "Fault")
        {
            var faultCode = FirstText(payload, "faultcode");
            var faultString = FirstText(payload, "faultstring") ?? FirstText(payload, "Reason") ?? "SOAP fault";
            throw new KashFlowException(operation, $"KashFlow {operation} fault: {faultString}", null)
            {
                FaultCode = faultCode,
            };
        }

        try
        {
            var serializer = Serializers.GetOrAdd(typeof(TResponse), static type => new XmlSerializer(type));
            using var reader = payload.CreateReader();
            if (serializer.Deserialize(reader) is not TResponse response)
                throw new KashFlowException(operation, $"KashFlow {operation} returned an empty {payload.Name.LocalName} payload.");

            return response;
        }
        catch (InvalidOperationException ex)
        {
            throw new KashFlowException(
                operation,
                $"KashFlow {operation} returned {payload.Name.LocalName}, which does not match the SOAP contract. {Innermost(ex)}",
                ex);
        }
    }

    private static string CreateEnvelope(object request)
    {
        var serializer = Serializers.GetOrAdd(request.GetType(), static type => new XmlSerializer(type));
        var payload = new StringBuilder();
        var settings = new XmlWriterSettings
        {
            OmitXmlDeclaration = true,
            Indent = false,
            Encoding = Encoding.UTF8,
        };

        using (var writer = XmlWriter.Create(payload, settings))
        {
            var namespaces = new XmlSerializerNamespaces();
            namespaces.Add("", "KashFlow");
            serializer.Serialize(writer, request, namespaces);
        }

        return $"""
            <?xml version="1.0" encoding="utf-8"?>
            <soap:Envelope xmlns:soap="http://schemas.xmlsoap.org/soap/envelope/" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xmlns:xsd="http://www.w3.org/2001/XMLSchema">
              <soap:Body>
                {payload}
              </soap:Body>
            </soap:Envelope>
            """;
    }

    private static string? FirstText(XElement element, string localName)
        => element.Elements().FirstOrDefault(child => child.Name.LocalName == localName)?.Value;

    private static string Innermost(Exception exception)
    {
        var current = exception;
        while (current.InnerException is not null)
            current = current.InnerException;
        return current.Message;
    }

    private static string Preview(string body)
    {
        var trimmed = body.Trim();
        return trimmed.Length <= 500 ? trimmed : trimmed[..500];
    }
}
