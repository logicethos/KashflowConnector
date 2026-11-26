using System.Text.Json.Serialization;

namespace KashflowMCP.Rest;

internal sealed class RestSessionPostRequest
{
    public string UserName { get; set; } = "";
    public string Password { get; set; } = "";
}

internal sealed class RestMemorableWordEntry
{
    public int Position { get; set; }
    public string Value { get; set; } = "";
}

internal sealed class RestSessionChallengeResponse
{
    public string? TemporaryToken { get; set; }
    public List<RestMemorableWordEntry>? MemorableWordList { get; set; }
}

internal sealed class RestSessionPutRequest
{
    public string TemporaryToken { get; set; } = "";
    public List<RestMemorableWordEntry> MemorableWordList { get; set; } = [];
}

internal sealed class RestSessionTokenResponse
{
    public string? SessionToken { get; set; }
}

internal sealed class RestErrorBody
{
    public string? Error { get; set; }
    public string? Message { get; set; }
}

public sealed class BulkPurchasePaymentItem
{
    public int ObjectNumber { get; set; }

    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public decimal Amount { get; set; }
}

public sealed class BulkPurchasePaymentRequest
{
    public string Date { get; set; } = "";
    public string? Comment { get; set; }
    public int AccountId { get; set; }
    public int Method { get; set; }
    public List<BulkPurchasePaymentItem> PaymentItems { get; set; } = [];
}

public sealed class BulkPurchasePaymentResponse
{
    public int Number { get; set; }
    public string? Date { get; set; }
    public string? Comment { get; set; }
    public int AccountId { get; set; }
    public int Method { get; set; }
    public List<BulkPurchasePaymentItem>? PaymentItems { get; set; }
    public bool? Reconciled { get; set; }
}
