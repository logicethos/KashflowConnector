using System.ComponentModel;
using KashFlow;
using ModelContextProtocol.Server;

namespace KashflowMCP.Tools;

[McpServerToolType]
public sealed class SupplierTools(KashFlowClient client)
{
    [McpServerTool(Name = "get_suppliers"), Description("List suppliers.")]
    public async Task<string> GetSuppliers(CancellationToken cancellationToken)
        => JsonResults.Serialize(await client.GetSuppliersAsync(cancellationToken));

    [McpServerTool(Name = "get_supplier"), Description("Get a supplier by code.")]
    public async Task<string> GetSupplier(
        [Description("Supplier code")] string supplierCode,
        CancellationToken cancellationToken)
        => JsonResults.Serialize(await client.GetSupplierAsync(supplierCode, cancellationToken));

    [McpServerTool(Name = "get_supplier_by_id"), Description("Get a supplier by numeric ID.")]
    public async Task<string> GetSupplierById(
        [Description("Supplier ID")] int supplierId,
        CancellationToken cancellationToken)
        => JsonResults.Serialize(await client.GetSupplierByIDAsync(supplierId, cancellationToken));

    [McpServerTool(Name = "get_supplier2"), Description("Get a supplier (v2 shape including named trade border).")]
    public async Task<string> GetSupplier2(
        [Description("Supplier code")] string supplierCode,
        CancellationToken cancellationToken)
        => JsonResults.Serialize(await client.GetSupplier2Async(supplierCode, cancellationToken));
}
