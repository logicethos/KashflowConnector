using System.Text.Json;
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
    public long? Id { get; set; }
    public int ObjectNumber { get; set; }

    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public decimal Amount { get; set; }

    public string? PaidDate { get; set; }
    public string? ContactName { get; set; }
    public string? ContactCode { get; set; }
    public bool? resetBankReconciliation { get; set; }
}

public sealed class BulkPurchasePaymentRequest
{
    public int? Number { get; set; }
    public string Date { get; set; } = "";
    public string? Comment { get; set; }
    public int AccountId { get; set; }
    public int Method { get; set; }
    public bool? Reconciled { get; set; }
    public int? BankReconciliationId { get; set; }
    public bool? resetBankReconciliation { get; set; }
    public List<BulkPurchasePaymentItem> PaymentItems { get; set; } = [];
    public long? BFSTransactionId { get; set; }
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
    public int? BankReconciliationId { get; set; }
    public long? BFSTransactionId { get; set; }
}

/// <summary>One bank transaction → purchase receipt assignment for PUT transactionlist.</summary>
public sealed class BankTransactionAssignToEntity
{
    public int TransactionId { get; set; }
    public int Number { get; set; }
    public int PaymentMethod { get; set; }
}

public sealed class BankTransactionBulkAssignRequest
{
    public List<BankTransactionAssignToEntity>? AssignToInvoices { get; set; }
    public List<BankTransactionAssignToEntity>? AssignToPurchases { get; set; }
}

public sealed class BankTransactionBulkAssignResponse
{
    public List<BankTransactionAssignToEntity>? AssignToInvoices { get; set; }
    public List<BankTransactionAssignToEntity>? AssignToPurchases { get; set; }
    public JsonElement? FailedTransactions { get; set; }
}

public sealed class RestCurrencyRequest
{
    public string Code { get; set; } = "GBP";

    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public decimal? ExchangeRate { get; set; }
}

public sealed class RestCurrencyResponse
{
    public string? Name { get; set; }
    public string? Symbol { get; set; }
    public bool? DisplaySymbolOnRight { get; set; }
    public string Code { get; set; } = "GBP";

    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public decimal? ExchangeRate { get; set; }
}

public sealed class PurchaseLineItemRequest
{
    public int? Number { get; set; }
    public string? Description { get; set; }

    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public decimal? Quantity { get; set; }

    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public decimal? Rate { get; set; }

    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public decimal? VATLevel { get; set; }

    public bool? VATExempt { get; set; }

    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public decimal? VATAmount { get; set; }

    public int? NominalCode { get; set; }
    public string? NominalName { get; set; }
    public string? ProductCode { get; set; }
    public string? TaxCode { get; set; }
    public bool? Disallowed { get; set; }
    public int? ProjectNumber { get; set; }

    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public decimal? ImportDuty { get; set; }
}

public sealed class PurchasePaymentLineRequest
{
    public long? Id { get; set; }
    public string? Date { get; set; }

    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public decimal? Amount { get; set; }

    public int? AccountId { get; set; }
    public long? Method { get; set; }
    public string? Note { get; set; }
    public long? BankTransactionId { get; set; }
    public long? BFSTransactionId { get; set; }
    public int? BulkId { get; set; }
    public int? BankReconciliationId { get; set; }
    public bool? ResetBankReconciliation { get; set; }
    public int? PaymentProcessor { get; set; }
}

public sealed class PurchaseRequest
{
    public int? Number { get; set; }
    public string? IssuedDate { get; set; }
    public string? DueDate { get; set; }
    public string? SupplierCode { get; set; }
    public string? SupplierReference { get; set; }
    public RestCurrencyRequest? Currency { get; set; }
    public List<PurchaseLineItemRequest>? LineItems { get; set; }
    public List<PurchasePaymentLineRequest>? PaymentLines { get; set; }
    public int? ProjectNumber { get; set; }
    public string? AdditionalFieldValue { get; set; }
    public bool? IsCISReverseCharge { get; set; }
    public string? Type { get; set; }
}

public sealed class PurchasePaymentLineResponse
{
    public long? Id { get; set; }
    public string? Date { get; set; }

    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public decimal? Amount { get; set; }

    public int? AccountId { get; set; }
    public long? Method { get; set; }
    public string? Note { get; set; }
    public long? BankTransactionId { get; set; }
    public long? BFSTransactionId { get; set; }
    public int? BulkId { get; set; }
    public int? BulkPaymentNumber { get; set; }
    public int? BankReconciliationId { get; set; }
    public int? PaymentProcessor { get; set; }
    public string? Permalink { get; set; }
}

public sealed class PurchaseLineItemResponse
{
    public int? Number { get; set; }
    public string? Description { get; set; }

    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public decimal? Quantity { get; set; }

    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public decimal? Rate { get; set; }

    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public decimal? VATLevel { get; set; }

    public bool? VATExempt { get; set; }

    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public decimal? VATAmount { get; set; }

    public int? NominalCode { get; set; }
    public string? NominalName { get; set; }
    public string? ProductCode { get; set; }
    public string? TaxCode { get; set; }
    public bool? Disallowed { get; set; }
    public int? ProjectNumber { get; set; }

    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public decimal? ImportDuty { get; set; }
}

public sealed class PurchaseResponse
{
    public int Number { get; set; }
    public string? IssuedDate { get; set; }
    public string? DueDate { get; set; }
    public string? SupplierName { get; set; }
    public string? SupplierCode { get; set; }
    public string? SupplierReference { get; set; }
    public RestCurrencyResponse? Currency { get; set; }
    public List<PurchaseLineItemResponse>? LineItems { get; set; }
    public List<PurchasePaymentLineResponse>? PaymentLines { get; set; }
    public int? ProjectNumber { get; set; }
    public string? AdditionalFieldValue { get; set; }
    public bool? IsCISReverseCharge { get; set; }
    public string? Type { get; set; }
    public string? Status { get; set; }

    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public decimal? GrossAmount { get; set; }

    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public decimal? DueAmount { get; set; }

    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public decimal? TotalPaidAmount { get; set; }

    public int? FileCount { get; set; }
}

public sealed class BankTransactionListQuery
{
    public int? Page { get; set; }
    public int? PerPage { get; set; }
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public int? SortColumn { get; set; }
    public string? Order { get; set; }
    /// <summary>Status filter, for example <c>Unallocated</c>.</summary>
    public string? FilterBy { get; set; }
    public bool? IncludeBulk { get; set; }
    public string? SearchText { get; set; }
    public bool? IncludeFeedMatched { get; set; }
    public bool? IncludeJournals { get; set; }
    public bool? IncludeNominalCodeBankTransactions { get; set; }
    public bool? Reconciled { get; set; }
}

public sealed class BankReconciliationListItem
{
    public int Id { get; set; }
    public string? StartDate { get; set; }
    public string? EndDate { get; set; }

    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public decimal? StartBalance { get; set; }

    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public decimal? EndBalance { get; set; }

    public string? Status { get; set; }
    public bool? LatestReconciliation { get; set; }
    public bool? ReconciliationDone { get; set; }
}

public sealed class BankReconciliationMetadata
{
    public string? NextReconciliationStartDate { get; set; }

    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public decimal? NextReconciliationStartBalance { get; set; }

    public int? InProgressReconciliationId { get; set; }
}

public sealed class MultipartUploadPlan
{
    public string Endpoint { get; set; } = "";
    public string FileName { get; set; } = "";
    public string ContentType { get; set; } = "application/octet-stream";
    public long ByteLength { get; set; }
    public string FormFieldName { get; set; } = "file";
}
