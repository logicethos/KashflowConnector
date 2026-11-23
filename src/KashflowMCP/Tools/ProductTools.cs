using System.ComponentModel;
using KashFlow;
using ModelContextProtocol.Server;

namespace KashflowMCP.Tools;

[McpServerToolType]
public sealed class ProductTools(KashFlowClient client)
{
    [McpServerTool(Name = "get_products"), Description("List products.")]
    public async Task<string> GetProducts(CancellationToken cancellationToken)
        => JsonResults.Serialize(await client.GetProductsAsync(cancellationToken));

    [McpServerTool(Name = "get_products_with_sub_products"), Description("List products including sub-products.")]
    public async Task<string> GetProductsWithSubProducts(CancellationToken cancellationToken)
        => JsonResults.Serialize(await client.GetProductsWithSubProductsAsync(cancellationToken));

    [McpServerTool(Name = "get_sub_products"), Description("List sub-products for a nominal ID.")]
    public async Task<string> GetSubProducts(
        [Description("Nominal ID")] int nominalId,
        CancellationToken cancellationToken)
        => JsonResults.Serialize(await client.GetSubProductsAsync(nominalId, cancellationToken));
}
