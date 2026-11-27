using System.Net;
using System.Text;
using System.Xml.Linq;
using Xunit;

namespace KashFlow.Tests;

public class ReceiptCurrencyTests
{
    [Fact]
    public void Usd_192_at_1_3123_books_146_31_and_keeps_the_foreign_header()
    {
        var source = UsdReceipt(192m, 1.3123m);
        var soap = ReceiptCurrency.ToSoap(source);

        Assert.Equal(192m, source.Lines![0].Rate);
        Assert.Equal(192m, source.NetAmount);
        Assert.Equal("USD", soap.CurrencyCode);
        Assert.Equal(1.3123m, soap.ExchangeRate);
        Assert.Equal(146.31m, soap.Lines![0].Rate);
        Assert.Equal(0m, soap.Lines[0].VatAmount);
        Assert.Equal(192m, soap.NetAmount);
        Assert.Equal(0m, soap.VATAmount);
        Assert.Equal(1.25m, soap.Lines[0].ImportDuty);

        var preview = ReceiptCurrency.Describe(source, soap);
        Assert.Equal(ReceiptCurrency.ForeignPerGbp, preview.RateConvention);
        Assert.True(preview.IsForeignCurrency);
        Assert.Equal(1.3123m, preview.ExchangeRateForeignPerGbp);
        Assert.Equal(192m, preview.ForeignNet);
        Assert.Equal(146.31m, preview.GbpNet);
        Assert.Equal(146.31m, preview.Lines[0].GbpUnitRate);
    }

    [Fact]
    public void Hetzner_eur_21_49_at_1_1941_books_18_pounds()
    {
        var soap = ReceiptCurrency.ToSoap(new Receipt2
        {
            CurrencyCode = "eur",
            ExchangeRate = 1.1941m,
            NetAmount = 21.49m,
            Lines =
            [
                new ReceiptLine2 { Quantity = 1, Description = "Hetzner", Rate = 21.49m, ChargeType = 5000 },
            ],
        });

        Assert.Equal("EUR", soap.CurrencyCode);
        Assert.Equal(18.00m, soap.Lines![0].Rate);
        Assert.Equal(21.49m, soap.NetAmount);
    }

    [Fact]
    public void Vat_uses_the_same_rate_and_penny_adjustment_balances_the_total()
    {
        var soap = ReceiptCurrency.ToSoap(new Receipt2
        {
            CurrencyCode = "USD",
            ExchangeRate = 3m,
            Lines =
            [
                new ReceiptLine2 { Quantity = 1, Rate = 10m, VatRate = 20m, VatAmount = 2m, ChargeType = 1 },
                new ReceiptLine2 { Quantity = 1, Rate = 10m, VatRate = 20m, VatAmount = 2m, ChargeType = 1 },
            ],
        });

        Assert.Equal(20m, soap.NetAmount);
        Assert.Equal(4m, soap.VATAmount);
        Assert.Equal(3.33m, soap.Lines![0].Rate);
        Assert.Equal(0.67m, soap.Lines[0].VatAmount);
        Assert.Equal(3.34m, soap.Lines[1].Rate);
        Assert.Equal(0.66m, soap.Lines[1].VatAmount);
        Assert.Equal(6.67m, soap.Lines.Sum(line => line.Rate));
        Assert.Equal(1.33m, soap.Lines.Sum(line => line.VatAmount));
    }

    [Fact]
    public void Quantity_above_one_uses_a_two_dp_unit_rate_when_it_reconstructs_the_line()
    {
        var soap = ReceiptCurrency.ToSoap(new Receipt2
        {
            CurrencyCode = "AUD",
            ExchangeRate = 2m,
            Lines = [new ReceiptLine2 { Quantity = 3, Rate = 15m, VatAmount = 0m, ChargeType = 5000 }],
        });

        Assert.Equal(7.50m, soap.Lines![0].Rate);
        Assert.Equal(45m, soap.NetAmount);
    }

    [Fact]
    public void Credit_note_divides_the_negative_document_amount()
    {
        var soap = ReceiptCurrency.ToSoap(new Receipt2
        {
            CurrencyCode = "EUR",
            ExchangeRate = 1.15m,
            Lines = [new ReceiptLine2 { Quantity = 1, Rate = -118.30m, ChargeType = 5000 }],
        });

        Assert.Equal(-102.87m, soap.Lines![0].Rate);
        Assert.Equal(-118.30m, soap.NetAmount);
    }

    [Fact]
    public void Gbp_is_unchanged_and_the_rate_becomes_1()
    {
        var soap = ReceiptCurrency.ToSoap(new Receipt2
        {
            CurrencyCode = "GBP",
            NetAmount = 10.5m,
            VATAmount = 2.1m,
            Lines = [new ReceiptLine2 { Quantity = 1, Rate = 10.5m, VatAmount = 2.1m }],
        });

        Assert.Equal(10.5m, soap.Lines![0].Rate);
        Assert.Equal(2.1m, soap.Lines[0].VatAmount);
        Assert.Equal(1m, soap.ExchangeRate);
    }

    [Theory]
    [InlineData("USD", 0.7620)]
    [InlineData("EUR", 0.85)]
    [InlineData("USD", 0)]
    [InlineData("GBP", 1.3123)]
    public void Rejects_reciprocal_or_missing_rates(string currency, double rate)
    {
        var error = Assert.Throws<ArgumentException>(() => ReceiptCurrency.ToSoap(new Receipt2
        {
            CurrencyCode = currency,
            ExchangeRate = (decimal)rate,
            Lines = [new ReceiptLine2 { Quantity = 1, Rate = 10m }],
        }));
        Assert.Contains("GBP", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Stronger_currency_may_use_a_rate_below_one()
    {
        var soap = ReceiptCurrency.ToSoap(new Receipt2
        {
            CurrencyCode = "KWD",
            ExchangeRate = 0.40m,
            Lines = [new ReceiptLine2 { Quantity = 1, Rate = 10m }],
        });

        Assert.Equal(25.00m, soap.Lines![0].Rate);
    }

    [Fact]
    public void GbpPerForeign_convention_is_rejected()
    {
        var error = Assert.Throws<ArgumentException>(() => ReceiptCurrency.ToSoap(
            UsdReceipt(192m, 1.3123m),
            ReceiptCurrency.GbpPerForeign));
        Assert.Contains("ForeignPerGbp", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("1.3123", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Header_that_is_not_the_document_total_is_rejected()
    {
        var error = Assert.Throws<ArgumentException>(() => ReceiptCurrency.ToSoap(new Receipt2
        {
            CurrencyCode = "USD",
            ExchangeRate = 1.3123m,
            NetAmount = 146.31m,
            Lines = [new ReceiptLine2 { Quantity = 1, Rate = 192m }],
        }));
        Assert.Contains("netAmount", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ToSoapLine_converts_only_when_a_foreign_currency_is_named()
    {
        var foreign = new ReceiptLine2 { Quantity = 1, Rate = 21.49m, VatAmount = 0m, ChargeType = 9 };
        var soap = ReceiptCurrency.ToSoapLine(foreign, "EUR", 1.1941m);
        Assert.Equal(21.49m, foreign.Rate);
        Assert.Equal(18.00m, soap.Rate);
        Assert.Equal(9, soap.ChargeType);

        var home = ReceiptCurrency.ToSoapLine(foreign, "GBP", null);
        Assert.Equal(21.49m, home.Rate);
    }

    [Fact]
    public async Task InsertReceipt2_sends_gbp_line_rates_and_does_not_mutate_the_caller()
    {
        var handler = new RecordingHandler(_ => Envelope("""
            <InsertReceipt2Response xmlns="KashFlow">
              <InsertReceipt2Result>4195</InsertReceipt2Result>
              <Status>OK</Status>
            </InsertReceipt2Response>
            """));
        await using var client = Client(handler);
        var source = UsdReceipt(192m, 1.3123m);

        var id = await client.InsertReceipt2Async(source);

        Assert.Equal(4195, id);
        Assert.Equal(192m, source.Lines![0].Rate);
        var document = XDocument.Parse(handler.Body);
        Assert.Equal("USD", document.Descendants(Service + "CurrencyCode").Single().Value);
        Assert.Equal(1.3123m, decimal.Parse(document.Descendants(Service + "ExchangeRate").Single().Value, System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(192m, decimal.Parse(document.Descendants(Service + "NetAmount").Single().Value, System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(146.31m, decimal.Parse(document.Descendants(Service + "Rate").Single().Value, System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task InsertReceipt2_rejects_a_foreign_receipt_with_no_rate_before_http()
    {
        var handler = new RecordingHandler(_ => throw new InvalidOperationException("must not send"));
        await using var client = Client(handler);

        var error = await Assert.ThrowsAsync<ArgumentException>(() => client.InsertReceipt2Async(new Receipt2
        {
            CurrencyCode = "USD",
            NetAmount = 192m,
            Lines = [new ReceiptLine2 { Quantity = 1, Rate = 192m }],
        }));

        Assert.Contains("1.3123", error.Message, StringComparison.Ordinal);
        Assert.Equal("", handler.Body);
    }

    [Fact]
    public async Task InsertInvoice_is_not_converted()
    {
        var handler = new RecordingHandler(_ => Envelope("""
            <InsertInvoiceResponse xmlns="KashFlow">
              <InsertInvoiceResult>7</InsertInvoiceResult>
              <Status>OK</Status>
            </InsertInvoiceResponse>
            """));
        await using var client = Client(handler);

        await client.InsertInvoiceAsync(new Invoice
        {
            CustomerID = 1,
            CurrencyCode = "USD",
            ExchangeRate = 1.3123m,
            NetAmount = 192m,
            Lines = [new InvoiceLine { Quantity = 1, Rate = 192m, VatAmount = 0m }],
        });

        var document = XDocument.Parse(handler.Body);
        Assert.Equal(192m, decimal.Parse(document.Descendants(Service + "Rate").Single().Value, System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task UpdateReceipt_converts_the_legacy_invoice_shaped_payload()
    {
        var handler = new RecordingHandler(_ => Envelope("""
            <UpdateReceiptResponse xmlns="KashFlow">
              <UpdateReceiptResult>1</UpdateReceiptResult>
              <Status>OK</Status>
            </UpdateReceiptResponse>
            """));
        await using var client = Client(handler);

        await client.UpdateReceiptAsync(new Invoice
        {
            InvoiceNumber = 12,
            CurrencyCode = "USD",
            ExchangeRate = 1.3123m,
            NetAmount = 192m,
            Lines = [new InvoiceLine { Quantity = 1, Rate = 192m, Description = "legacy" }],
        });

        var document = XDocument.Parse(handler.Body);
        Assert.Equal(146.31m, decimal.Parse(document.Descendants(Service + "Rate").Single().Value, System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(192m, decimal.Parse(document.Descendants(Service + "NetAmount").Single().Value, System.Globalization.CultureInfo.InvariantCulture));
    }

    private static Receipt2 UsdReceipt(decimal amount, decimal rate)
        => new()
        {
            SupplierId = 7,
            CurrencyCode = "USD",
            ExchangeRate = rate,
            NetAmount = amount,
            Lines =
            [
                new ReceiptLine2
                {
                    Quantity = 1,
                    Description = "KF04195",
                    Rate = amount,
                    ChargeType = 5000,
                    ImportDuty = 1.25m,
                },
            ],
        };

    private static readonly XNamespace Service = "KashFlow";

    private static KashFlowClient Client(RecordingHandler handler)
        => new(new KashFlowClientOptions("user", "secret-password"), new HttpClient(handler));

    private static HttpResponseMessage Envelope(string body)
    {
        var xml = $"""
            <?xml version="1.0" encoding="utf-8"?>
            <soap:Envelope xmlns:soap="http://schemas.xmlsoap.org/soap/envelope/">
              <soap:Body>
                {body}
              </soap:Body>
            </soap:Envelope>
            """;
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(xml, Encoding.UTF8, "text/xml"),
        };
    }

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public string Body { get; private set; } = "";

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            return respond(request);
        }
    }
}
