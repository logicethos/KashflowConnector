using System.ComponentModel;

namespace KashflowMCP.Tools;

public sealed class InsertCustomerInput
{
    [Description("Customer display name. Required when customerJson is omitted.")]
    public string? Name { get; set; }

    [Description("Unique customer code in the KashFlow account.")]
    public string? Code { get; set; }

    [Description("Contact name shown on the customer.")]
    public string? Contact { get; set; }

    [Description("Contact title, for example Ms.")]
    public string? ContactTitle { get; set; }

    [Description("Contact first name.")]
    public string? ContactFirstName { get; set; }

    [Description("Contact last name.")]
    public string? ContactLastName { get; set; }

    [Description("Customer email address.")]
    public string? Email { get; set; }

    [Description("Telephone number.")]
    public string? Telephone { get; set; }

    [Description("Mobile number.")]
    public string? Mobile { get; set; }

    [Description("Fax number.")]
    public string? Fax { get; set; }

    [Description("Website URL.")]
    public string? Website { get; set; }

    [Description("Address line 1.")]
    public string? Address1 { get; set; }

    [Description("Address line 2.")]
    public string? Address2 { get; set; }

    [Description("Address line 3 (town or city).")]
    public string? Address3 { get; set; }

    [Description("Address line 4 (county).")]
    public string? Address4 { get; set; }

    [Description("Postal code.")]
    public string? Postcode { get; set; }

    [Description("Country name.")]
    public string? CountryName { get; set; }

    [Description("ISO country code, for example GB.")]
    public string? CountryCode { get; set; }

    [Description("Free-text notes.")]
    public string? Notes { get; set; }

    [Description("Payment terms in days.")]
    public int? PaymentTerms { get; set; }

    [Description("Discount percent.")]
    public decimal? Discount { get; set; }

    [Description("Whether to show the discount on documents.")]
    public bool? ShowDiscount { get; set; }

    [Description("KashFlow currency ID. Use 0 for the account default.")]
    public int? CurrencyId { get; set; }

    [Description("Customer VAT number.")]
    public string? VatNumber { get; set; }

    [Description("1 when the customer is in the EU, otherwise 0.")]
    public int? Ec { get; set; }

    [Description("1 when the customer is outside the EU, otherwise 0.")]
    public int? OutsideEc { get; set; }

    [Description("1 when a separate delivery address is set.")]
    public long? CustHasDeliveryAddress { get; set; }

    [Description("Delivery address line 1.")]
    public string? DeliveryAddress1 { get; set; }

    [Description("Delivery address line 2.")]
    public string? DeliveryAddress2 { get; set; }

    [Description("Delivery address line 3.")]
    public string? DeliveryAddress3 { get; set; }

    [Description("Delivery address line 4.")]
    public string? DeliveryAddress4 { get; set; }

    [Description("Delivery postal code.")]
    public string? DeliveryPostcode { get; set; }

    [Description("Delivery country name.")]
    public string? DeliveryCountryName { get; set; }

    [Description("Delivery country code.")]
    public string? DeliveryCountryCode { get; set; }
}

public sealed class InsertSupplierInput
{
    [Description("Supplier display name. Required when supplierJson is omitted.")]
    public string? Name { get; set; }

    [Description("Unique supplier code.")]
    public string? Code { get; set; }

    [Description("Contact name.")]
    public string? Contact { get; set; }

    [Description("Contact title.")]
    public string? ContactTitle { get; set; }

    [Description("Contact first name.")]
    public string? ContactFirstName { get; set; }

    [Description("Contact last name.")]
    public string? ContactLastName { get; set; }

    [Description("Supplier email.")]
    public string? Email { get; set; }

    [Description("Telephone number.")]
    public string? Telephone { get; set; }

    [Description("Mobile number.")]
    public string? Mobile { get; set; }

    [Description("Fax number.")]
    public string? Fax { get; set; }

    [Description("Website URL.")]
    public string? Website { get; set; }

    [Description("Address line 1.")]
    public string? Address1 { get; set; }

    [Description("Address line 2.")]
    public string? Address2 { get; set; }

    [Description("Address line 3.")]
    public string? Address3 { get; set; }

    [Description("Address line 4.")]
    public string? Address4 { get; set; }

    [Description("Postal code.")]
    public string? PostCode { get; set; }

    [Description("Free-text notes.")]
    public string? Notes { get; set; }

    [Description("Supplier VAT number.")]
    public string? VatNumber { get; set; }

    [Description("Payment terms in days.")]
    public int? PaymentTerms { get; set; }

    [Description("KashFlow currency ID.")]
    public int? CurrencyId { get; set; }

    [Description("1 when the supplier is in the EU, otherwise 0.")]
    public int? Ec { get; set; }

    [Description("Legacy numeric trade border on the Supplier type (not the named Supplier2 border).")]
    public decimal? TradeBorderType { get; set; }
}

public sealed class InsertDocumentLineInput
{
    [Description("Line quantity.")]
    public decimal Quantity { get; set; }

    [Description("Line description.")]
    public string? Description { get; set; }

    [Description("Unit net rate in the document currency.")]
    public decimal Rate { get; set; }

    [Description("Nominal code (charge type) for this line.")]
    public int ChargeType { get; set; }

    [Description("VAT rate percent, for example 20. Omit vatAmount to compute quantity * rate * vatRate / 100.")]
    public decimal VatRate { get; set; }

    [Description("VAT amount. When omitted, quantity * rate * vatRate / 100 is used.")]
    public decimal? VatAmount { get; set; }

    [Description("Optional product ID.")]
    public int? ProductId { get; set; }

    [Description("Sort order. Lower numbers print first.")]
    public int? Sort { get; set; }

    [Description("Optional project ID.")]
    public int? ProjId { get; set; }

    [Description("Import duty. Used by purchase receipt lines.")]
    public decimal? ImportDuty { get; set; }
}

public sealed class InsertInvoiceInput
{
    [Description("Customer ID. Required when customer (code) and invoiceJson are omitted.")]
    public int? CustomerId { get; set; }

    [Description("Customer code. Used when customerId is omitted.")]
    public string? Customer { get; set; }

    [Description("Invoice date (ISO-8601).")]
    public DateTime? InvoiceDate { get; set; }

    [Description("Due date (ISO-8601).")]
    public DateTime? DueDate { get; set; }

    [Description("Currency code, for example GBP.")]
    public string? CurrencyCode { get; set; }

    [Description("Customer reference / PO number.")]
    public string? CustomerReference { get; set; }

    [Description("Exchange rate. Leave unset for the account default.")]
    public decimal? ExchangeRate { get; set; }

    [Description("Net total. When omitted, the sum of quantity * rate across lines is used.")]
    public decimal? NetAmount { get; set; }

    [Description("VAT total. When omitted, the sum of line VAT amounts is used.")]
    public decimal? VatAmount { get; set; }

    [Description("Invoice lines. At least one is required when invoiceJson is omitted.")]
    public List<InsertDocumentLineInput>? Lines { get; set; }
}

public sealed class InsertReceiptInput
{
    [Description("Supplier ID. Required when supplier (code) and receiptJson are omitted.")]
    public int? SupplierId { get; set; }

    [Description("Supplier code. Used when supplierId is omitted.")]
    public string? Supplier { get; set; }

    [Description("Receipt date (ISO-8601).")]
    public DateTime? ReceiptDate { get; set; }

    [Description("Due date (ISO-8601).")]
    public DateTime? DueDate { get; set; }

    [Description("Currency code, for example GBP.")]
    public string? CurrencyCode { get; set; }

    [Description("Supplier reference.")]
    public string? SupplierReference { get; set; }

    [Description("Exchange rate. Leave unset for the account default.")]
    public decimal? ExchangeRate { get; set; }

    [Description("Net total. When omitted, the sum of quantity * rate across lines is used.")]
    public decimal? NetAmount { get; set; }

    [Description("VAT total. When omitted, the sum of line VAT amounts is used.")]
    public decimal? VatAmount { get; set; }

    [Description("Receipt lines. At least one is required when receiptJson is omitted.")]
    public List<InsertDocumentLineInput>? Lines { get; set; }
}

public sealed class ReceiptPaymentAllocationInput
{
    [Description("Purchase receipt number.")]
    public int ReceiptNumber { get; set; }

    [Description("Amount to allocate to this receipt, greater than 0.")]
    public decimal Amount { get; set; }
}

public sealed class InsertJournalLineInput
{
    [Description("Nominal code ID to post. Required.")]
    public int NominalId { get; set; }

    [Description("Signed amount. Debit and credit lines should balance to zero.")]
    public decimal Amount { get; set; }

    [Description("Line comment.")]
    public string? Comment { get; set; }

    [Description("Optional project ID.")]
    public int? ProjId { get; set; }
}

public sealed class InsertJournalInput
{
    [Description("Journal date (ISO-8601). Required when journalJson is omitted.")]
    public DateTime? JournalDate { get; set; }

    [Description("Journal comment.")]
    public string? Comment { get; set; }

    [Description("Optional journal number. Leave unset to let KashFlow allocate one.")]
    public int? JournalNumber { get; set; }

    [Description("Journal lines. Amounts should balance. At least one line is required when journalJson is omitted.")]
    public List<InsertJournalLineInput>? Lines { get; set; }
}
