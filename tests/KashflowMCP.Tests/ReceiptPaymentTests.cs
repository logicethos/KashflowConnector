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
}
