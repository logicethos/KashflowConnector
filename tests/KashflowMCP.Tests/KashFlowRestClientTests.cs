using System.Net;
using System.Text;
using System.Text.Json;
using KashflowMCP.Rest;
using KashflowMCP.Tools;
using Xunit;

namespace KashflowMCP.Tests;

public class KashFlowRestClientTests
{
    private const string Password = "soap-password-secret";
    private const string Memorable = "pineapple";

    [Fact]
    public void FillMemorableWord_picks_1_based_characters()
    {
        var filled = KashFlowRestClient.FillMemorableWord(
            Memorable,
            [
                new RestMemorableWordEntry { Position = 3, Value = "" },
                new RestMemorableWordEntry { Position = 4, Value = "" },
                new RestMemorableWordEntry { Position = 6, Value = "" },
            ]);

        // pineapple → positions 3,4,6 are n, e, p (docs example).
        Assert.Equal(["n", "e", "p"], filled.Select(item => item.Value));
    }

    [Fact]
    public void FillMemorableWord_rejects_out_of_range_positions_without_echoing_the_word()
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            KashFlowRestClient.FillMemorableWord(
                Memorable,
                [new RestMemorableWordEntry { Position = 99, Value = "" }]));

        Assert.Contains("position 99", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(Memorable, error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task EnsureSession_posts_then_puts_and_caches_token()
    {
        var handler = new ScriptedHandler(
        [
            (req, _) =>
            {
                Assert.Equal(HttpMethod.Post, req.Method);
                Assert.EndsWith("/sessiontoken", req.RequestUri!.AbsolutePath, StringComparison.Ordinal);
                return Json(HttpStatusCode.OK, """
                    {
                      "TemporaryToken":"temp-1",
                      "MemorableWordList":[
                        {"Position":3,"Value":""},
                        {"Position":4,"Value":""},
                        {"Position":6,"Value":""}
                      ]
                    }
                    """);
            },
            (req, body) =>
            {
                Assert.Equal(HttpMethod.Put, req.Method);
                Assert.Contains("\"TemporaryToken\":\"temp-1\"", body, StringComparison.Ordinal);
                Assert.Contains("\"Position\":3", body, StringComparison.Ordinal);
                Assert.Contains("\"Value\":\"n\"", body, StringComparison.Ordinal);
                Assert.DoesNotContain(Password, body, StringComparison.Ordinal);
                Assert.DoesNotContain(Memorable, body, StringComparison.Ordinal);
                return Json(HttpStatusCode.OK, """{"SessionToken":"sess-abc"}""");
            },
        ]);

        using var client = new KashFlowRestClient("user", Password, Memorable, handler);
        await client.EnsureSessionAsync();
        Assert.Equal("sess-abc", client.CachedSessionToken);

        // Second call should not hit the network again.
        await client.EnsureSessionAsync();
        Assert.Equal(2, handler.CallCount);
    }

    [Fact]
    public async Task PostPurchasesBulkPayment_sends_KfToken_and_parses_201()
    {
        var handler = new ScriptedHandler(
        [
            (_, _) => Json(HttpStatusCode.OK, """
                {
                  "TemporaryToken":"temp-1",
                  "MemorableWordList":[{"Position":1,"Value":""}]
                }
                """),
            (_, _) => Json(HttpStatusCode.OK, """{"SessionToken":"sess-xyz"}"""),
            (req, body) =>
            {
                Assert.Equal(HttpMethod.Post, req.Method);
                Assert.EndsWith("/purchases/bulk/payments", req.RequestUri!.AbsolutePath, StringComparison.Ordinal);
                Assert.Equal("KfToken", req.Headers.Authorization!.Scheme);
                Assert.Equal("sess-xyz", req.Headers.Authorization.Parameter);
                Assert.Contains("\"ObjectNumber\":4199", body, StringComparison.Ordinal);
                Assert.Contains("\"Amount\":2.4", body, StringComparison.Ordinal);
                Assert.DoesNotContain(Password, body, StringComparison.Ordinal);
                Assert.DoesNotContain(Memorable, body, StringComparison.Ordinal);
                return Json(HttpStatusCode.Created, """
                    {
                      "Number":24,
                      "Date":"2026-09-10",
                      "AccountId":99,
                      "Method":7,
                      "PaymentItems":[
                        {"ObjectNumber":4199,"Amount":"2.40"},
                        {"ObjectNumber":4200,"Amount":"88.88"}
                      ],
                      "Reconciled":false
                    }
                    """);
            },
        ]);

        using var client = new KashFlowRestClient("user", Password, Memorable, handler);
        var response = await client.PostPurchasesBulkPaymentAsync(new BulkPurchasePaymentRequest
        {
            Date = "2026-09-10",
            AccountId = 99,
            Method = 7,
            PaymentItems =
            [
                new BulkPurchasePaymentItem { ObjectNumber = 4199, Amount = 2.40m },
                new BulkPurchasePaymentItem { ObjectNumber = 4200, Amount = 88.88m },
            ],
        });

        Assert.Equal(24, response.Number);
        Assert.Equal(2, response.PaymentItems!.Count);
        Assert.Equal(2.40m, response.PaymentItems[0].Amount);
    }

    [Fact]
    public async Task PostPurchasesBulkPayment_refreshes_session_after_401()
    {
        var handler = new ScriptedHandler(
        [
            (_, _) => Json(HttpStatusCode.OK, """
                {"TemporaryToken":"t1","MemorableWordList":[{"Position":1,"Value":""}]}
                """),
            (_, _) => Json(HttpStatusCode.OK, """{"SessionToken":"old"}"""),
            (_, _) => Json(HttpStatusCode.Unauthorized, """
                {"Error":"InvalidSessionToken","Message":"Session token provided is invalid."}
                """),
            (_, _) => Json(HttpStatusCode.OK, """
                {"TemporaryToken":"t2","MemorableWordList":[{"Position":1,"Value":""}]}
                """),
            (_, _) => Json(HttpStatusCode.OK, """{"SessionToken":"new"}"""),
            (req, _) =>
            {
                Assert.Equal("new", req.Headers.Authorization!.Parameter);
                return Json(HttpStatusCode.Created, """
                    {"Number":1,"Date":"2026-09-10","AccountId":1,"Method":1,"PaymentItems":[]}
                    """);
            },
        ]);

        using var client = new KashFlowRestClient("user", Password, Memorable, handler);
        var response = await client.PostPurchasesBulkPaymentAsync(new BulkPurchasePaymentRequest
        {
            Date = "2026-09-10",
            AccountId = 1,
            Method = 1,
            PaymentItems = [new BulkPurchasePaymentItem { ObjectNumber = 1, Amount = 1m }],
        });

        Assert.Equal(1, response.Number);
        Assert.Equal("new", client.CachedSessionToken);
        Assert.Equal(6, handler.CallCount);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body)
        => new(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };

    private sealed class ScriptedHandler : HttpMessageHandler
    {
        private readonly Queue<Func<HttpRequestMessage, string, HttpResponseMessage>> _steps;

        public ScriptedHandler(IEnumerable<Func<HttpRequestMessage, string, HttpResponseMessage>> steps)
            => _steps = new Queue<Func<HttpRequestMessage, string, HttpResponseMessage>>(steps);

        public int CallCount { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            if (_steps.Count == 0)
                throw new InvalidOperationException($"Unexpected HTTP call #{CallCount} to {request.RequestUri}");

            var body = request.Content is null
                ? ""
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return _steps.Dequeue()(request, body);
        }
    }
}

public class PayPurchasesBulkTests
{
    [Fact]
    public void Build_starling_example_matches_expected_request_body()
    {
        var receipts = new Dictionary<int, KashFlow.Receipt2>
        {
            [4199] = new() { ReceiptNumber = 4199, NetAmount = 2.40m, VATAmount = 0m, AmountPaid = 0m },
            [4200] = new() { ReceiptNumber = 4200, NetAmount = 88.88m, VATAmount = 0m, AmountPaid = 0m },
        };

        var planned = PayPurchasesBulk.Build(
            new DateTime(2026, 9, 10),
            91.28m,
            [new(4199, 2.40m), new(4200, 88.88m)],
            receipts,
            bankAccountId: 555,
            paymentMethodId: 12,
            note: "Starling 1206 batch");

        Assert.Equal("2026-09-10", planned.Request.Date);
        Assert.Equal(555, planned.Request.AccountId);
        Assert.Equal(12, planned.Request.Method);
        Assert.Equal("Starling 1206 batch", planned.Request.Comment);
        Assert.Equal(2, planned.Request.PaymentItems.Count);
        Assert.Equal(4199, planned.Request.PaymentItems[0].ObjectNumber);
        Assert.Equal(2.40m, planned.Request.PaymentItems[0].Amount);
        Assert.Equal(4200, planned.Request.PaymentItems[1].ObjectNumber);
        Assert.Equal(88.88m, planned.Request.PaymentItems[1].Amount);

        using var doc = JsonDocument.Parse(PayPurchasesBulk.SerializeRequestBody(planned.Request));
        Assert.Equal("2026-09-10", doc.RootElement.GetProperty("Date").GetString());
        Assert.Equal(91.28m, doc.RootElement.GetProperty("PaymentItems").EnumerateArray().Sum(item => item.GetProperty("Amount").GetDecimal()));
    }

    [Fact]
    public void Build_rejects_over_allocation_using_gbp_book_outstanding()
    {
        // €21.49 at 1.1941 → £18.00 book; £10 already paid → £8 outstanding.
        var receipts = new Dictionary<int, KashFlow.Receipt2>
        {
            [100] = new()
            {
                ReceiptNumber = 100,
                NetAmount = 21.49m,
                VATAmount = 0m,
                AmountPaid = 10m,
                ExchangeRate = 1.1941m,
            },
        };

        var error = Assert.Throws<ArgumentException>(() => PayPurchasesBulk.Build(
            new DateTime(2026, 1, 1),
            9m,
            [new(100, 9m)],
            receipts,
            1,
            2,
            null));

        Assert.Contains("exceeds outstanding", error.Message, StringComparison.Ordinal);
    }
}
