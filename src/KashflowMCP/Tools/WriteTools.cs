using System.ComponentModel;
using KashFlow;
using ModelContextProtocol.Server;

namespace KashflowMCP.Tools;

/// <summary>Write tools — registered only when KashFlow__ReadOnly is false.</summary>
[McpServerToolType]
public sealed class WriteTools(KashFlowSession session, McpRuntimeOptions runtime)
{
    [McpServerTool(Name = "insert_customer"), Description("Insert a customer. Requires KashFlow__ReadOnly=false. Pass structured customer fields, or customerJson with a full Customer payload (including extra text and checkbox fields). When customerJson is set, the structured fields are ignored.")]
    public async Task<string> InsertCustomer(
        [Description("Structured customer. Ignored when customerJson is set.")] InsertCustomerInput? customer = null,
        [Description("Legacy full Customer JSON string. When set, this is used instead of the structured customer.")] string? customerJson = null,
        CancellationToken cancellationToken = default)
    {
        ToolGuard.EnsureWritable(runtime);
        var mapped = WriteInputMapper.ResolveCustomer(customer, customerJson);
        var id = await session.Client.InsertCustomerAsync(mapped, cancellationToken);
        return JsonResults.Serialize(new { customerId = id });
    }

    [McpServerTool(Name = "insert_supplier"), Description("Insert a supplier. Requires KashFlow__ReadOnly=false. Pass structured supplier fields, or supplierJson with a full Supplier payload. When supplierJson is set, the structured fields are ignored.")]
    public async Task<string> InsertSupplier(
        [Description("Structured supplier. Ignored when supplierJson is set.")] InsertSupplierInput? supplier = null,
        [Description("Legacy full Supplier JSON string. When set, this is used instead of the structured supplier.")] string? supplierJson = null,
        CancellationToken cancellationToken = default)
    {
        ToolGuard.EnsureWritable(runtime);
        var mapped = WriteInputMapper.ResolveSupplier(supplier, supplierJson);
        var id = await session.Client.InsertSupplierAsync(mapped, cancellationToken);
        return JsonResults.Serialize(new { supplierId = id });
    }

    [McpServerTool(Name = "insert_invoice"), Description("Insert a sales invoice. Requires KashFlow__ReadOnly=false. Pass structured invoice fields (customer and lines), or invoiceJson with a full Invoice payload. When invoiceJson is set, the structured fields are ignored. Net and VAT totals are summed from the lines when omitted.")]
    public async Task<string> InsertInvoice(
        [Description("Structured invoice. Ignored when invoiceJson is set.")] InsertInvoiceInput? invoice = null,
        [Description("Legacy full Invoice JSON string. When set, this is used instead of the structured invoice.")] string? invoiceJson = null,
        CancellationToken cancellationToken = default)
    {
        ToolGuard.EnsureWritable(runtime);
        var mapped = WriteInputMapper.ResolveInvoice(invoice, invoiceJson);
        var id = await session.Client.InsertInvoiceAsync(mapped, cancellationToken);
        return JsonResults.Serialize(new { invoiceId = id });
    }

    [McpServerTool(Name = "insert_receipt2"), Description("Insert a purchase receipt (Receipt2). Requires KashFlow__ReadOnly=false. Pass structured receipt fields (supplier and lines), or receiptJson with a full Receipt2 payload. When receiptJson is set, the structured fields are ignored.")]
    public async Task<string> InsertReceipt2(
        [Description("Structured receipt. Ignored when receiptJson is set.")] InsertReceiptInput? receipt = null,
        [Description("Legacy full Receipt2 JSON string. When set, this is used instead of the structured receipt.")] string? receiptJson = null,
        CancellationToken cancellationToken = default)
    {
        ToolGuard.EnsureWritable(runtime);
        var mapped = WriteInputMapper.ResolveReceipt(receipt, receiptJson);
        var id = await session.Client.InsertReceipt2Async(mapped, cancellationToken);
        return JsonResults.Serialize(new { receiptId = id });
    }

    [McpServerTool(Name = "insert_journal"), Description("Insert a journal. Requires KashFlow__ReadOnly=false. Pass structured journal fields (date and balancing lines), or journalJson with a full JournalEntry payload. When journalJson is set, the structured fields are ignored.")]
    public async Task<string> InsertJournal(
        [Description("Structured journal. Ignored when journalJson is set.")] InsertJournalInput? journal = null,
        [Description("Legacy full JournalEntry JSON string. When set, this is used instead of the structured journal.")] string? journalJson = null,
        CancellationToken cancellationToken = default)
    {
        ToolGuard.EnsureWritable(runtime);
        var mapped = WriteInputMapper.ResolveJournal(journal, journalJson);
        var id = await session.Client.InsertJournalAsync(mapped, cancellationToken);
        return JsonResults.Serialize(new { journalId = id });
    }

    [McpServerTool(Name = "insert_receipt_payment"), Description("Record a payment against a purchase receipt (SOAP InsertReceiptPayment). Requires KashFlow__ReadOnly=false. Check get_receipt_payments first so the receipt is not paid twice. payInvoice is the receipt number. payAccount is the bank account id from get_bank_accounts (accountId). If you know the nominal code instead (accountCode, for example 1206 Starling or 1209 Revolut), pass bankAccountCode and it is resolved to accountId. payMethod is the methodId from get_receipt_payment_methods. When both an id and a name or code are passed, the id is used.")]
    public async Task<string> InsertReceiptPayment(
        [Description("Purchase receipt number. Stored as Payment.PayInvoice.")] int receiptNumber,
        [Description("Date the receipt was paid (ISO-8601). Stored as Payment.PayDate.")] DateTime paymentDate,
        [Description("Amount paid, greater than 0, in the receipt currency. Stored as Payment.PayAmount.")] decimal amount,
        [Description("Payment method id from get_receipt_payment_methods (methodId). Stored as Payment.PayMethod. Optional when paymentMethodName is set.")] int? paymentMethodId = null,
        [Description("Payment method name from get_receipt_payment_methods (methodName), matched ignoring case. Used only when paymentMethodId is omitted.")] string? paymentMethodName = null,
        [Description("Bank account id from get_bank_accounts (accountId). Stored as Payment.PayAccount. Optional when bankAccountCode is set. When both are set, this id is used.")] int? bankAccountId = null,
        [Description("Bank account nominal code from get_bank_accounts (accountCode), for example 1206. Resolved to accountId. Used only when bankAccountId is omitted.")] int? bankAccountCode = null,
        [Description("Optional note stored as Payment.PayNote.")] string? note = null,
        CancellationToken cancellationToken = default)
    {
        ToolGuard.EnsureWritable(runtime);

        var methodId = paymentMethodId is int givenMethod && givenMethod > 0
            ? givenMethod
            : ReceiptPaymentMapper.ResolvePaymentMethodId(
                paymentMethodId,
                paymentMethodName,
                await session.Client.GetRecPayMethodsAsync(cancellationToken));

        var accountId = bankAccountId is int givenAccount && givenAccount > 0
            ? givenAccount
            : ReceiptPaymentMapper.ResolveBankAccountId(
                bankAccountId,
                bankAccountCode,
                await session.Client.GetBankAccountsAsync(cancellationToken));

        var payment = ReceiptPaymentMapper.Create(receiptNumber, paymentDate, amount, methodId, accountId, note);
        var id = await session.Client.InsertReceiptPaymentAsync(payment, cancellationToken);
        return JsonResults.Serialize(new
        {
            receiptPaymentId = id,
            receiptNumber,
            amount,
            payAccount = accountId,
            payMethod = methodId,
        });
    }

    [McpServerTool(Name = "transfer_between_bank_accounts"), Description("Move money from one KashFlow bank account to another. KashFlow records a transfer as one InsertBankTransaction on the source account (moneyout = amount, moneyin = 0) with transactionType set to the destination bank's nominal id from get_nominal_codes (id), not a get_bank_tx_types value. KashFlow then shows that same line on the destination feed. Do not also insert a moneyin on the destination — that doubles the transfer. Requires KashFlow__ReadOnly=false. Does not replace insert_bank_transaction. Resolve the destination with toBankAccountId / toBankAccountCode (looked up via get_nominal_codes), or pass toNominalId to override. Source account codes such as 1206 use the same lookup as insert_receipt_payment.")]
    public async Task<string> TransferBetweenBankAccounts(
        [Description("Amount to move, greater than 0.")] decimal amount,
        [Description("Transfer date (ISO-8601) stored on the source bank transaction.")] DateTime transferDate,
        [Description("Source bank account id from get_bank_accounts (accountId). Optional when fromBankAccountCode is set.")] int? fromBankAccountId = null,
        [Description("Source nominal code from get_bank_accounts (accountCode), for example 1206. Used only when fromBankAccountId is omitted.")] int? fromBankAccountCode = null,
        [Description("Destination bank account id from get_bank_accounts (accountId). Optional when toBankAccountCode or toNominalId is set.")] int? toBankAccountId = null,
        [Description("Destination nominal code from get_bank_accounts (accountCode), for example 1209. Used to find the destination nominal id. Optional when toBankAccountId or toNominalId is set.")] int? toBankAccountCode = null,
        [Description("Destination bank account's nominal id (get_nominal_codes id). Stored as BankTransaction.TransactionType. Optional override when toBankAccountId or toBankAccountCode is set.")] int? toNominalId = null,
        [Description("Comment stored on the source bank transaction. When omitted, a short transfer note is used.")] string? comment = null,
        CancellationToken cancellationToken = default)
    {
        ToolGuard.EnsureWritable(runtime);

        var accounts = await session.Client.GetBankAccountsAsync(cancellationToken);
        var nominals = await session.Client.GetNominalCodesAsync(cancellationToken);

        var fromId = fromBankAccountId is int sourceId && sourceId > 0
            ? sourceId
            : BankTransfer.ResolveAccountId(fromBankAccountId, fromBankAccountCode, accounts, "from");

        int? resolvedToAccountId = null;
        if (toBankAccountId is int destinationId && destinationId > 0)
            resolvedToAccountId = destinationId;
        else if (toBankAccountCode is int destinationCode && destinationCode > 0)
            resolvedToAccountId = BankTransfer.ResolveAccountId(null, destinationCode, accounts, "to");
        else if (toNominalId is int onlyNominal && onlyNominal > 0)
            resolvedToAccountId = BankTransfer.FindBankAccountIdForNominal(onlyNominal, accounts, nominals);

        BankTransfer.EnsureDifferentAccounts(fromId, resolvedToAccountId);

        var destinationNominalId = BankTransfer.ResolveDestinationNominalId(
            toNominalId,
            toBankAccountId,
            toBankAccountCode,
            accounts,
            nominals);

        var result = await BankTransfer.PostAsync(
            fromId,
            destinationNominalId,
            amount,
            transferDate,
            comment,
            (transaction, token) => session.Client.InsertBankTransactionAsync(transaction, token),
            cancellationToken);

        return JsonResults.Serialize(new
        {
            transactionId = result.TransactionId,
            fromBankAccountId = result.FromBankAccountId,
            toBankAccountId = resolvedToAccountId,
            toNominalId = result.ToNominalId,
            amount,
        });
    }
}
