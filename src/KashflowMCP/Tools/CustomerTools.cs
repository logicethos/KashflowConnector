using System.ComponentModel;
using KashFlow;
using ModelContextProtocol.Server;

namespace KashflowMCP.Tools;

[McpServerToolType]
public sealed class CustomerTools(KashFlowClient client)
{
    [McpServerTool(Name = "get_customers"), Description("List customers.")]
    public async Task<string> GetCustomers(CancellationToken cancellationToken)
        => JsonResults.Serialize(await client.GetCustomersAsync(cancellationToken));

    [McpServerTool(Name = "get_customer"), Description("Get a customer by code.")]
    public async Task<string> GetCustomer(
        [Description("Customer code")] string customerCode,
        CancellationToken cancellationToken)
        => JsonResults.Serialize(await client.GetCustomerAsync(customerCode, cancellationToken));

    [McpServerTool(Name = "get_customer_by_id"), Description("Get a customer by numeric ID.")]
    public async Task<string> GetCustomerById(
        [Description("Customer ID")] int customerId,
        CancellationToken cancellationToken)
        => JsonResults.Serialize(await client.GetCustomerByIDAsync(customerId, cancellationToken));

    [McpServerTool(Name = "get_customer_by_email"), Description("Get a customer by email address.")]
    public async Task<string> GetCustomerByEmail(
        [Description("Customer email")] string customerEmail,
        CancellationToken cancellationToken)
        => JsonResults.Serialize(await client.GetCustomerByEmailAsync(customerEmail, cancellationToken));

    [McpServerTool(Name = "get_customers_by_postcode"), Description("Find customers by postcode.")]
    public async Task<string> GetCustomersByPostcode(
        [Description("Postcode")] string postcode,
        CancellationToken cancellationToken)
        => JsonResults.Serialize(await client.GetCustomersByPostcodeAsync(postcode, cancellationToken));

    [McpServerTool(Name = "get_customers_modified_since"), Description("List customers modified since a date (ISO-8601).")]
    public async Task<string> GetCustomersModifiedSince(
        [Description("Modified-since timestamp (ISO-8601)")] DateTime modifiedSince,
        CancellationToken cancellationToken)
        => JsonResults.Serialize(await client.GetCustomersModifiedSinceAsync(modifiedSince, cancellationToken));

    [McpServerTool(Name = "get_customer_balance"), Description("Get customer balance by code.")]
    public async Task<string> GetCustomerBalance(
        [Description("Customer code")] string customerCode,
        CancellationToken cancellationToken)
        => JsonResults.Serialize(await client.GetCustomerBalanceAsync(customerCode, cancellationToken));

    [McpServerTool(Name = "search_customer"), Description("Search for a customer by free text (code, name, etc.).")]
    public async Task<string> SearchCustomer(
        [Description("Search text")] string searchVal,
        CancellationToken cancellationToken)
        => JsonResults.Serialize(await client.SearchCustomerByCustTextAsync(searchVal, cancellationToken));
}
