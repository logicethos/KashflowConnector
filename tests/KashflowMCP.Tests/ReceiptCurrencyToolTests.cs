using System.Text.Json;
using KashFlow;
using KashflowMCP.Rest;
using KashflowMCP.Tools;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace KashflowMCP.Tests;

public class ReceiptCurrencyToolTests
{
    [Fact]
    public async Task DryRun_shows_foreign_and_gbp_amounts_and_does_not_require_a_write()
    {
        using var session = KashFlowSession.FromConfiguration(Config(new Dictionary<string, string?>
        {
            ["KashFlow:ReadOnly"] = "true",
        }));
        var tools = new WriteTools(session, new McpRuntimeOptions(true));

        var json = await tools.InsertReceipt2(
            new InsertReceiptInput
            {
                SupplierId = 7,
                CurrencyCode = "USD",
                ExchangeRate = 1.3123m,
                Lines =
                [
                    new InsertDocumentLineInput
                    {
                        Quantity = 1,
                        Description = "KF04195",
                        Rate = 192m,
                        ChargeType = 5000,
                        VatRate = 0,
                    },
                ],
            },
            dryRun: true);

        using var document = JsonDocument.Parse(json);
        Assert.True(document.RootElement.GetProperty("dryRun").GetBoolean());
        Assert.False(document.RootElement.TryGetProperty("receiptId", out _));
        var currency = document.RootElement.GetProperty("currency");
        Assert.Equal("USD", currency.GetProperty("currencyCode").GetString());
        Assert.Equal("ForeignPerGbp", currency.GetProperty("rateConvention").GetString());
        Assert.Equal(1.3123m, currency.GetProperty("exchangeRateForeignPerGbp").GetDecimal());
        Assert.Equal(192m, currency.GetProperty("foreignNet").GetDecimal());
        Assert.Equal(192m, currency.GetProperty("foreignGross").GetDecimal());
        Assert.Equal(146.31m, currency.GetProperty("gbpNet").GetDecimal());
        Assert.Equal(146.31m, currency.GetProperty("gbpGross").GetDecimal());
        var line = currency.GetProperty("lines")[0];
        Assert.Equal(192m, line.GetProperty("foreignUnitRate").GetDecimal());
        Assert.Equal(146.31m, line.GetProperty("gbpUnitRate").GetDecimal());
    }

    [Fact]
    public async Task DryRun_rejects_the_reciprocal_convention()
    {
        using var session = KashFlowSession.FromConfiguration(Config(new Dictionary<string, string?>()));
        var tools = new WriteTools(session, new McpRuntimeOptions(true));

        var error = await Assert.ThrowsAsync<ArgumentException>(() => tools.InsertReceipt2(
            new InsertReceiptInput
            {
                SupplierId = 7,
                CurrencyCode = "USD",
                ExchangeRate = 0.7620m,
                RateConvention = "GbpPerForeign",
                Lines = [new InsertDocumentLineInput { Quantity = 1, Rate = 192m, ChargeType = 5000 }],
            },
            dryRun: true));

        Assert.Contains("1.3123", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Structured_input_stays_in_document_currency()
    {
        var receipt = WriteInputMapper.ReceiptFromInput(new InsertReceiptInput
        {
            SupplierId = 7,
            CurrencyCode = "USD",
            ExchangeRate = 1.3123m,
            Lines = [new InsertDocumentLineInput { Quantity = 1, Rate = 192m, ChargeType = 5000, VatAmount = 0m }],
        });

        Assert.Equal(192m, receipt.Lines![0].Rate);
        Assert.Equal(192m, receipt.NetAmount);
        Assert.Equal(1.3123m, receipt.ExchangeRate);
    }

    [Fact]
    public void Payment_line_put_fills_a_missing_home_rate_and_keeps_an_existing_one()
    {
        var missing = PurchasePaymentLines.ToRequestPreservingNonPaymentFields(new PurchaseResponse
        {
            Number = 4195,
            SupplierCode = "HETZ",
            Currency = new RestCurrencyResponse { Code = "EUR", ExchangeRate = 1.2m },
            LineItems =
            [
                new PurchaseLineItemResponse
                {
                    Number = 1,
                    Rate = 12m,
                    VATAmount = 2.4m,
                    NominalCode = 5000,
                },
            ],
        });

        var filled = Assert.Single(missing.LineItems!);
        Assert.Equal(10.00m, filled.HomeCurrencyRate);
        Assert.Equal(2.00m, filled.HomeCurrencyVATAmount);
        Assert.Equal(12m, filled.Rate);

        var kept = PurchasePaymentLines.ToRequestPreservingNonPaymentFields(new PurchaseResponse
        {
            Currency = new RestCurrencyResponse { Code = "EUR", ExchangeRate = 1.2m },
            LineItems =
            [
                new PurchaseLineItemResponse
                {
                    Rate = 12m,
                    HomeCurrencyRate = 12m,
                    VATAmount = 0m,
                    HomeCurrencyVATAmount = 0m,
                },
            ],
        });
        Assert.Equal(12m, Assert.Single(kept.LineItems!).HomeCurrencyRate);
    }

    private static IConfiguration Config(Dictionary<string, string?> values)
        => new ConfigurationBuilder().AddInMemoryCollection(values).Build();
}
