using System.Text.Json;
using System.Text.Json.Serialization;
using KashFlow;

namespace KashflowMCP.Tools;

public static class WriteInputMapper
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    public static Customer ResolveCustomer(InsertCustomerInput? customer, string? customerJson)
    {
        if (!string.IsNullOrWhiteSpace(customerJson))
            return CustomerFromJson(customerJson);
        if (customer is not null)
            return CustomerFromInput(customer);
        throw new ArgumentException("Pass customer fields or customerJson.");
    }

    public static Supplier ResolveSupplier(InsertSupplierInput? supplier, string? supplierJson)
    {
        if (!string.IsNullOrWhiteSpace(supplierJson))
            return SupplierFromJson(supplierJson);
        if (supplier is not null)
            return SupplierFromInput(supplier);
        throw new ArgumentException("Pass supplier fields or supplierJson.");
    }

    public static Invoice ResolveInvoice(InsertInvoiceInput? invoice, string? invoiceJson)
    {
        if (!string.IsNullOrWhiteSpace(invoiceJson))
            return InvoiceFromJson(invoiceJson);
        if (invoice is not null)
            return InvoiceFromInput(invoice);
        throw new ArgumentException("Pass invoice fields or invoiceJson.");
    }

    public static Receipt2 ResolveReceipt(InsertReceiptInput? receipt, string? receiptJson)
    {
        if (!string.IsNullOrWhiteSpace(receiptJson))
            return ReceiptFromJson(receiptJson);
        if (receipt is not null)
            return ReceiptFromInput(receipt);
        throw new ArgumentException("Pass receipt fields or receiptJson.");
    }

    public static JournalEntry ResolveJournal(InsertJournalInput? journal, string? journalJson)
    {
        if (!string.IsNullOrWhiteSpace(journalJson))
            return JournalFromJson(journalJson);
        if (journal is not null)
            return JournalFromInput(journal);
        throw new ArgumentException("Pass journal fields or journalJson.");
    }

    public static Customer CustomerFromJson(string customerJson)
        => Deserialize<Customer>(customerJson, "customerJson");

    public static Supplier SupplierFromJson(string supplierJson)
        => Deserialize<Supplier>(supplierJson, "supplierJson");

    public static Invoice InvoiceFromJson(string invoiceJson)
        => Deserialize<Invoice>(invoiceJson, "invoiceJson");

    public static Receipt2 ReceiptFromJson(string receiptJson)
        => Deserialize<Receipt2>(receiptJson, "receiptJson");

    public static JournalEntry JournalFromJson(string journalJson)
        => Deserialize<JournalEntry>(journalJson, "journalJson");

    public static Customer CustomerFromInput(InsertCustomerInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (string.IsNullOrWhiteSpace(input.Name))
            throw new ArgumentException("customer.name is required when customerJson is omitted.");

        var customer = new Customer
        {
            Name = input.Name.Trim(),
            Code = input.Code,
            Contact = input.Contact,
            ContactTitle = input.ContactTitle,
            ContactFirstName = input.ContactFirstName,
            ContactLastName = input.ContactLastName,
            Email = input.Email,
            Telephone = input.Telephone,
            Mobile = input.Mobile,
            Fax = input.Fax,
            Website = input.Website,
            Address1 = input.Address1,
            Address2 = input.Address2,
            Address3 = input.Address3,
            Address4 = input.Address4,
            Postcode = input.Postcode,
            CountryName = input.CountryName,
            CountryCode = input.CountryCode,
            Notes = input.Notes,
            VATNumber = input.VatNumber,
            DeliveryAddress1 = input.DeliveryAddress1,
            DeliveryAddress2 = input.DeliveryAddress2,
            DeliveryAddress3 = input.DeliveryAddress3,
            DeliveryAddress4 = input.DeliveryAddress4,
            DeliveryPostcode = input.DeliveryPostcode,
            DeliveryCountryName = input.DeliveryCountryName,
            DeliveryCountryCode = input.DeliveryCountryCode,
        };
        if (input.PaymentTerms is int terms)
            customer.PaymentTerms = terms;
        if (input.Discount is decimal discount)
            customer.Discount = discount;
        if (input.ShowDiscount is bool showDiscount)
            customer.ShowDiscount = showDiscount;
        if (input.CurrencyId is int currencyId)
            customer.CurrencyID = currencyId;
        if (input.Ec is int ec)
            customer.EC = ec;
        if (input.OutsideEc is int outsideEc)
            customer.OutsideEC = outsideEc;
        if (input.CustHasDeliveryAddress is long hasDelivery)
            customer.CustHasDeliveryAddress = hasDelivery;
        return customer;
    }

    public static Supplier SupplierFromInput(InsertSupplierInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (string.IsNullOrWhiteSpace(input.Name))
            throw new ArgumentException("supplier.name is required when supplierJson is omitted.");

        var supplier = new Supplier
        {
            Name = input.Name.Trim(),
            Code = input.Code,
            Contact = input.Contact,
            ContactTitle = input.ContactTitle,
            ContactFirstName = input.ContactFirstName,
            ContactLastName = input.ContactLastName,
            Email = input.Email,
            Telephone = input.Telephone,
            Mobile = input.Mobile,
            Fax = input.Fax,
            Website = input.Website,
            Address1 = input.Address1,
            Address2 = input.Address2,
            Address3 = input.Address3,
            Address4 = input.Address4,
            PostCode = input.PostCode,
            Notes = input.Notes,
            VATNumber = input.VatNumber,
        };
        if (input.PaymentTerms is int terms)
            supplier.PaymentTerms = terms;
        if (input.CurrencyId is int currencyId)
            supplier.CurrencyID = currencyId;
        if (input.Ec is int ec)
            supplier.EC = ec;
        if (input.TradeBorderType is decimal border)
            supplier.TradeBorderType = border;
        return supplier;
    }

    public static Invoice InvoiceFromInput(InsertInvoiceInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if ((input.CustomerId is null or 0) && string.IsNullOrWhiteSpace(input.Customer))
            throw new ArgumentException("invoice.customerId or invoice.customer (code) is required when invoiceJson is omitted.");

        var lines = MapInvoiceLines(input.Lines, "invoice.lines");
        var invoice = new Invoice
        {
            Customer = input.Customer,
            InvoiceDate = input.InvoiceDate,
            DueDate = input.DueDate,
            CurrencyCode = input.CurrencyCode,
            CustomerReference = input.CustomerReference,
            Lines = lines,
            NetAmount = input.NetAmount ?? lines.Sum(line => line.Quantity * line.Rate),
            VATAmount = input.VatAmount ?? lines.Sum(line => line.VatAmount),
        };
        if (input.CustomerId is int customerId)
            invoice.CustomerID = customerId;
        if (input.ExchangeRate is decimal exchangeRate)
            invoice.ExchangeRate = exchangeRate;
        return invoice;
    }

    public static Receipt2 ReceiptFromInput(InsertReceiptInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if ((input.SupplierId is null or 0) && string.IsNullOrWhiteSpace(input.Supplier))
            throw new ArgumentException("receipt.supplierId or receipt.supplier (code) is required when receiptJson is omitted.");

        var lines = MapReceiptLines(input.Lines, "receipt.lines");
        var receipt = new Receipt2
        {
            Supplier = input.Supplier,
            ReceiptDate = input.ReceiptDate,
            DueDate = input.DueDate,
            CurrencyCode = input.CurrencyCode,
            SupplierReference = input.SupplierReference,
            Lines = lines,
            NetAmount = input.NetAmount ?? lines.Sum(line => line.Quantity * line.Rate),
            VATAmount = input.VatAmount ?? lines.Sum(line => line.VatAmount),
        };
        if (input.SupplierId is int supplierId)
            receipt.SupplierId = supplierId;
        if (input.ExchangeRate is decimal exchangeRate)
            receipt.ExchangeRate = exchangeRate;
        return receipt;
    }

    public static JournalEntry JournalFromInput(InsertJournalInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input.JournalDate is null)
            throw new ArgumentException("journal.journalDate is required when journalJson is omitted.");
        if (input.Lines is null || input.Lines.Count == 0)
            throw new ArgumentException("journal.lines needs at least one line when journalJson is omitted.");

        var lines = new KashFlowAnyList<JournalLine>(input.Lines.Count);
        foreach (var line in input.Lines)
        {
            if (line.NominalId <= 0)
                throw new ArgumentException("journal.lines[].nominalId is required.");
            lines.Add(new JournalLine
            {
                NominalID = line.NominalId,
                Amount = line.Amount,
                Comment = line.Comment,
                ProjId = line.ProjId ?? 0,
            });
        }

        var journal = new JournalEntry
        {
            JournalDate = input.JournalDate,
            Comment = input.Comment,
            Lines = lines,
        };
        if (input.JournalNumber is int number)
            journal.JournalNumber = number;
        return journal;
    }

    public static decimal LineVat(InsertDocumentLineInput line)
        => line.VatAmount ?? (line.Quantity * line.Rate * line.VatRate / 100m);

    private static KashFlowAnyList<InvoiceLine> MapInvoiceLines(List<InsertDocumentLineInput>? lines, string name)
    {
        if (lines is null || lines.Count == 0)
            throw new ArgumentException($"{name} needs at least one line.");

        var mapped = new KashFlowAnyList<InvoiceLine>(lines.Count);
        foreach (var line in lines)
        {
            mapped.Add(new InvoiceLine
            {
                Quantity = line.Quantity,
                Description = line.Description,
                Rate = line.Rate,
                ChargeType = line.ChargeType,
                VatRate = line.VatRate,
                VatAmount = LineVat(line),
                ProductID = line.ProductId ?? 0,
                Sort = line.Sort ?? 0,
                ProjID = line.ProjId ?? 0,
            });
        }

        return mapped;
    }

    private static KashFlowAnyList<ReceiptLine2> MapReceiptLines(List<InsertDocumentLineInput>? lines, string name)
    {
        if (lines is null || lines.Count == 0)
            throw new ArgumentException($"{name} needs at least one line.");

        var mapped = new KashFlowAnyList<ReceiptLine2>(lines.Count);
        foreach (var line in lines)
        {
            mapped.Add(new ReceiptLine2
            {
                Quantity = line.Quantity,
                Description = line.Description,
                Rate = line.Rate,
                ChargeType = line.ChargeType,
                VatRate = line.VatRate,
                VatAmount = LineVat(line),
                ProductId = line.ProductId ?? 0,
                Sort = line.Sort ?? 0,
                ProjId = line.ProjId ?? 0,
                ImportDuty = line.ImportDuty ?? 0,
            });
        }

        return mapped;
    }

    private static T Deserialize<T>(string json, string argumentName) where T : class
    {
        try
        {
            return JsonSerializer.Deserialize<T>(json, JsonOptions)
                ?? throw new ArgumentException($"{argumentName} was empty.");
        }
        catch (JsonException ex)
        {
            throw new ArgumentException($"{argumentName} is not valid {typeof(T).Name} JSON.", ex);
        }
    }
}
