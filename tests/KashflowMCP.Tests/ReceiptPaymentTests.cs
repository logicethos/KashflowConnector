using System.Text.Json;
using KashFlow;
using KashflowMCP.Tools;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace KashflowMCP.Tests;

public class ReceiptPaymentMapperTests
{
    [Fact]
    public void Create_stores_the_receipt_number_as_pay_invoice()
    {
        var payment = ReceiptPaymentMapper.Create(
            42,
            new DateTime(2026, 4, 2),
            19.5m,
            paymentMethodId: 3,
            bankAccountId: 8,
            note: "  paid  ");

        Assert.Equal(42, payment.PayInvoice);
        Assert.Equal(new DateTime(2026, 4, 2), payment.PayDate);
        Assert.Equal(19.5m, payment.PayAmount);
        Assert.Equal(3, payment.PayMethod);
        Assert.Equal(8, payment.PayAccount);
        Assert.Equal("paid", payment.PayNote);
    }

    [Fact]
    public void Create_keeps_a_negative_credit_note_amount()
    {
        var payment = ReceiptPaymentMapper.Create(
            4026,
            new DateTime(2026, 4, 2),
            -11.98m,
            paymentMethodId: 3,
            bankAccountId: 8,
            note: "KF04026");

        Assert.Equal(4026, payment.PayInvoice);
        Assert.Equal(-11.98m, payment.PayAmount);
        Assert.Equal(3, payment.PayMethod);
        Assert.Equal(8, payment.PayAccount);
    }

    [Fact]
    public void Create_rejects_exactly_zero()
    {
        var error = Assert.Throws<ArgumentException>(() => ReceiptPaymentMapper.Create(
            4026,
            new DateTime(2026, 4, 2),
            0m,
            paymentMethodId: 3,
            bankAccountId: 8,
            note: null));

        Assert.Contains("non-zero", error.Message, StringComparison.Ordinal);
        Assert.Contains("PaidIn", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Bank_line_preview_names_paid_in_for_a_negative_amount()
    {
        var paidIn = ReceiptPaymentBankLine.Describe(-11.98m);
        Assert.Equal(-11.98m, paidIn.Amount);
        Assert.Equal("PaidIn", paidIn.BankLine);
        Assert.Equal("in", paidIn.MoneyDirection);
        Assert.Contains("money in", paidIn.BankLineNote, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("-11.98", paidIn.BankLineNote, StringComparison.Ordinal);

        var paidOut = ReceiptPaymentBankLine.Describe(11.98m);
        Assert.Equal("PaidOut", paidOut.BankLine);
        Assert.Equal("out", paidOut.MoneyDirection);
    }

    [Fact]
    public async Task InsertReceiptPayment_dry_run_shows_credit_note_as_paid_in_without_writing()
    {
        using var session = KashFlowSession.FromConfiguration(new ConfigurationBuilder().Build());
        var tools = new WriteTools(session, new McpRuntimeOptions(true));

        var json = await tools.InsertReceiptPayment(
            4026,
            new DateTime(2026, 4, 2),
            -11.98m,
            paymentMethodId: 3,
            bankAccountId: 8,
            note: "credit note",
            dryRun: true);

        using var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.GetProperty("dryRun").GetBoolean());
        Assert.Equal("SOAP InsertReceiptPayment", doc.RootElement.GetProperty("operation").GetString());
        Assert.Equal(-11.98m, doc.RootElement.GetProperty("amount").GetDecimal());
        Assert.Equal(-11.98m, doc.RootElement.GetProperty("payAmount").GetDecimal());
        Assert.Equal(4026, doc.RootElement.GetProperty("receiptNumber").GetInt32());
        Assert.Equal(8, doc.RootElement.GetProperty("payAccount").GetInt32());
        Assert.Equal(3, doc.RootElement.GetProperty("payMethod").GetInt32());
        Assert.Equal("PaidIn", doc.RootElement.GetProperty("bankLine").GetString());
        Assert.Equal("in", doc.RootElement.GetProperty("moneyDirection").GetString());
        var note = doc.RootElement.GetProperty("bankLineNote").GetString();
        Assert.Contains("PaidIn", note, StringComparison.Ordinal);
        Assert.Contains("money in", note, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("-11.98", note, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InsertReceiptPayment_dry_run_rejects_zero_and_labels_a_positive_amount_paid_out()
    {
        using var session = KashFlowSession.FromConfiguration(new ConfigurationBuilder().Build());
        var tools = new WriteTools(session, new McpRuntimeOptions(true));

        var zero = await Assert.ThrowsAsync<ArgumentException>(() => tools.InsertReceiptPayment(
            10,
            new DateTime(2026, 1, 1),
            0m,
            paymentMethodId: 1,
            bankAccountId: 2,
            dryRun: true));
        Assert.Contains("non-zero", zero.Message, StringComparison.Ordinal);

        var json = await tools.InsertReceiptPayment(
            10,
            new DateTime(2026, 1, 1),
            5m,
            paymentMethodId: 1,
            bankAccountId: 2,
            dryRun: true);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(5m, doc.RootElement.GetProperty("payAmount").GetDecimal());
        Assert.Equal("PaidOut", doc.RootElement.GetProperty("bankLine").GetString());
    }

    [Fact]
    public void ResolveBankAccount_prefers_the_internal_id()
    {
        Assert.Equal(8, ReceiptPaymentMapper.ResolveBankAccountId(8, 1206, accounts: null));
    }

    [Fact]
    public void ResolveBankAccount_maps_a_nominal_code_such_as_1206()
    {
        var accounts = new List<BankAccount>
        {
            new() { AccountID = 4, AccountCode = 1206, AccountName = "Starling" },
            new() { AccountID = 9, AccountCode = 1209, AccountName = "Revolut" },
        };

        Assert.Equal(4, ReceiptPaymentMapper.ResolveBankAccountId(null, 1206, accounts));
        Assert.Equal(9, ReceiptPaymentMapper.ResolveBankAccountId(0, 1209, accounts));
    }

    [Fact]
    public void ResolveBankAccount_rejects_unknown_ambiguous_and_missing_accounts()
    {
        var accounts = new List<BankAccount>
        {
            new() { AccountID = 1, AccountCode = 1200, AccountName = "A" },
            new() { AccountID = 2, AccountCode = 1200, AccountName = "B" },
        };

        Assert.Throws<ArgumentException>(() => ReceiptPaymentMapper.ResolveBankAccountId(null, 1206, accounts));
        Assert.Throws<ArgumentException>(() => ReceiptPaymentMapper.ResolveBankAccountId(null, 1200, accounts));
        Assert.Throws<ArgumentException>(() => ReceiptPaymentMapper.ResolveBankAccountId(null, null, accounts));
    }

    [Fact]
    public void ResolvePaymentMethod_matches_a_name_when_no_id_is_given()
    {
        var methods = new List<PaymentMethod>
        {
            new() { MethodID = 2, MethodName = "Bank Transfer" },
        };

        Assert.Equal(2, ReceiptPaymentMapper.ResolvePaymentMethodId(null, "bank transfer", methods));
        Assert.Equal(5, ReceiptPaymentMapper.ResolvePaymentMethodId(5, "ignored", methods));
        Assert.Throws<ArgumentException>(() => ReceiptPaymentMapper.ResolvePaymentMethodId(null, "Cash", methods));
    }

    [Fact]
    public async Task InsertReceiptPayment_is_blocked_while_read_only()
    {
        using var session = KashFlowSession.FromConfiguration(new ConfigurationBuilder().Build());
        var tools = new WriteTools(session, new McpRuntimeOptions(true));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => tools.InsertReceiptPayment(
            10,
            new DateTime(2026, 1, 1),
            5m,
            paymentMethodId: 1,
            bankAccountId: 2));

        Assert.Contains("ReadOnly", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Generated_delete_receipt_payment_is_blocked_while_read_only()
    {
        using var session = KashFlowSession.FromConfiguration(new ConfigurationBuilder().Build());
        var tools = new AdditionalWriteTools(session, new McpRuntimeOptions(true));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => tools.DeleteReceiptPayment(9));

        Assert.Contains("ReadOnly", error.Message, StringComparison.Ordinal);
    }
}
