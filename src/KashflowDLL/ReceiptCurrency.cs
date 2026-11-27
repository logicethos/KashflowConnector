namespace KashFlow;

/// <summary>
/// Converts purchase-receipt money from document currency into the SOAP line amounts KashFlow posts.
/// </summary>
/// <remarks>
/// <para>
/// KashFlow staff guidance for <c>InsertInvoice</c> / <c>InsertReceipt</c>: line net and VAT must be the
/// home-currency (GBP) amounts, and <c>ExchangeRate</c> must be set. Observed inserts that sent the
/// foreign figure as <c>Rate</c> were booked at that figure in sterling, and settling then posted a
/// forex gain. REST is different: <c>Rate</c> is quote currency and <c>HomeCurrencyRate</c> is home
/// currency (<c>https://www.kashflow.com/developers/rest-api/api-purchases/</c>). The AUD sample there
/// uses <c>ExchangeRate</c> 2 with <c>Rate</c> 15 and <c>HomeCurrencyRate</c> 7.50, so the rate is
/// foreign units per 1 GBP and home = foreign / rate.
/// </para>
/// <para>
/// Tool and SOAP receipt inputs to this library stay in document currency. ToSoap copies
/// the document and rewrites line <c>Rate</c> and <c>VatAmount</c> to GBP. Header <c>NetAmount</c> and
/// <c>VATAmount</c> stay in the document currency so readers that do
/// <c>(net + VAT) / ExchangeRate</c> still see the foreign total. <c>ValuesInCurrency</c> is left unset:
/// the flag is on the schema and is not documented, and the live bug shows the default treats line
/// amounts as GBP.
/// </para>
/// </remarks>
public static class ReceiptCurrency
{
    /// <summary>The only accepted convention: foreign currency units per 1 GBP.</summary>
    public const string ForeignPerGbp = "ForeignPerGbp";

    /// <summary>Rejected convention. Callers must pass the reciprocal themselves.</summary>
    public const string GbpPerForeign = "GbpPerForeign";

    public const string HomeCurrencyCode = "GBP";

    /// <summary>
    /// Currencies where 1 unit is normally worth less than £1, so a foreign-per-GBP rate below 1
    /// is the reciprocal (GBP per foreign unit) and is rejected. Stronger currencies (KWD, BHD, OMR, JOD)
    /// are not listed; any positive rate is accepted for codes outside this set.
    /// </summary>
    private static readonly HashSet<string> RateAtLeastOne = new(StringComparer.OrdinalIgnoreCase)
    {
        "USD", "EUR", "AUD", "CAD", "NZD", "CHF", "JPY", "CNY", "SEK", "NOK", "DKK",
        "PLN", "CZK", "HUF", "INR", "ZAR", "SGD", "HKD", "MXN", "BRL", "TRY", "THB",
        "KRW", "TWD", "PHP", "IDR", "MYR", "AED", "SAR", "QAR", "ILS", "RON", "BGN",
        "RUB", "ARS", "CLP", "COP", "PEN", "VND", "PKR", "BDT", "LKR", "NGN", "GHS",
        "KES", "UAH", "EGP",
    };

    public static bool IsHome(string? currencyCode)
        => string.IsNullOrWhiteSpace(currencyCode)
            || currencyCode.Trim().Equals(HomeCurrencyCode, StringComparison.OrdinalIgnoreCase);

    public static string NormalizeCode(string? currencyCode)
        => IsHome(currencyCode) ? HomeCurrencyCode : currencyCode!.Trim().ToUpperInvariant();

    /// <summary>
    /// Accepts null/blank and <see cref="ForeignPerGbp"/>. Rejects <see cref="GbpPerForeign"/> and anything else.
    /// </summary>
    public static void ValidateConvention(string? rateConvention)
    {
        if (string.IsNullOrWhiteSpace(rateConvention))
            return;

        var value = rateConvention.Trim();
        if (value.Equals(ForeignPerGbp, StringComparison.OrdinalIgnoreCase))
            return;

        if (value.Equals(GbpPerForeign, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "rateConvention GbpPerForeign is not accepted. Pass ForeignPerGbp: exchangeRate is foreign currency units per 1 GBP " +
                "(USD 1.3123 means £1 = $1.3123). Do not pass the reciprocal (GBP per 1 foreign unit).");
        }

        throw new ArgumentException(
            "rateConvention must be ForeignPerGbp (foreign currency units per 1 GBP).");
    }

    /// <summary>
    /// Checks <paramref name="exchangeRate"/> for <paramref name="currencyCode"/>.
    /// Returns the rate to store: 1 for GBP, otherwise the caller's foreign-per-GBP rate.
    /// </summary>
    public static decimal ValidateRate(string? currencyCode, decimal exchangeRate)
    {
        var code = NormalizeCode(currencyCode);
        if (code == HomeCurrencyCode)
        {
            if (exchangeRate is 0m or 1m)
                return 1m;

            throw new ArgumentException(
                $"GBP receipts use exchange rate 1 (foreign units per £1). {exchangeRate} is not 1.");
        }

        if (exchangeRate <= 0m)
        {
            throw new ArgumentException(
                $"Receipt currency {code} requires exchangeRate: foreign currency units per 1 GBP " +
                "(USD 1.3123, EUR about 1.15). Do not pass the reciprocal (GBP per 1 foreign unit), and do not omit the rate.");
        }

        if (exchangeRate < 1m && RateAtLeastOne.Contains(code))
        {
            throw new ArgumentException(
                $"Exchange rate {exchangeRate} for {code} is below 1, which is the GBP-per-foreign reciprocal. " +
                "Pass foreign currency units per 1 GBP (for example USD 1.3123, not 0.7620).");
        }

        return exchangeRate;
    }

    public static Receipt2 ToSoap(Receipt2 source, string? rateConvention = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        var clone = Clone(source);
        var money = Read(clone.Lines);
        var converted = ConvertDocument(
            clone.CurrencyCode,
            clone.ExchangeRate,
            clone.NetAmount,
            clone.VATAmount,
            money,
            rateConvention);
        clone.CurrencyCode = converted.CurrencyCode;
        clone.ExchangeRate = converted.ExchangeRate;
        clone.NetAmount = converted.ForeignNet;
        clone.VATAmount = converted.ForeignVat;
        Write(clone.Lines, converted.Lines);
        return clone;
    }

    public static Invoice ToSoap(Invoice source, string? rateConvention = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        var clone = Clone(source);
        var money = Read(clone.Lines);
        var converted = ConvertDocument(
            clone.CurrencyCode,
            clone.ExchangeRate,
            clone.NetAmount,
            clone.VATAmount,
            money,
            rateConvention);
        clone.CurrencyCode = converted.CurrencyCode;
        clone.ExchangeRate = converted.ExchangeRate;
        clone.NetAmount = converted.ForeignNet;
        clone.VATAmount = converted.ForeignVat;
        Write(clone.Lines, converted.Lines);
        return clone;
    }

    public static Invoice_TypeDefined ToSoap(Invoice_TypeDefined source, string? rateConvention = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        var clone = Clone(source);
        var money = Read(clone.Lines);
        var converted = ConvertDocument(
            clone.CurrencyCode,
            clone.ExchangeRate,
            clone.NetAmount,
            clone.VATAmount,
            money,
            rateConvention);
        clone.CurrencyCode = converted.CurrencyCode;
        clone.ExchangeRate = converted.ExchangeRate;
        clone.NetAmount = converted.ForeignNet;
        clone.VATAmount = converted.ForeignVat;
        Write(clone.Lines, converted.Lines);
        return clone;
    }

    public static ReceiptLine2 ToSoapLine(
        ReceiptLine2 line,
        string? currencyCode,
        decimal? exchangeRate,
        string? rateConvention = null)
    {
        ArgumentNullException.ThrowIfNull(line);
        var converted = ConvertDocument(
            currencyCode,
            exchangeRate ?? 0m,
            netAmount: 0m,
            vatAmount: 0m,
            [new MoneyInput(line.Quantity, line.Rate, line.VatRate, line.VatAmount)],
            rateConvention,
            headerOptional: true);
        var copy = Clone(line);
        copy.Rate = converted.Lines[0].GbpUnitRate;
        copy.VatAmount = converted.Lines[0].GbpVat;
        return copy;
    }

    public static InvoiceLine ToSoapLine(
        InvoiceLine line,
        string? currencyCode,
        decimal? exchangeRate,
        string? rateConvention = null)
    {
        ArgumentNullException.ThrowIfNull(line);
        var converted = ConvertDocument(
            currencyCode,
            exchangeRate ?? 0m,
            netAmount: 0m,
            vatAmount: 0m,
            [new MoneyInput(line.Quantity, line.Rate, line.VatRate, line.VatAmount)],
            rateConvention,
            headerOptional: true);
        var copy = Clone(line);
        copy.Rate = converted.Lines[0].GbpUnitRate;
        copy.VatAmount = converted.Lines[0].GbpVat;
        return copy;
    }

    public static ReceiptCurrencyPreview Describe(Receipt2 document, Receipt2 soap)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(soap);
        var lines = new List<ReceiptCurrencyLinePreview>();
        var documentLines = document.Lines ?? new KashFlowAnyList<ReceiptLine2>();
        var soapLines = soap.Lines ?? new KashFlowAnyList<ReceiptLine2>();
        var count = Math.Max(documentLines.Count, soapLines.Count);
        for (var i = 0; i < count; i++)
        {
            var foreign = i < documentLines.Count ? documentLines[i] : null;
            var home = i < soapLines.Count ? soapLines[i] : null;
            var quantity = foreign?.Quantity ?? home?.Quantity ?? 0m;
            var foreignUnit = foreign?.Rate ?? 0m;
            var foreignVat = foreign?.VatAmount ?? 0m;
            lines.Add(new ReceiptCurrencyLinePreview(
                quantity,
                foreign?.Description ?? home?.Description,
                foreignUnit,
                Round2(quantity * foreignUnit),
                foreignVat,
                home?.Rate ?? 0m,
                home is null ? 0m : Round2(quantity * home.Rate),
                home?.VatAmount ?? 0m));
        }

        var foreignNet = soap.NetAmount != 0m ? soap.NetAmount : lines.Sum(line => line.ForeignNet);
        var foreignVatTotal = soap.VATAmount != 0m ? soap.VATAmount : lines.Sum(line => line.ForeignVat);
        var gbpNet = lines.Sum(line => line.GbpNet);
        var gbpVat = lines.Sum(line => line.GbpVat);
        return new ReceiptCurrencyPreview(
            NormalizeCode(soap.CurrencyCode),
            !IsHome(soap.CurrencyCode),
            ForeignPerGbp,
            soap.ExchangeRate,
            foreignNet,
            foreignVatTotal,
            Round2(foreignNet + foreignVatTotal),
            gbpNet,
            gbpVat,
            Round2(gbpNet + gbpVat),
            lines);
    }

    private static ConvertedDocument ConvertDocument(
        string? currencyCode,
        decimal exchangeRate,
        decimal netAmount,
        decimal vatAmount,
        IReadOnlyList<MoneyInput> lines,
        string? rateConvention,
        bool headerOptional = false)
    {
        ValidateConvention(rateConvention);
        var code = NormalizeCode(currencyCode);
        var rate = ValidateRate(code, exchangeRate);

        if (code == HomeCurrencyCode)
        {
            var homeLines = lines.Select(line =>
            {
                if (line.Quantity == 0m)
                    throw new ArgumentException("Receipt line quantity must not be 0.");
                return new ConvertedLine(line.UnitRate, line.VatAmount);
            }).ToList();
            return new ConvertedDocument(code, rate, netAmount, vatAmount, homeLines);
        }

        if (lines.Count == 0)
        {
            return new ConvertedDocument(code, rate, netAmount, vatAmount, []);
        }

        var foreignNets = new decimal[lines.Count];
        var foreignVats = new decimal[lines.Count];
        for (var i = 0; i < lines.Count; i++)
        {
            if (lines[i].Quantity == 0m)
                throw new ArgumentException("Receipt line quantity must not be 0.");
            foreignNets[i] = Round2(lines[i].Quantity * lines[i].UnitRate);
            foreignVats[i] = Round2(lines[i].VatAmount);
        }

        var foreignNet = Round2(foreignNets.Sum());
        var foreignVat = Round2(foreignVats.Sum());
        if (!headerOptional)
        {
            foreignNet = ResolveHeader(netAmount, foreignNet, lines.Count, "netAmount");
            foreignVat = ResolveHeader(vatAmount, foreignVat, lines.Count, "vatAmount");
        }

        var gbpNets = foreignNets.Select(value => Round2(value / rate)).ToList();
        var gbpVats = foreignVats.Select(value => Round2(value / rate)).ToList();
        BalanceTo(gbpNets, Round2(foreignNet / rate));
        BalanceTo(gbpVats, Round2(foreignVat / rate));

        var convertedLines = new List<ConvertedLine>(lines.Count);
        for (var i = 0; i < lines.Count; i++)
            convertedLines.Add(new ConvertedLine(UnitRate(lines[i].Quantity, gbpNets[i]), gbpVats[i]));

        return new ConvertedDocument(code, rate, foreignNet, foreignVat, convertedLines);
    }

    private static decimal ResolveHeader(decimal supplied, decimal lineSum, int lineCount, string name)
    {
        if (supplied == 0m)
            return lineSum;

        var tolerance = 0.01m * lineCount;
        if (Math.Abs(supplied - lineSum) <= tolerance)
            return Round2(supplied);

        throw new ArgumentException(
            $"Receipt {name} {supplied} is document currency and must match the sum of the document-currency lines ({lineSum}).");
    }

    private static void BalanceTo(List<decimal> parts, decimal target)
    {
        if (parts.Count == 0)
            return;
        var drift = Round2(target - parts.Sum());
        if (drift == 0m)
            return;
        parts[^1] = Round2(parts[^1] + drift);
    }

    private static decimal UnitRate(decimal quantity, decimal lineNet)
    {
        if (quantity == 1m)
            return lineNet;

        var exact = lineNet / quantity;
        var rounded = Round2(exact);
        if (Round2(quantity * rounded) == lineNet)
            return rounded;
        return exact;
    }

    private static decimal Round2(decimal value)
        => decimal.Round(value, 2, MidpointRounding.AwayFromZero);

    private static List<MoneyInput> Read(IEnumerable<ReceiptLine2>? lines)
        => lines?.Select(line => new MoneyInput(line.Quantity, line.Rate, line.VatRate, line.VatAmount)).ToList()
            ?? [];

    private static List<MoneyInput> Read(IEnumerable<InvoiceLine>? lines)
        => lines?.Select(line => new MoneyInput(line.Quantity, line.Rate, line.VatRate, line.VatAmount)).ToList()
            ?? [];

    private static void Write(IList<ReceiptLine2>? lines, IReadOnlyList<ConvertedLine> converted)
    {
        if (lines is null)
            return;
        for (var i = 0; i < lines.Count; i++)
        {
            lines[i].Rate = converted[i].GbpUnitRate;
            lines[i].VatAmount = converted[i].GbpVat;
        }
    }

    private static void Write(IList<InvoiceLine>? lines, IReadOnlyList<ConvertedLine> converted)
    {
        if (lines is null)
            return;
        for (var i = 0; i < lines.Count; i++)
        {
            lines[i].Rate = converted[i].GbpUnitRate;
            lines[i].VatAmount = converted[i].GbpVat;
        }
    }

    private readonly record struct MoneyInput(decimal Quantity, decimal UnitRate, decimal VatRate, decimal VatAmount);

    private readonly record struct ConvertedLine(decimal GbpUnitRate, decimal GbpVat);

    private sealed record ConvertedDocument(
        string CurrencyCode,
        decimal ExchangeRate,
        decimal ForeignNet,
        decimal ForeignVat,
        IReadOnlyList<ConvertedLine> Lines);

    private static Receipt2 Clone(Receipt2 source)
    {
        var lines = new KashFlowAnyList<ReceiptLine2>();
        if (source.Lines is not null)
        {
            foreach (var line in source.Lines)
                lines.Add(Clone(line));
        }

        return new Receipt2
        {
            ReceiptDBId = source.ReceiptDBId,
            ReceiptNumber = source.ReceiptNumber,
            ReceiptDate = source.ReceiptDate,
            DueDate = source.DueDate,
            Supplier = source.Supplier,
            SupplierId = source.SupplierId,
            Paid = source.Paid,
            SupplierReference = source.SupplierReference,
            EstimateCategory = source.EstimateCategory,
            SuppressTotal = source.SuppressTotal,
            ProjectId = source.ProjectId,
            CurrencyCode = source.CurrencyCode,
            ExchangeRate = source.ExchangeRate,
            ReadableString = source.ReadableString,
            Lines = lines,
            NetAmount = source.NetAmount,
            VATAmount = source.VATAmount,
            AmountPaid = source.AmountPaid,
            SupplierName = source.SupplierName,
            PermaLink = source.PermaLink,
            DeliveryAddress = source.DeliveryAddress,
            UseCustomDeliveryAddress = source.UseCustomDeliveryAddress,
            CISRCNetAmount = source.CISRCNetAmount,
            CISRCVatAmount = source.CISRCVatAmount,
            IsCISReverseCharge = source.IsCISReverseCharge,
        };
    }

    private static Invoice Clone(Invoice source)
    {
        var lines = new KashFlowAnyList<InvoiceLine>();
        if (source.Lines is not null)
        {
            foreach (var line in source.Lines)
                lines.Add(Clone(line));
        }

        return new Invoice
        {
            InvoiceDBID = source.InvoiceDBID,
            InvoiceNumber = source.InvoiceNumber,
            InvoiceDate = source.InvoiceDate,
            DueDate = source.DueDate,
            Customer = source.Customer,
            CustomerID = source.CustomerID,
            Paid = source.Paid,
            CustomerReference = source.CustomerReference,
            EstimateCategory = source.EstimateCategory,
            SuppressTotal = source.SuppressTotal,
            ProjectID = source.ProjectID,
            CurrencyCode = source.CurrencyCode,
            ExchangeRate = source.ExchangeRate,
            ReadableString = source.ReadableString,
            Lines = lines,
            NetAmount = source.NetAmount,
            VATAmount = source.VATAmount,
            AmountPaid = source.AmountPaid,
            CustomerName = source.CustomerName,
            PermaLink = source.PermaLink,
            DeliveryAddress = source.DeliveryAddress,
            UseCustomDeliveryAddress = source.UseCustomDeliveryAddress,
            CISRCNetAmount = source.CISRCNetAmount,
            CISRCVatAmount = source.CISRCVatAmount,
            IsCISReverseCharge = source.IsCISReverseCharge,
        };
    }

    private static Invoice_TypeDefined Clone(Invoice_TypeDefined source)
    {
        var lines = new List<InvoiceLine>();
        if (source.Lines is not null)
        {
            foreach (var line in source.Lines)
                lines.Add(Clone(line));
        }

        return new Invoice_TypeDefined
        {
            InvoiceDBID = source.InvoiceDBID,
            InvoiceNumber = source.InvoiceNumber,
            InvoiceDate = source.InvoiceDate,
            DueDate = source.DueDate,
            Customer = source.Customer,
            CustomerID = source.CustomerID,
            Paid = source.Paid,
            CustomerReference = source.CustomerReference,
            EstimateCategory = source.EstimateCategory,
            SuppressTotal = source.SuppressTotal,
            ProjectID = source.ProjectID,
            CurrencyCode = source.CurrencyCode,
            ExchangeRate = source.ExchangeRate,
            ReadableString = source.ReadableString,
            Lines = lines,
            NetAmount = source.NetAmount,
            VATAmount = source.VATAmount,
            AmountPaid = source.AmountPaid,
            CustomerName = source.CustomerName,
            CISRCNetAmount = source.CISRCNetAmount,
            CISRCVatAmount = source.CISRCVatAmount,
            IsCISReverseCharge = source.IsCISReverseCharge,
        };
    }

    private static ReceiptLine2 Clone(ReceiptLine2 line)
        => new()
        {
            Quantity = line.Quantity,
            Description = line.Description,
            Rate = line.Rate,
            ChargeType = line.ChargeType,
            VatRate = line.VatRate,
            VatAmount = line.VatAmount,
            ProductId = line.ProductId,
            Sort = line.Sort,
            ProjId = line.ProjId,
            LineId = line.LineId,
            ImportDuty = line.ImportDuty,
            ValuesInCurrency = line.ValuesInCurrency,
        };

    private static InvoiceLine Clone(InvoiceLine line)
        => new()
        {
            Quantity = line.Quantity,
            Description = line.Description,
            Rate = line.Rate,
            ChargeType = line.ChargeType,
            VatRate = line.VatRate,
            VatAmount = line.VatAmount,
            ProductID = line.ProductID,
            Sort = line.Sort,
            ProjID = line.ProjID,
            LineID = line.LineID,
            ValuesInCurrency = line.ValuesInCurrency,
        };
}

/// <summary>Document-currency input beside the GBP amounts that will be sent as SOAP line rates.</summary>
public sealed record ReceiptCurrencyPreview(
    string CurrencyCode,
    bool IsForeignCurrency,
    string RateConvention,
    decimal ExchangeRateForeignPerGbp,
    decimal ForeignNet,
    decimal ForeignVat,
    decimal ForeignGross,
    decimal GbpNet,
    decimal GbpVat,
    decimal GbpGross,
    IReadOnlyList<ReceiptCurrencyLinePreview> Lines);

/// <summary>One receipt line in both currencies. Unit rates are per quantity; nets are quantity times unit rate.</summary>
public sealed record ReceiptCurrencyLinePreview(
    decimal Quantity,
    string? Description,
    decimal ForeignUnitRate,
    decimal ForeignNet,
    decimal ForeignVat,
    decimal GbpUnitRate,
    decimal GbpNet,
    decimal GbpVat);
