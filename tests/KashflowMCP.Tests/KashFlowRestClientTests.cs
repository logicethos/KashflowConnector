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

    [Fact]
    public async Task PutBankTransactionList_sends_assign_to_purchases()
    {
        var handler = SessionThen((req, body) =>
        {
            Assert.Equal(HttpMethod.Put, req.Method);
            Assert.EndsWith("/bankaccounts/99/transactionlist", req.RequestUri!.AbsolutePath, StringComparison.Ordinal);
            Assert.Contains("\"AssignToPurchases\"", body, StringComparison.Ordinal);
            Assert.Contains("\"TransactionId\":501", body, StringComparison.Ordinal);
            Assert.Contains("\"Number\":4200", body, StringComparison.Ordinal);
            Assert.Contains("\"PaymentMethod\":7", body, StringComparison.Ordinal);
            return Json(HttpStatusCode.OK, """{"AssignToPurchases":[{"TransactionId":501,"Number":4200,"PaymentMethod":7}]}""");
        });

        using var client = new KashFlowRestClient("user", Password, Memorable, handler);
        var response = await client.PutBankTransactionListAsync(99, new BankTransactionBulkAssignRequest
        {
            AssignToPurchases =
            [
                new BankTransactionAssignToEntity { TransactionId = 501, Number = 4200, PaymentMethod = 7 },
            ],
        });

        Assert.NotNull(response.AssignToPurchases);
        Assert.Equal(501, response.AssignToPurchases![0].TransactionId);
    }

    [Fact]
    public async Task PutAndDeletePurchasesBulkPayment_use_number_path()
    {
        var handler = new ScriptedHandler(
        [
            SessionChallenge(),
            SessionToken("sess"),
            (req, body) =>
            {
                Assert.Equal(HttpMethod.Put, req.Method);
                Assert.EndsWith("/purchases/bulk/payments/24", req.RequestUri!.AbsolutePath, StringComparison.Ordinal);
                Assert.Contains("\"Comment\":\"fixed\"", body, StringComparison.Ordinal);
                return Json(HttpStatusCode.OK, """
                    {"Number":24,"Date":"2026-09-11","AccountId":99,"Method":7,"Comment":"fixed","PaymentItems":[{"ObjectNumber":4199,"Amount":2.4}]}
                    """);
            },
            (req, _) =>
            {
                Assert.Equal(HttpMethod.Delete, req.Method);
                Assert.EndsWith("/purchases/bulk/payments/24", req.RequestUri!.AbsolutePath, StringComparison.Ordinal);
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            },
        ]);

        using var client = new KashFlowRestClient("user", Password, Memorable, handler);
        var updated = await client.PutPurchasesBulkPaymentAsync(24, new BulkPurchasePaymentRequest
        {
            Number = 24,
            Date = "2026-09-11",
            Comment = "fixed",
            AccountId = 99,
            Method = 7,
            PaymentItems = [new BulkPurchasePaymentItem { ObjectNumber = 4199, Amount = 2.4m }],
        });
        Assert.Equal("fixed", updated.Comment);
        await client.DeletePurchasesBulkPaymentAsync(24);
        Assert.Equal(4, handler.CallCount);
    }

    [Fact]
    public async Task GetAndPutPurchase_round_trip_payment_lines()
    {
        var handler = new ScriptedHandler(
        [
            SessionChallenge(),
            SessionToken("sess"),
            (req, _) =>
            {
                Assert.Equal(HttpMethod.Get, req.Method);
                Assert.EndsWith("/purchases/4200", req.RequestUri!.AbsolutePath, StringComparison.Ordinal);
                return Json(HttpStatusCode.OK, """
                    {
                      "Number":4200,
                      "IssuedDate":"2026-09-01",
                      "DueDate":"2026-09-30",
                      "SupplierCode":"SUP1",
                      "Currency":{"Code":"GBP","ExchangeRate":1},
                      "LineItems":[{"Number":1,"Description":"Widget","Quantity":1,"Rate":88.88,"VATAmount":0,"NominalCode":5000}],
                      "PaymentLines":[{"Id":11,"Date":"2026-09-10","Amount":88.88,"AccountId":99,"Method":7,"Note":"old"}]
                    }
                    """);
            },
            (req, body) =>
            {
                Assert.Equal(HttpMethod.Put, req.Method);
                Assert.Contains("\"BankTransactionId\":901", body, StringComparison.Ordinal);
                Assert.Contains("\"SupplierCode\":\"SUP1\"", body, StringComparison.Ordinal);
                Assert.Contains("\"LineItems\"", body, StringComparison.Ordinal);
                return Json(HttpStatusCode.OK, """
                    {
                      "Number":4200,
                      "SupplierCode":"SUP1",
                      "PaymentLines":[{"Id":11,"Date":"2026-09-10","Amount":88.88,"AccountId":99,"Method":7,"BankTransactionId":901}]
                    }
                    """);
            },
        ]);

        using var client = new KashFlowRestClient("user", Password, Memorable, handler);
        var existing = await client.GetPurchaseAsync(4200);
        var request = PurchasePaymentLines.BuildUpdateRequest(
            existing,
            [new PurchasePaymentLines.PaymentLineEdit { Id = 11, BankTransactionId = 901 }],
            replaceAll: false);
        Assert.Equal("SUP1", request.SupplierCode);
        Assert.Single(request.LineItems!);
        Assert.Equal(901, request.PaymentLines![0].BankTransactionId);

        var updated = await client.PutPurchaseAsync(4200, request);
        Assert.Equal(901, updated.PaymentLines![0].BankTransactionId);
    }

    [Fact]
    public async Task GetBankAccountTransactions_appends_filter_query()
    {
        var handler = SessionThen((req, _) =>
        {
            Assert.Equal(HttpMethod.Get, req.Method);
            Assert.Contains("filterby=Unallocated", req.RequestUri!.Query, StringComparison.Ordinal);
            Assert.Contains("includefeedmatched=true", req.RequestUri.Query, StringComparison.Ordinal);
            Assert.Contains("startdate=2026-09-01", req.RequestUri.Query, StringComparison.Ordinal);
            return Json(HttpStatusCode.OK, """{"Data":[],"TotalRecords":0}""");
        });

        using var client = new KashFlowRestClient("user", Password, Memorable, handler);
        var payload = await client.GetBankAccountTransactionsAsync(99, new BankTransactionListQuery
        {
            FilterBy = "Unallocated",
            IncludeFeedMatched = true,
            StartDate = new DateTime(2026, 9, 1),
        });
        Assert.Equal(JsonValueKind.Object, payload.ValueKind);
    }

    [Fact]
    public async Task Reconciliations_and_files_use_expected_paths()
    {
        var handler = new ScriptedHandler(
        [
            SessionChallenge(),
            SessionToken("sess"),
            (req, _) =>
            {
                Assert.EndsWith("/bankaccounts/99/reconciliations", req.RequestUri!.AbsolutePath, StringComparison.Ordinal);
                return Json(HttpStatusCode.OK, """{"Data":[{"Id":1,"Status":"Done"}]}""");
            },
            (req, _) =>
            {
                Assert.EndsWith("/bankaccounts/99/reconciliations/metadata", req.RequestUri!.AbsolutePath, StringComparison.Ordinal);
                return Json(HttpStatusCode.OK, """{"NextReconciliationStartDate":"2026-10-01","NextReconciliationStartBalance":100.5}""");
            },
            (req, _) =>
            {
                Assert.EndsWith("/purchases/4200/files", req.RequestUri!.AbsolutePath, StringComparison.Ordinal);
                return Json(HttpStatusCode.OK, """[{"FileName":"invoice.pdf"}]""");
            },
            (req, _) =>
            {
                Assert.Equal(HttpMethod.Delete, req.Method);
                Assert.EndsWith("/purchases/4200/file", req.RequestUri!.AbsolutePath, StringComparison.Ordinal);
                Assert.Contains("fileName=invoice.pdf", req.RequestUri.Query, StringComparison.Ordinal);
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{}", Encoding.UTF8, "application/json"),
                };
            },
        ]);

        using var client = new KashFlowRestClient("user", Password, Memorable, handler);
        await client.GetBankReconciliationsAsync(99);
        var meta = await client.GetBankReconciliationMetadataAsync(99);
        Assert.Equal("2026-10-01", meta.NextReconciliationStartDate);
        await client.ListPurchaseFilesAsync(4200);
        await client.DeletePurchaseFileAsync(4200, "invoice.pdf");
        Assert.Equal(6, handler.CallCount);
    }

    [Fact]
    public async Task UploadAndAttachPurchaseFile_posts_multipart_then_json()
    {
        var handler = new ScriptedHandler(
        [
            SessionChallenge(),
            SessionToken("sess"),
            (req, _) =>
            {
                Assert.Equal(HttpMethod.Post, req.Method);
                Assert.EndsWith("/files", req.RequestUri!.AbsolutePath, StringComparison.Ordinal);
                Assert.Contains("objectType=purchases", req.RequestUri.Query, StringComparison.Ordinal);
                Assert.StartsWith("multipart/", req.Content!.Headers.ContentType!.MediaType!, StringComparison.Ordinal);
                return Json(HttpStatusCode.OK, """{"FileName":"bill.pdf","Path":"/dropbox/bill.pdf"}""");
            },
            (req, body) =>
            {
                Assert.Equal(HttpMethod.Post, req.Method);
                Assert.EndsWith("/purchases/4200/files", req.RequestUri!.AbsolutePath, StringComparison.Ordinal);
                Assert.Contains("bill.pdf", body, StringComparison.Ordinal);
                return Json(HttpStatusCode.OK, """{"ok":true}""");
            },
        ]);

        using var client = new KashFlowRestClient("user", Password, Memorable, handler);
        var result = await client.UploadAndAttachPurchaseFileAsync(
            4200,
            "bill.pdf",
            Encoding.UTF8.GetBytes("pdf-bytes"),
            "application/pdf");
        Assert.Equal(JsonValueKind.Object, result.ValueKind);
        Assert.Equal(4, handler.CallCount);
    }

    [Fact]
    public void BuildBankTransactionsUrl_encodes_filters()
    {
        var url = KashFlowRestClient.BuildBankTransactionsUrl(12, new BankTransactionListQuery
        {
            FilterBy = "Unallocated",
            IncludeFeedMatched = false,
            PerPage = 50,
        });
        Assert.Equal(
            "bankaccounts/12/transactions?perpage=50&filterby=Unallocated&includefeedmatched=false",
            url);
    }

    [Fact]
    public void ValidateUpload_rejects_oversize_and_path_names()
    {
        Assert.Throws<ArgumentException>(() =>
            KashFlowRestClient.ValidateUpload("../x.pdf", [1]));
        Assert.Throws<ArgumentException>(() =>
            KashFlowRestClient.ValidateUpload("big.bin", new byte[KashFlowRestClient.MaxUploadBytes + 1]));
    }

    private static ScriptedHandler SessionThen(Func<HttpRequestMessage, string, HttpResponseMessage> next)
        => new(
        [
            SessionChallenge(),
            SessionToken("sess"),
            next,
        ]);

    private static Func<HttpRequestMessage, string, HttpResponseMessage> SessionChallenge()
        => (_, _) => Json(HttpStatusCode.OK, """
            {"TemporaryToken":"temp-1","MemorableWordList":[{"Position":1,"Value":""}]}
            """);

    private static Func<HttpRequestMessage, string, HttpResponseMessage> SessionToken(string token)
        => (_, _) => Json(HttpStatusCode.OK, $$"""{"SessionToken":"{{token}}"}""");

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

public class PurchasePaymentLinesTests
{
    [Fact]
    public void BuildUpdateRequest_preserves_non_payment_fields_and_patches_by_id()
    {
        var existing = new PurchaseResponse
        {
            Number = 4200,
            IssuedDate = "2026-09-01",
            SupplierCode = "SUP1",
            Currency = new RestCurrencyResponse { Code = "EUR", ExchangeRate = 1.2m },
            LineItems =
            [
                new PurchaseLineItemResponse
                {
                    Number = 1,
                    Description = "Widget",
                    Quantity = 1,
                    Rate = 10,
                    NominalCode = 5000,
                },
            ],
            PaymentLines =
            [
                new PurchasePaymentLineResponse
                {
                    Id = 11,
                    Date = "2026-09-10",
                    Amount = 10,
                    AccountId = 99,
                    Method = 7,
                    Note = "old",
                },
            ],
        };

        var request = PurchasePaymentLines.BuildUpdateRequest(
            existing,
            [new PurchasePaymentLines.PaymentLineEdit { Id = 11, BankTransactionId = 901, Note = "linked" }],
            replaceAll: false);

        Assert.Equal("SUP1", request.SupplierCode);
        Assert.Equal("EUR", request.Currency!.Code);
        Assert.Equal(1.2m, request.Currency.ExchangeRate);
        var lineItems = Assert.Single(request.LineItems!);
        Assert.Equal("Widget", lineItems.Description);
        var paymentLine = Assert.Single(request.PaymentLines!);
        Assert.Equal(901, paymentLine.BankTransactionId);
        Assert.Equal("linked", paymentLine.Note);

        using var doc = JsonDocument.Parse(PurchasePaymentLines.SerializeRequestBody(request));
        Assert.Equal(901, doc.RootElement.GetProperty("PaymentLines")[0].GetProperty("BankTransactionId").GetInt64());
    }

    [Fact]
    public void BuildUpdateRequest_replaceAll_swaps_payment_lines()
    {
        var existing = new PurchaseResponse
        {
            Number = 1,
            SupplierCode = "S",
            PaymentLines =
            [
                new PurchasePaymentLineResponse { Id = 1, Date = "2026-01-01", Amount = 5, AccountId = 1, Method = 1 },
            ],
        };

        var request = PurchasePaymentLines.BuildUpdateRequest(
            existing,
            [
                new PurchasePaymentLines.PaymentLineEdit
                {
                    Date = new DateTime(2026, 2, 1),
                    Amount = 9m,
                    AccountId = 2,
                    Method = 3,
                },
            ],
            replaceAll: true);

        var replaced = Assert.Single(request.PaymentLines!);
        Assert.Null(replaced.Id);
        Assert.Equal("2026-02-01", replaced.Date);
        Assert.Equal(9m, replaced.Amount);
    }
}
