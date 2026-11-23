using System.Net;
using System.Reflection;
using System.Text;
using System.Xml.Linq;
using Xunit;

namespace KashFlow.Tests;

public class KashFlowClientTests
{
    private static readonly XNamespace Service = "KashFlow";
    private static readonly XNamespace Soap = "http://schemas.xmlsoap.org/soap/envelope/";
    private static readonly XNamespace Xsi = "http://www.w3.org/2001/XMLSchema-instance";

    [Fact]
    public void CoversEverySoapOperation()
    {
        var methods = typeof(KashFlowClient)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .Where(method => method.Name.EndsWith("Async", StringComparison.Ordinal)
                && method.Name != nameof(KashFlowClient.DisposeAsync))
            .Select(method => method.Name[..^"Async".Length])
            .ToHashSet(StringComparer.Ordinal);

        Assert.Equal(192, KashFlowClient.Operations.Count);
        Assert.Equal(KashFlowClient.Operations.OrderBy(name => name, StringComparer.Ordinal), methods.OrderBy(name => name, StringComparer.Ordinal));
    }

    [Fact]
    public void MoneyDatesAndNominalsUseExactClrTypes()
    {
        Assert.Equal(typeof(decimal), typeof(Invoice).GetProperty(nameof(Invoice.NetAmount))!.PropertyType);
        Assert.Equal(typeof(decimal), typeof(InvoiceLine).GetProperty(nameof(InvoiceLine.Quantity))!.PropertyType);
        Assert.Equal(typeof(decimal), typeof(ReceiptLine2).GetProperty(nameof(ReceiptLine2.ImportDuty))!.PropertyType);
        Assert.Equal(typeof(DateTime?), typeof(Invoice).GetProperty(nameof(Invoice.InvoiceDate))!.PropertyType);
        Assert.Equal(typeof(DateTime?), typeof(Supplier).GetProperty(nameof(Supplier.Created))!.PropertyType);
        Assert.Equal(typeof(int), typeof(NominalCode).GetProperty(nameof(NominalCode.Code))!.PropertyType);
        Assert.Equal(typeof(int), typeof(NominalCode).GetProperty(nameof(NominalCode.Id))!.PropertyType);
        Assert.Equal(typeof(KashFlowAnyList<InvoiceLine>), typeof(Invoice).GetProperty(nameof(Invoice.Lines))!.PropertyType);
        Assert.Equal(typeof(KashFlowAnyList<ReceiptLine2>), typeof(Receipt2).GetProperty(nameof(Receipt2.Lines))!.PropertyType);
        Assert.Equal(typeof(KashFlowAnyList<JournalLine>), typeof(JournalEntry).GetProperty(nameof(JournalEntry.Lines))!.PropertyType);
        Assert.Equal(typeof(KashFlowAnyList<NominalCode>), typeof(BalanceSheet).GetProperty(nameof(BalanceSheet.FixedAssets))!.PropertyType);
        Assert.Equal(typeof(TradeBorder?), typeof(Supplier2).GetProperty(nameof(Supplier2.TradeBorderType))!.PropertyType);
        Assert.Equal(typeof(decimal), typeof(Supplier).GetProperty(nameof(Supplier.TradeBorderType))!.PropertyType);
        Assert.Equal(typeof(byte[]), typeof(KashFlowClient).GetMethod(nameof(KashFlowClient.SageFileAsync))!.GetParameters()[0].ParameterType);
    }

    [Fact]
    public void LegacyReceiptMethodsUseTheInvoiceContract()
    {
        Assert.Equal(typeof(Task<Invoice?>), typeof(KashFlowClient).GetMethod(nameof(KashFlowClient.GetReceiptAsync))!.ReturnType);
        Assert.Equal(typeof(Task<KashFlowPage<Invoice>>), typeof(KashFlowClient).GetMethod(nameof(KashFlowClient.GetReceiptsWithPagingAsync))!.ReturnType);
        Assert.Equal(typeof(Task<int>), typeof(KashFlowClient).GetMethod(nameof(KashFlowClient.InsertReceipt2Async))!.ReturnType);
        Assert.Equal(typeof(Receipt2), typeof(KashFlowClient).GetMethod(nameof(KashFlowClient.InsertReceipt2Async))!.GetParameters()[0].ParameterType);
    }

    [Fact]
    public async Task InsertReceipt2_WritesQualifiedEnvelopeAndTypedLines()
    {
        var handler = new RecordingHandler(_ => Envelope("""
            <InsertReceipt2Response xmlns="KashFlow">
              <InsertReceipt2Result>42</InsertReceipt2Result>
              <Status>OK</Status>
            </InsertReceipt2Response>
            """));
        await using var client = Client(handler);

        var id = await client.InsertReceipt2Async(new Receipt2
        {
            ReceiptDate = new DateTime(2024, 3, 15),
            SupplierId = 7,
            CurrencyCode = "GBP",
            NetAmount = 10.5m,
            VATAmount = 2.1m,
            Lines =
            [
                new ReceiptLine2
                {
                    Quantity = 1,
                    Description = "Paper & clips",
                    Rate = 10.5m,
                    ChargeType = 5000,
                    VatRate = 20m,
                    VatAmount = 2.1m,
                },
            ],
        });

        Assert.Equal(42, id);
        Assert.Equal("\"KashFlow/InsertReceipt2\"", handler.SoapAction);

        var document = XDocument.Parse(handler.Body);
        Assert.Equal(Soap + "Envelope", document.Root!.Name);
        Assert.Equal("user", document.Descendants(Service + "UserName").Single().Value);
        Assert.Equal("secret-password", document.Descendants(Service + "Password").Single().Value);
        Assert.Equal("GBP", document.Descendants(Service + "CurrencyCode").Single().Value);
        Assert.Equal("10.5", document.Descendants(Service + "NetAmount").Single().Value);
        Assert.Equal("Paper & clips", document.Descendants(Service + "Description").Single().Value);
        Assert.Contains("2024-03-15", document.Descendants(Service + "ReceiptDate").Single().Value, StringComparison.Ordinal);
        Assert.Empty(document.Descendants(Service + "Created"));

        var line = document.Descendants(Service + "anyType").Single();
        var typeName = line.Attribute(Xsi + "type")?.Value;
        Assert.NotNull(typeName);
        Assert.Contains("ReceiptLine2", typeName, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InsertSupplier_OmitsUnsetDatesAndWritesLegacyBorderAsDecimal()
    {
        var handler = new RecordingHandler(_ => Envelope("""
            <InsertSupplierResponse xmlns="KashFlow">
              <InsertSupplierResult>9</InsertSupplierResult>
              <Status>OK</Status>
            </InsertSupplierResponse>
            """));
        await using var client = Client(handler);

        var id = await client.InsertSupplierAsync(new Supplier
        {
            Name = "Acme",
            Code = "ACME",
            PaymentTerms = 30,
            TradeBorderType = 1,
        });

        Assert.Equal(9, id);
        var document = XDocument.Parse(handler.Body);
        Assert.Equal("Acme", document.Descendants(Service + "Name").Single().Value);
        Assert.Equal("1", document.Descendants(Service + "TradeBorderType").Single().Value);
        Assert.Empty(document.Descendants(Service + "Created"));
        Assert.Empty(document.Descendants(Service + "Updated"));
    }

    [Fact]
    public async Task InsertSupplier2_WritesNamedTradeBorder()
    {
        var handler = new RecordingHandler(_ => Envelope("""
            <InsertSupplier2Response xmlns="KashFlow">
              <InsertSupplier2Result>4</InsertSupplier2Result>
              <Status>OK</Status>
            </InsertSupplier2Response>
            """));
        await using var client = Client(handler);

        await client.InsertSupplier2Async(new Supplier2 { Name = "EU Parts", TradeBorderType = TradeBorder.EC });

        var border = XDocument.Parse(handler.Body).Descendants(Service + "TradeBorderType").Single();
        Assert.Equal("EC", border.Value);
    }

    [Fact]
    public async Task GetSuppliers_ReadsIdsDatesAndDecimals()
    {
        var handler = new RecordingHandler(_ => Envelope("""
            <GetSuppliersResponse xmlns="KashFlow">
              <GetSuppliersResult>
                <Supplier>
                  <SupplierID>3</SupplierID>
                  <Name>Acme</Name>
                  <Created>2024-01-02T00:00:00</Created>
                  <Updated>2024-01-03T00:00:00</Updated>
                  <EC>1</EC>
                  <CurrencyID>1</CurrencyID>
                  <PaymentTerms>30</PaymentTerms>
                  <TradeBorderType>2.5</TradeBorderType>
                </Supplier>
              </GetSuppliersResult>
              <Status>OK</Status>
              <StatusDetail></StatusDetail>
            </GetSuppliersResponse>
            """));
        await using var client = Client(handler);

        var suppliers = await client.GetSuppliersAsync();

        var supplier = Assert.Single(suppliers);
        Assert.Equal(3, supplier.SupplierID);
        Assert.Equal("Acme", supplier.Name);
        Assert.Equal(new DateTime(2024, 1, 2), supplier.Created);
        Assert.Equal(1, supplier.EC);
        Assert.Equal(2.5m, supplier.TradeBorderType);
    }

    [Fact]
    public async Task GetNominalCodes_ReadsNumericCodeAndLowerCaseId()
    {
        var handler = new RecordingHandler(_ => Envelope("""
            <GetNominalCodesResponse xmlns="KashFlow">
              <GetNominalCodesResult>
                <NominalCode>
                  <id>10</id>
                  <Code>5000</Code>
                  <Name>Purchases</Name>
                  <debit>12.34</debit>
                  <credit>0</credit>
                  <balance>12.34</balance>
                </NominalCode>
              </GetNominalCodesResult>
              <Status>ok</Status>
            </GetNominalCodesResponse>
            """));
        await using var client = Client(handler);

        var code = Assert.Single(await client.GetNominalCodesAsync());

        Assert.Equal(10, code.Id);
        Assert.Equal(5000, code.Code);
        Assert.Equal(12.34m, code.Debit);
    }

    [Fact]
    public async Task GetInvoiceByID_ReadsPolymorphicLines()
    {
        var handler = new RecordingHandler(_ => Envelope("""
            <GetInvoiceByIDResponse xmlns="KashFlow" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">
              <GetInvoiceByIDResult>
                <InvoiceDBID>1</InvoiceDBID>
                <InvoiceNumber>100</InvoiceNumber>
                <Lines>
                  <anyType xsi:type="InvoiceLine">
                    <Quantity>2</Quantity>
                    <Description>Work</Description>
                    <Rate>15.25</Rate>
                    <ChargeType>4000</ChargeType>
                    <VatRate>20</VatRate>
                    <VatAmount>6.10</VatAmount>
                    <ProductID>0</ProductID>
                    <Sort>1</Sort>
                    <ProjID>0</ProjID>
                    <LineID>9</LineID>
                  </anyType>
                  <anyType xsi:type="InvoiceLine">
                    <Quantity>1</Quantity>
                    <Description>Parts</Description>
                    <Rate>4</Rate>
                    <ChargeType>4001</ChargeType>
                    <VatRate>0</VatRate>
                    <VatAmount>0</VatAmount>
                    <ProductID>0</ProductID>
                    <Sort>2</Sort>
                    <ProjID>0</ProjID>
                    <LineID>10</LineID>
                  </anyType>
                </Lines>
                <NetAmount>30.50</NetAmount>
                <VATAmount>6.10</VATAmount>
              </GetInvoiceByIDResult>
              <Status>OK</Status>
            </GetInvoiceByIDResponse>
            """));
        await using var client = Client(handler);

        var invoice = await client.GetInvoiceByIDAsync(100);

        Assert.NotNull(invoice);
        Assert.Equal(30.50m, invoice.NetAmount);
        Assert.Equal(2, invoice.Lines!.Count);
        Assert.Equal(2m, invoice.Lines[0].Quantity);
        Assert.Equal(15.25m, invoice.Lines[0].Rate);
        Assert.Equal(9, invoice.Lines[0].LineID);
        Assert.Equal("Work", invoice.Lines[0].Description);
        Assert.Equal(10, invoice.Lines[1].LineID);
        Assert.Equal("Parts", invoice.Lines[1].Description);
    }

    [Fact]
    public async Task GetReceiptsWithPaging_ReadsPageMetadata()
    {
        var handler = new RecordingHandler(_ => Envelope("""
            <GetReceiptsWithPagingResponse xmlns="KashFlow">
              <GetReceiptsWithPagingResult>
                <Invoice>
                  <InvoiceDBID>8</InvoiceDBID>
                  <InvoiceNumber>15</InvoiceNumber>
                  <NetAmount>5</NetAmount>
                </Invoice>
              </GetReceiptsWithPagingResult>
              <Status>OK</Status>
              <StatusDetail></StatusDetail>
              <PageNumber>2</PageNumber>
              <TotalPages>6</TotalPages>
              <TotalRecords>550</TotalRecords>
            </GetReceiptsWithPagingResponse>
            """));
        await using var client = Client(handler);

        var page = await client.GetReceiptsWithPagingAsync(2, 100, "All");

        Assert.Equal(2, page.PageNumber);
        Assert.Equal(6, page.TotalPages);
        Assert.Equal(550, page.TotalRecords);
        Assert.Equal(8, Assert.Single(page.Items).InvoiceDBID);
    }

    [Fact]
    public async Task UnsuccessfulStatus_ThrowsWithoutEchoingThePassword()
    {
        var handler = new RecordingHandler(_ => Envelope("""
            <GetSuppliersResponse xmlns="KashFlow">
              <Status>NO</Status>
              <StatusDetail>Invalid username or password</StatusDetail>
            </GetSuppliersResponse>
            """));
        await using var client = Client(handler);

        var error = await Assert.ThrowsAsync<KashFlowException>(() => client.GetSuppliersAsync());

        Assert.Equal("GetSuppliers", error.Operation);
        Assert.Equal("NO", error.Status);
        Assert.Equal("Invalid username or password", error.StatusDetail);
        Assert.DoesNotContain("secret-password", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SoapFault_ThrowsWithFaultText()
    {
        var handler = new RecordingHandler(_ => Envelope("""
            <soap:Fault>
              <faultcode>soap:Client</faultcode>
              <faultstring>Server did not recognize the value of HTTP Header SOAPAction.</faultstring>
            </soap:Fault>
            """, HttpStatusCode.InternalServerError));
        await using var client = Client(handler);

        var error = await Assert.ThrowsAsync<KashFlowException>(() => client.GetCompanyDetailsAsync());

        Assert.Contains("SOAPAction", error.Message, StringComparison.Ordinal);
        Assert.Equal("soap:Client", error.FaultCode);
    }

    [Fact]
    public async Task UnknownTradeBorder_FailsTheCall()
    {
        var handler = new RecordingHandler(_ => Envelope("""
            <GetSupplier2Response xmlns="KashFlow">
              <GetSupplier2Result>
                <SupplierID>1</SupplierID>
                <Name>Acme</Name>
                <TradeBorderType>Mars</TradeBorderType>
              </GetSupplier2Result>
              <Status>OK</Status>
            </GetSupplier2Response>
            """));
        await using var client = Client(handler);

        var error = await Assert.ThrowsAsync<KashFlowException>(() => client.GetSupplier2Async("ACME"));

        Assert.Equal("GetSupplier2", error.Operation);
        Assert.Contains("contract", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LiveService_RejectsUnknownCredentialsAsAKashFlowError()
    {
        if (Environment.GetEnvironmentVariable("KASHFLOW_LIVE") != "1")
            return;

        await using var client = new KashFlowClient("kashflow-client-probe", "secret-password");
        var error = await Assert.ThrowsAsync<KashFlowException>(() => client.GetSuppliersAsync());

        Assert.Equal("GetSuppliers", error.Operation);
        Assert.DoesNotContain("secret-password", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("does not match the SOAP contract", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadOnly_BlocksWritesAndAllowsReads()
    {
        var blocked = new RecordingHandler(_ => throw new InvalidOperationException("A read-only connection must not send a write."));
        await using var writer = new KashFlowClient(new KashFlowClientOptions("user", "secret-password") { ReadOnly = true }, new HttpClient(blocked));

        var error = await Assert.ThrowsAsync<KashFlowException>(() => writer.InsertSupplierAsync(new Supplier { Name = "Acme" }));

        Assert.Equal("InsertSupplier", error.Operation);
        Assert.Contains("read-only", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("", blocked.Body);

        var allowed = new RecordingHandler(_ => Envelope("""
            <GetSupplier2Response xmlns="KashFlow">
              <GetSupplier2Result><SupplierID>1</SupplierID><Name>Acme</Name></GetSupplier2Result>
              <Status>OK</Status>
            </GetSupplier2Response>
            """));
        await using var reader = new KashFlowClient(new KashFlowClientOptions("user", "secret-password") { ReadOnly = true }, new HttpClient(allowed));

        var supplier = await reader.GetSupplier2Async("ACME");

        Assert.Equal("Acme", supplier?.Name);
        Assert.Contains("GetSupplier2", allowed.SoapAction, StringComparison.Ordinal);
    }

    private static KashFlowClient Client(RecordingHandler handler)
        => new(new KashFlowClientOptions("user", "secret-password"), new HttpClient(handler));

    private static HttpResponseMessage Envelope(string body, HttpStatusCode status = HttpStatusCode.OK)
    {
        var xml = $"""
            <?xml version="1.0" encoding="utf-8"?>
            <soap:Envelope xmlns:soap="http://schemas.xmlsoap.org/soap/envelope/">
              <soap:Body>
                {body}
              </soap:Body>
            </soap:Envelope>
            """;
        return new HttpResponseMessage(status)
        {
            Content = new StringContent(xml, Encoding.UTF8, "text/xml"),
        };
    }

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public string Body { get; private set; } = "";

        public string? SoapAction { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            SoapAction = request.Headers.TryGetValues("SOAPAction", out var values) ? values.Single() : null;
            Body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            return respond(request);
        }
    }
}
