using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using KashFlow;
using KashflowMCP.Rest;
using ModelContextProtocol.Server;

namespace KashflowMCP.Tools;

/// <summary>
/// REST-backed tools. Registered only when <c>KashFlow__MemorableWord</c> (or
/// <c>KASHFLOW_MEMORABLE_WORD</c>) is configured. Writes still require
/// <c>KashFlow__ReadOnly=false</c> unless <c>dryRun=true</c>.
/// </summary>
[McpServerToolType]
public sealed class RestTools(KashFlowSession session, McpRuntimeOptions runtime)
{
    private static readonly JsonSerializerOptions RequestJsonOptions = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = null,
    };

    [McpServerTool(Name = "pay_purchases_bulk"), Description("Pay several purchase receipts as one bank-feed debit via KashFlow REST POST /v2/purchases/bulk/payments (UI New batch payment). SOAP InsertReceiptPayment cannot do this — each SOAP payment creates its own bank line. Validates that allocations sum exactly to totalAmount, receipt numbers are unique, each receipt exists, and no allocation exceeds that receipt's GBP outstanding (book GBP value minus AmountPaid; for FX receipts book value is (net+VAT)/ExchangeRate). Requires KashFlow__MemorableWord (REST session auth). Requires KashFlow__ReadOnly=false unless dryRun=true. dryRun returns the exact REST request body without writing. Example: Starling 1206, 2026-09-10, £91.28 = KF04199 £2.40 + KF04200 £88.88.")]
    public async Task<string> PayPurchasesBulkAsync(
        [Description("Payment date (ISO-8601). Sent as Date (yyyy-MM-dd).")] DateTime paymentDate,
        [Description("Bank amount being paid. Must equal the sum of allocations to the penny.")] decimal totalAmount,
        [Description("Per-receipt splits. Each amount must be greater than 0. Sent as PaymentItems[{ObjectNumber, Amount}].")] List<ReceiptPaymentAllocationInput> allocations,
        [Description("Bank account id from get_bank_accounts (accountId). Sent as AccountId. Optional when bankAccountCode is set.")] int? bankAccountId = null,
        [Description("Bank account nominal code from get_bank_accounts (accountCode), for example 1206 Starling. Used only when bankAccountId is omitted.")] int? bankAccountCode = null,
        [Description("Payment method id from get_receipt_payment_methods (methodId). Sent as Method. Optional when paymentMethodName is set.")] int? paymentMethodId = null,
        [Description("Payment method name from get_receipt_payment_methods. Used only when paymentMethodId is omitted.")] string? paymentMethodName = null,
        [Description("Optional note. Sent as Comment.")] string? note = null,
        [Description("When true, validate and return the exact REST request body without writing. Default false.")] bool dryRun = false,
        CancellationToken cancellationToken = default)
    {
        if (!dryRun)
            ToolGuard.EnsureWritable(runtime);
        ArgumentNullException.ThrowIfNull(allocations);

        var rest = RequireRest("pay_purchases_bulk");
        var methodId = await ResolveMethodAsync(paymentMethodId, paymentMethodName, cancellationToken);
        var accountId = await ResolveAccountAsync(bankAccountId, bankAccountCode, cancellationToken);

        var receipts = await LoadReceiptsAsync(allocations.Select(a => a.ReceiptNumber), cancellationToken);
        var planned = PayPurchasesBulk.Build(
            paymentDate,
            totalAmount,
            allocations.Select(item => new PayPurchasesBulk.Allocation(item.ReceiptNumber, item.Amount)).ToList(),
            receipts,
            accountId,
            methodId,
            note);

        var requestBodyJson = PayPurchasesBulk.SerializeRequestBody(planned.Request);
        if (dryRun)
        {
            return JsonResults.Serialize(new
            {
                dryRun = true,
                endpoint = "POST https://api.kashflow.com/v2/purchases/bulk/payments",
                totalAmount,
                payAccount = accountId,
                payMethod = methodId,
                request = JsonSerializer.Deserialize<JsonElement>(requestBodyJson),
                requestBodyJson,
                allocations = planned.Planned.Select(item => new
                {
                    receiptNumber = item.ReceiptNumber,
                    amount = item.Amount,
                    outstandingBefore = item.OutstandingBefore,
                }),
            });
        }

        var response = await rest.PostPurchasesBulkPaymentAsync(planned.Request, cancellationToken);
        return JsonResults.Serialize(new
        {
            dryRun = false,
            bulkPaymentNumber = response.Number,
            totalAmount,
            payAccount = accountId,
            payMethod = methodId,
            date = response.Date ?? planned.Request.Date,
            comment = response.Comment,
            reconciled = response.Reconciled,
            paymentItems = response.PaymentItems?.Select(item => new
            {
                receiptNumber = item.ObjectNumber,
                amount = item.Amount,
            }),
            requestBodyJson,
        });
    }

    [McpServerTool(Name = "link_bank_transactions_to_purchases"), Description("Link existing bank-feed transactions to purchase receipts via KashFlow REST PUT /v2/bankaccounts/{id}/transactionlist. Does not create a new bank line. Swagger: assigns bank transactions to purchases (and invoices) and then deletes the bank transaction (converts it into a receipt payment). Requires memorable word. Requires ReadOnly=false unless dryRun=true.")]
    public async Task<string> LinkBankTransactionsToPurchasesAsync(
        [Description("Assignments. Each item: bank transaction id, purchase receipt number, and payment method.")] List<BankTransactionPurchaseLinkInput> assignments,
        [Description("Bank account id from get_bank_accounts. Optional when bankAccountCode is set.")] int? bankAccountId = null,
        [Description("Bank account nominal code from get_bank_accounts. Used only when bankAccountId is omitted.")] int? bankAccountCode = null,
        [Description("Default payment method id when an assignment omits paymentMethodId. Optional when paymentMethodName is set.")] int? paymentMethodId = null,
        [Description("Default payment method name when an assignment omits paymentMethodId. Used only when paymentMethodId is omitted.")] string? paymentMethodName = null,
        [Description("When true, return the exact REST request body without writing. Default false.")] bool dryRun = false,
        CancellationToken cancellationToken = default)
    {
        if (!dryRun)
            ToolGuard.EnsureWritable(runtime);
        ArgumentNullException.ThrowIfNull(assignments);
        if (assignments.Count == 0)
            throw new ArgumentException("assignments must contain at least one item.");

        var rest = RequireRest("link_bank_transactions_to_purchases");
        var accountId = await ResolveAccountAsync(bankAccountId, bankAccountCode, cancellationToken);
        var defaultMethod = paymentMethodId is int mid && mid > 0
            ? mid
            : !string.IsNullOrWhiteSpace(paymentMethodName) || paymentMethodId is not null
                ? await ResolveMethodAsync(paymentMethodId, paymentMethodName, cancellationToken)
                : 0;

        var purchases = new List<BankTransactionAssignToEntity>();
        foreach (var item in assignments)
        {
            if (item.TransactionId <= 0)
                throw new ArgumentException("Each assignment needs transactionId greater than 0.");
            if (item.ReceiptNumber <= 0)
                throw new ArgumentException("Each assignment needs receiptNumber greater than 0.");
            var method = item.PaymentMethodId is int m && m > 0
                ? m
                : defaultMethod;
            if (method <= 0)
            {
                throw new ArgumentException(
                    "Pass paymentMethodId (or paymentMethodName) on the tool or on each assignment.");
            }

            purchases.Add(new BankTransactionAssignToEntity
            {
                TransactionId = item.TransactionId,
                Number = item.ReceiptNumber,
                PaymentMethod = method,
            });
        }

        var request = new BankTransactionBulkAssignRequest
        {
            AssignToPurchases = purchases,
        };
        var requestBodyJson = JsonSerializer.Serialize(request, RequestJsonOptions);
        var endpoint = $"PUT https://api.kashflow.com/v2/bankaccounts/{accountId}/transactionlist";

        if (dryRun)
        {
            return JsonResults.Serialize(new
            {
                dryRun = true,
                endpoint,
                bankAccountId = accountId,
                request = JsonSerializer.Deserialize<JsonElement>(requestBodyJson),
                requestBodyJson,
                note = "Swagger: assigns then deletes the bank transaction (no new bank line).",
            });
        }

        var response = await rest.PutBankTransactionListAsync(accountId, request, cancellationToken);
        return JsonResults.Serialize(new
        {
            dryRun = false,
            endpoint,
            bankAccountId = accountId,
            response,
            requestBodyJson,
        });
    }

    [McpServerTool(Name = "get_purchases_bulk_payment"), Description("Get a bulk purchase payment via KashFlow REST GET /v2/purchases/bulk/payments/{number}. Requires memorable word.")]
    public async Task<string> GetPurchasesBulkPaymentAsync(
        [Description("Bulk payment number returned by pay_purchases_bulk.")] int bulkPaymentNumber,
        CancellationToken cancellationToken = default)
    {
        var rest = RequireRest("get_purchases_bulk_payment");
        if (bulkPaymentNumber <= 0)
            throw new ArgumentException("bulkPaymentNumber must be greater than 0.");
        var response = await rest.GetPurchasesBulkPaymentAsync(bulkPaymentNumber, cancellationToken);
        return JsonResults.Serialize(new { bulkPayment = response });
    }

    [McpServerTool(Name = "update_purchases_bulk_payment"), Description("Edit a bulk purchase payment via KashFlow REST PUT /v2/purchases/bulk/payments/{number}. GET then merge so omitted fields keep current values. Requires memorable word. Requires ReadOnly=false unless dryRun=true.")]
    public async Task<string> UpdatePurchasesBulkPaymentAsync(
        [Description("Bulk payment number to edit.")] int bulkPaymentNumber,
        [Description("Optional new payment date (ISO-8601). Sent as Date.")] DateTime? paymentDate = null,
        [Description("Optional new note. Sent as Comment. Pass empty string to clear.")] string? note = null,
        [Description("Optional new bank account id. Sent as AccountId.")] int? bankAccountId = null,
        [Description("Optional new bank account code. Used only when bankAccountId is omitted.")] int? bankAccountCode = null,
        [Description("Optional new payment method id. Sent as Method.")] int? paymentMethodId = null,
        [Description("Optional new payment method name. Used only when paymentMethodId is omitted.")] string? paymentMethodName = null,
        [Description("Optional replacement PaymentItems. When set, replaces the whole split list.")] List<ReceiptPaymentAllocationInput>? allocations = null,
        [Description("When true, return the exact REST request body without writing. Default false.")] bool dryRun = false,
        CancellationToken cancellationToken = default)
    {
        if (!dryRun)
            ToolGuard.EnsureWritable(runtime);
        var rest = RequireRest("update_purchases_bulk_payment");
        if (bulkPaymentNumber <= 0)
            throw new ArgumentException("bulkPaymentNumber must be greater than 0.");

        var current = await rest.GetPurchasesBulkPaymentAsync(bulkPaymentNumber, cancellationToken);
        var request = new BulkPurchasePaymentRequest
        {
            Number = bulkPaymentNumber,
            Date = paymentDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? current.Date ?? "",
            Comment = note is null ? current.Comment : (string.IsNullOrWhiteSpace(note) ? null : note.Trim()),
            AccountId = current.AccountId,
            Method = current.Method,
            Reconciled = current.Reconciled,
            BankReconciliationId = current.BankReconciliationId,
            BFSTransactionId = current.BFSTransactionId,
            PaymentItems = (current.PaymentItems ?? [])
                .Select(item => new BulkPurchasePaymentItem
                {
                    Id = item.Id,
                    ObjectNumber = item.ObjectNumber,
                    Amount = item.Amount,
                    PaidDate = item.PaidDate,
                    ContactName = item.ContactName,
                    ContactCode = item.ContactCode,
                })
                .ToList(),
        };

        if (bankAccountId is int || bankAccountCode is int)
            request.AccountId = await ResolveAccountAsync(bankAccountId, bankAccountCode, cancellationToken);
        if (paymentMethodId is int || !string.IsNullOrWhiteSpace(paymentMethodName))
            request.Method = await ResolveMethodAsync(paymentMethodId, paymentMethodName, cancellationToken);

        if (allocations is not null)
        {
            if (allocations.Count == 0)
                throw new ArgumentException("allocations, when set, must contain at least one item.");
            request.PaymentItems = allocations
                .Select(item =>
                {
                    if (item.ReceiptNumber <= 0 || item.Amount <= 0)
                        throw new ArgumentException("Each allocation needs receiptNumber and amount greater than 0.");
                    return new BulkPurchasePaymentItem
                    {
                        ObjectNumber = item.ReceiptNumber,
                        Amount = decimal.Round(item.Amount, 2, MidpointRounding.AwayFromZero),
                    };
                })
                .ToList();
        }

        if (string.IsNullOrWhiteSpace(request.Date))
            throw new InvalidOperationException("Bulk payment has no Date; pass paymentDate.");

        var requestBodyJson = JsonSerializer.Serialize(request, RequestJsonOptions);
        var endpoint = $"PUT https://api.kashflow.com/v2/purchases/bulk/payments/{bulkPaymentNumber}";
        if (dryRun)
        {
            return JsonResults.Serialize(new
            {
                dryRun = true,
                endpoint,
                request = JsonSerializer.Deserialize<JsonElement>(requestBodyJson),
                requestBodyJson,
            });
        }

        var response = await rest.PutPurchasesBulkPaymentAsync(bulkPaymentNumber, request, cancellationToken);
        return JsonResults.Serialize(new
        {
            dryRun = false,
            endpoint,
            bulkPayment = response,
            requestBodyJson,
        });
    }

    [McpServerTool(Name = "delete_purchases_bulk_payment"), Description("Delete a bulk purchase payment via KashFlow REST DELETE /v2/purchases/bulk/payments/{number}. Requires memorable word. Requires ReadOnly=false unless dryRun=true. dryRun returns the exact request (method + URL; no body).")]
    public async Task<string> DeletePurchasesBulkPaymentAsync(
        [Description("Bulk payment number to delete.")] int bulkPaymentNumber,
        [Description("When true, return the exact REST request without writing. Default false.")] bool dryRun = false,
        CancellationToken cancellationToken = default)
    {
        if (!dryRun)
            ToolGuard.EnsureWritable(runtime);
        var rest = RequireRest("delete_purchases_bulk_payment");
        if (bulkPaymentNumber <= 0)
            throw new ArgumentException("bulkPaymentNumber must be greater than 0.");

        var endpoint = $"DELETE https://api.kashflow.com/v2/purchases/bulk/payments/{bulkPaymentNumber}";
        var request = new { method = "DELETE", path = $"purchases/bulk/payments/{bulkPaymentNumber}", bulkPaymentNumber };
        var requestBodyJson = JsonSerializer.Serialize(request, RequestJsonOptions);

        if (dryRun)
        {
            return JsonResults.Serialize(new
            {
                dryRun = true,
                endpoint,
                request = JsonSerializer.Deserialize<JsonElement>(requestBodyJson),
                requestBodyJson,
            });
        }

        await rest.DeletePurchasesBulkPaymentAsync(bulkPaymentNumber, cancellationToken);
        return JsonResults.Serialize(new
        {
            dryRun = false,
            endpoint,
            deleted = true,
            bulkPaymentNumber,
            requestBodyJson,
        });
    }

    [McpServerTool(Name = "update_purchase_payment_lines"), Description("Edit purchase receipt PaymentLines in place via KashFlow REST GET+PUT /v2/purchases/{number}. Read-modify-write: copies LineItems, supplier, currency, and dates from the current receipt so only PaymentLines change. Use BankTransactionId to link a payment line to a bank transaction. Requires memorable word. Requires ReadOnly=false unless dryRun=true.")]
    public async Task<string> UpdatePurchasePaymentLinesAsync(
        [Description("Purchase receipt number.")] int receiptNumber,
        [Description("Payment line edits. With id: patch that existing line. Without id: append a new line (date, amount, accountId, method required).")] List<PurchasePaymentLineEditInput> paymentLines,
        [Description("When true, replace the entire PaymentLines array with paymentLines. Default false (merge/append).")] bool replaceAll = false,
        [Description("When true, return the exact REST PUT body without writing. Default false.")] bool dryRun = false,
        CancellationToken cancellationToken = default)
    {
        if (!dryRun)
            ToolGuard.EnsureWritable(runtime);
        var rest = RequireRest("update_purchase_payment_lines");
        if (receiptNumber <= 0)
            throw new ArgumentException("receiptNumber must be greater than 0.");
        ArgumentNullException.ThrowIfNull(paymentLines);
        if (paymentLines.Count == 0)
            throw new ArgumentException("paymentLines must contain at least one item.");

        var existing = await rest.GetPurchaseAsync(receiptNumber, cancellationToken);
        var edits = paymentLines.Select(line => new PurchasePaymentLines.PaymentLineEdit
        {
            Id = line.Id,
            Date = line.Date,
            Amount = line.Amount,
            AccountId = line.AccountId,
            Method = line.Method,
            Note = line.Note,
            BankTransactionId = line.BankTransactionId,
            BFSTransactionId = line.BFSTransactionId,
            BulkId = line.BulkId,
            BankReconciliationId = line.BankReconciliationId,
            ResetBankReconciliation = line.ResetBankReconciliation,
        }).ToList();

        var request = PurchasePaymentLines.BuildUpdateRequest(existing, edits, replaceAll);
        var requestBodyJson = PurchasePaymentLines.SerializeRequestBody(request);
        var endpoint = $"PUT https://api.kashflow.com/v2/purchases/{receiptNumber}";

        if (dryRun)
        {
            return JsonResults.Serialize(new
            {
                dryRun = true,
                endpoint,
                receiptNumber,
                replaceAll,
                request = JsonSerializer.Deserialize<JsonElement>(requestBodyJson),
                requestBodyJson,
            });
        }

        var response = await rest.PutPurchaseAsync(receiptNumber, request, cancellationToken);
        return JsonResults.Serialize(new
        {
            dryRun = false,
            endpoint,
            receiptNumber,
            purchase = response,
            requestBodyJson,
        });
    }

    [McpServerTool(Name = "list_purchase_files"), Description("List files attached to a purchase receipt via KashFlow REST GET /v2/purchases/{number}/files. Requires memorable word.")]
    public async Task<string> ListPurchaseFilesAsync(
        [Description("Purchase receipt number.")] int receiptNumber,
        [Description("Optional thumbnail size query value.")] string? thumbnailSize = null,
        CancellationToken cancellationToken = default)
    {
        var rest = RequireRest("list_purchase_files");
        if (receiptNumber <= 0)
            throw new ArgumentException("receiptNumber must be greater than 0.");
        var files = await rest.ListPurchaseFilesAsync(receiptNumber, thumbnailSize, cancellationToken);
        return JsonResults.Serialize(new { receiptNumber, files });
    }

    [McpServerTool(Name = "attach_purchase_file"), Description("Attach a file to a purchase receipt via KashFlow REST POST /v2/files?objectType=purchases then POST /v2/purchases/{number}/files. Accepts a local file path or base64 content (max 10 MiB). Multipart field name is file (swagger body undocumented). Requires memorable word. Requires ReadOnly=false unless dryRun=true.")]
    public async Task<string> AttachPurchaseFileAsync(
        [Description("Purchase receipt number.")] int receiptNumber,
        [Description("Local file path to upload. Ignored when contentBase64 is set.")] string? filePath = null,
        [Description("Base64 file bytes. When set, fileName is required.")] string? contentBase64 = null,
        [Description("File name for the upload. Required with contentBase64; otherwise taken from filePath.")] string? fileName = null,
        [Description("Optional Content-Type (default application/octet-stream).")] string? contentType = null,
        [Description("When true, return the upload plan without writing. Default false.")] bool dryRun = false,
        CancellationToken cancellationToken = default)
    {
        if (!dryRun)
            ToolGuard.EnsureWritable(runtime);
        var rest = RequireRest("attach_purchase_file");
        if (receiptNumber <= 0)
            throw new ArgumentException("receiptNumber must be greater than 0.");

        byte[] bytes;
        string resolvedName;
        if (!string.IsNullOrWhiteSpace(contentBase64))
        {
            if (string.IsNullOrWhiteSpace(fileName))
                throw new ArgumentException("fileName is required when contentBase64 is set.");
            try
            {
                bytes = Convert.FromBase64String(contentBase64.Trim());
            }
            catch (FormatException ex)
            {
                throw new ArgumentException("contentBase64 is not valid base64.", ex);
            }

            resolvedName = Path.GetFileName(fileName.Trim());
        }
        else if (!string.IsNullOrWhiteSpace(filePath))
        {
            var path = filePath.Trim();
            if (!File.Exists(path))
                throw new FileNotFoundException($"File not found: {path}", path);
            var info = new FileInfo(path);
            if (info.Length > KashFlowRestClient.MaxUploadBytes)
            {
                throw new ArgumentException(
                    $"File is {info.Length} bytes; max upload size is {KashFlowRestClient.MaxUploadBytes} bytes (10 MiB).");
            }

            bytes = await File.ReadAllBytesAsync(path, cancellationToken);
            resolvedName = string.IsNullOrWhiteSpace(fileName)
                ? info.Name
                : Path.GetFileName(fileName.Trim());
        }
        else
        {
            throw new ArgumentException("Pass filePath or contentBase64.");
        }

        KashFlowRestClient.ValidateUpload(resolvedName, bytes);
        var plan = new MultipartUploadPlan
        {
            Endpoint = $"POST https://api.kashflow.com/v2/files?objectType=purchases then POST https://api.kashflow.com/v2/purchases/{receiptNumber}/files",
            FileName = resolvedName,
            ContentType = string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType.Trim(),
            ByteLength = bytes.Length,
            FormFieldName = "file",
        };
        var requestBodyJson = JsonSerializer.Serialize(plan, RequestJsonOptions);

        if (dryRun)
        {
            return JsonResults.Serialize(new
            {
                dryRun = true,
                receiptNumber,
                request = JsonSerializer.Deserialize<JsonElement>(requestBodyJson),
                requestBodyJson,
                note = "Binary body omitted from dryRun; multipart field name is file.",
            });
        }

        var response = await rest.UploadAndAttachPurchaseFileAsync(
            receiptNumber,
            resolvedName,
            bytes,
            plan.ContentType,
            cancellationToken);
        return JsonResults.Serialize(new
        {
            dryRun = false,
            receiptNumber,
            fileName = resolvedName,
            byteLength = bytes.Length,
            response,
            requestBodyJson,
        });
    }

    [McpServerTool(Name = "delete_purchase_file"), Description("Delete a file from a purchase receipt via KashFlow REST DELETE /v2/purchases/{number}/file?fileName=. Requires memorable word. Requires ReadOnly=false unless dryRun=true.")]
    public async Task<string> DeletePurchaseFileAsync(
        [Description("Purchase receipt number.")] int receiptNumber,
        [Description("File name to delete (from list_purchase_files).")] string fileName,
        [Description("When true, return the exact REST request without writing. Default false.")] bool dryRun = false,
        CancellationToken cancellationToken = default)
    {
        if (!dryRun)
            ToolGuard.EnsureWritable(runtime);
        var rest = RequireRest("delete_purchase_file");
        if (receiptNumber <= 0)
            throw new ArgumentException("receiptNumber must be greater than 0.");
        if (string.IsNullOrWhiteSpace(fileName))
            throw new ArgumentException("fileName is required.");

        var trimmed = fileName.Trim();
        var endpoint = $"DELETE https://api.kashflow.com/v2/purchases/{receiptNumber}/file?fileName={Uri.EscapeDataString(trimmed)}";
        var request = new { method = "DELETE", path = $"purchases/{receiptNumber}/file", fileName = trimmed };
        var requestBodyJson = JsonSerializer.Serialize(request, RequestJsonOptions);

        if (dryRun)
        {
            return JsonResults.Serialize(new
            {
                dryRun = true,
                endpoint,
                request = JsonSerializer.Deserialize<JsonElement>(requestBodyJson),
                requestBodyJson,
            });
        }

        await rest.DeletePurchaseFileAsync(receiptNumber, trimmed, cancellationToken);
        return JsonResults.Serialize(new
        {
            dryRun = false,
            endpoint,
            deleted = true,
            receiptNumber,
            fileName = trimmed,
            requestBodyJson,
        });
    }

    [McpServerTool(Name = "list_bank_reconciliations"), Description("List bank reconciliations for a bank account via KashFlow REST GET /v2/bankaccounts/{id}/reconciliations. Requires memorable word.")]
    public async Task<string> ListBankReconciliationsAsync(
        [Description("Bank account id from get_bank_accounts. Optional when bankAccountCode is set.")] int? bankAccountId = null,
        [Description("Bank account nominal code. Used only when bankAccountId is omitted.")] int? bankAccountCode = null,
        [Description("Page number.")] int? page = null,
        [Description("Page size (perPage).")] int? perPage = null,
        [Description("Sort order: 0 = Asc, 1 = Desc.")] int? sortOrder = null,
        CancellationToken cancellationToken = default)
    {
        var rest = RequireRest("list_bank_reconciliations");
        var accountId = await ResolveAccountAsync(bankAccountId, bankAccountCode, cancellationToken);
        var payload = await rest.GetBankReconciliationsAsync(accountId, page, perPage, sortOrder, cancellationToken);
        return JsonResults.Serialize(new { bankAccountId = accountId, reconciliations = payload });
    }

    [McpServerTool(Name = "get_bank_reconciliation_metadata"), Description("Get next reconciliation start date/balance for a bank account via KashFlow REST GET /v2/bankaccounts/{id}/reconciliations/metadata. Requires memorable word.")]
    public async Task<string> GetBankReconciliationMetadataAsync(
        [Description("Bank account id from get_bank_accounts. Optional when bankAccountCode is set.")] int? bankAccountId = null,
        [Description("Bank account nominal code. Used only when bankAccountId is omitted.")] int? bankAccountCode = null,
        CancellationToken cancellationToken = default)
    {
        var rest = RequireRest("get_bank_reconciliation_metadata");
        var accountId = await ResolveAccountAsync(bankAccountId, bankAccountCode, cancellationToken);
        var meta = await rest.GetBankReconciliationMetadataAsync(accountId, cancellationToken);
        return JsonResults.Serialize(new { bankAccountId = accountId, metadata = meta });
    }

    [McpServerTool(Name = "get_bank_reconciliation"), Description("Get one bank reconciliation via KashFlow REST GET /v2/bankaccounts/{id}/reconciliations/{reconciliationId}. Requires memorable word.")]
    public async Task<string> GetBankReconciliationAsync(
        [Description("Reconciliation id.")] int reconciliationId,
        [Description("Bank account id from get_bank_accounts. Optional when bankAccountCode is set.")] int? bankAccountId = null,
        [Description("Bank account nominal code. Used only when bankAccountId is omitted.")] int? bankAccountCode = null,
        [Description("When true, exclude transactions (swagger query excludetransations).")] bool? excludeTransactions = null,
        CancellationToken cancellationToken = default)
    {
        var rest = RequireRest("get_bank_reconciliation");
        if (reconciliationId <= 0)
            throw new ArgumentException("reconciliationId must be greater than 0.");
        var accountId = await ResolveAccountAsync(bankAccountId, bankAccountCode, cancellationToken);
        var payload = await rest.GetBankReconciliationAsync(
            accountId, reconciliationId, excludeTransactions, cancellationToken);
        return JsonResults.Serialize(new
        {
            bankAccountId = accountId,
            reconciliationId,
            reconciliation = payload,
        });
    }

    [McpServerTool(Name = "list_bank_account_transactions"), Description("List bank account transactions via KashFlow REST GET /v2/bankaccounts/{id}/transactions with richer filters than SOAP search. Use filterBy=Unallocated and includeFeedMatched to find unmatched feed lines. Requires memorable word.")]
    public async Task<string> ListBankAccountTransactionsAsync(
        [Description("Bank account id from get_bank_accounts. Optional when bankAccountCode is set.")] int? bankAccountId = null,
        [Description("Bank account nominal code. Used only when bankAccountId is omitted.")] int? bankAccountCode = null,
        [Description("Page number (default 1).")] int? page = null,
        [Description("Records per page (default 10).")] int? perPage = null,
        [Description("Only transactions on/after this date.")] DateTime? startDate = null,
        [Description("Only transactions on/before this date.")] DateTime? endDate = null,
        [Description("Status filter, for example Unallocated.")] string? filterBy = null,
        [Description("Whether to include matched feed transactions (includefeedmatched).")] bool? includeFeedMatched = null,
        [Description("Whether to include bulk payment rows.")] bool? includeBulk = null,
        [Description("Whether to include journals.")] bool? includeJournals = null,
        [Description("Filter reconciled (true) / unreconciled (false); omit for both.")] bool? reconciled = null,
        [Description("Free-text search.")] string? searchText = null,
        [Description("Sort column: 1=Date, 2=PaidIn, 3=PaidOut, 4=TransactionType, 5=Comment, 6=Name.")] int? sortColumn = null,
        [Description("Sort order string.")] string? order = null,
        CancellationToken cancellationToken = default)
    {
        var rest = RequireRest("list_bank_account_transactions");
        var accountId = await ResolveAccountAsync(bankAccountId, bankAccountCode, cancellationToken);
        var query = new BankTransactionListQuery
        {
            Page = page,
            PerPage = perPage,
            StartDate = startDate,
            EndDate = endDate,
            FilterBy = filterBy,
            IncludeFeedMatched = includeFeedMatched,
            IncludeBulk = includeBulk,
            IncludeJournals = includeJournals,
            Reconciled = reconciled,
            SearchText = searchText,
            SortColumn = sortColumn,
            Order = order,
        };
        var path = KashFlowRestClient.BuildBankTransactionsUrl(accountId, query);
        var payload = await rest.GetBankAccountTransactionsAsync(accountId, query, cancellationToken);
        return JsonResults.Serialize(new
        {
            bankAccountId = accountId,
            endpoint = "GET https://api.kashflow.com/v2/" + path,
            transactions = payload,
        });
    }

    [McpServerTool(Name = "list_bank_feed_transactions"), Description("List bank-feed import transaction details via KashFlow REST GET /v2/bankaccounts/{id}/transactions/bfstransactions. Requires memorable word.")]
    public async Task<string> ListBankFeedTransactionsAsync(
        [Description("Bank account id from get_bank_accounts. Optional when bankAccountCode is set.")] int? bankAccountId = null,
        [Description("Bank account nominal code. Used only when bankAccountId is omitted.")] int? bankAccountCode = null,
        CancellationToken cancellationToken = default)
    {
        var rest = RequireRest("list_bank_feed_transactions");
        var accountId = await ResolveAccountAsync(bankAccountId, bankAccountCode, cancellationToken);
        var payload = await rest.GetBankFeedTransactionDetailsAsync(accountId, cancellationToken);
        return JsonResults.Serialize(new { bankAccountId = accountId, feedTransactions = payload });
    }

    private KashFlowRestClient RequireRest(string toolName)
        => session.RestClient
            ?? throw new InvalidOperationException(
                $"{toolName} needs KashFlow__MemorableWord (or KASHFLOW_MEMORABLE_WORD) for REST session auth. " +
                "SOAP username/password alone are not enough. The memorable word is never logged.");

    private async Task<int> ResolveAccountAsync(
        int? bankAccountId,
        int? bankAccountCode,
        CancellationToken cancellationToken)
        => ReceiptPaymentMapper.ResolveBankAccountId(
            bankAccountId,
            bankAccountCode,
            await session.Client.GetBankAccountsAsync(cancellationToken));

    private async Task<int> ResolveMethodAsync(
        int? paymentMethodId,
        string? paymentMethodName,
        CancellationToken cancellationToken)
        => ReceiptPaymentMapper.ResolvePaymentMethodId(
            paymentMethodId,
            paymentMethodName,
            await session.Client.GetRecPayMethodsAsync(cancellationToken));

    private async Task<Dictionary<int, Receipt2>> LoadReceiptsAsync(
        IEnumerable<int> receiptNumbers,
        CancellationToken cancellationToken)
    {
        var receipts = new Dictionary<int, Receipt2>();
        foreach (var number in receiptNumbers.Distinct())
        {
            if (number <= 0 || receipts.ContainsKey(number))
                continue;
            try
            {
                var receipt = await session.Client.GetReceipt2Async(number, cancellationToken);
                if (receipt is not null)
                    receipts[number] = receipt;
            }
            catch (KashFlowException)
            {
                // Validate reports missing receipts.
            }
        }

        return receipts;
    }
}

public sealed class BankTransactionPurchaseLinkInput
{
    [Description("Bank transaction id to assign (and remove as a plain bank line).")]
    public int TransactionId { get; set; }

    [Description("Purchase receipt number to pay.")]
    public int ReceiptNumber { get; set; }

    [Description("Optional payment method id for this assignment. Falls back to the tool-level method.")]
    public int? PaymentMethodId { get; set; }
}

public sealed class PurchasePaymentLineEditInput
{
    [Description("Existing payment line id to patch. Omit to append a new line.")]
    public long? Id { get; set; }

    [Description("Payment date (ISO-8601). Required for new lines.")]
    public DateTime? Date { get; set; }

    [Description("Payment amount. Required for new lines.")]
    public decimal? Amount { get; set; }

    [Description("Bank account id (AccountId). Required for new lines.")]
    public int? AccountId { get; set; }

    [Description("Payment method id (Method). Required for new lines.")]
    public long? Method { get; set; }

    [Description("Payment note.")]
    public string? Note { get; set; }

    [Description("Link this payment line to a bank transaction id.")]
    public long? BankTransactionId { get; set; }

    [Description("Optional bank-feed transaction id.")]
    public long? BFSTransactionId { get; set; }

    [Description("Optional bulk payment id.")]
    public int? BulkId { get; set; }

    [Description("Optional bank reconciliation id.")]
    public int? BankReconciliationId { get; set; }

    [Description("When true, reset bank reconciliation on this line.")]
    public bool? ResetBankReconciliation { get; set; }
}
