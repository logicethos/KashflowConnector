using System.ComponentModel;
using KashFlow;
using ModelContextProtocol.Server;

namespace KashflowMCP.Tools;

[McpServerToolType]
public sealed class ProductTools(KashFlowSession session)
{
    [McpServerTool(Name = "get_products"), Description("List products.")]
    public async Task<string> GetProducts(CancellationToken cancellationToken)
        => JsonResults.Serialize(await session.Client.GetProductsAsync(cancellationToken));

    [McpServerTool(Name = "get_products_with_sub_products"), Description("List products including sub-products.")]
    public async Task<string> GetProductsWithSubProducts(CancellationToken cancellationToken)
        => JsonResults.Serialize(await session.Client.GetProductsWithSubProductsAsync(cancellationToken));

    [McpServerTool(Name = "get_sub_products"), Description("List sub-products for a nominal ID.")]
    public async Task<string> GetSubProducts(
        [Description("Nominal ID")] int nominalId,
        CancellationToken cancellationToken)
        => JsonResults.Serialize(await session.Client.GetSubProductsAsync(nominalId, cancellationToken));
}
