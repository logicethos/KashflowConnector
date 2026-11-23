using System.ComponentModel;
using KashFlow;
using ModelContextProtocol.Server;

namespace KashflowMCP.Tools;

[McpServerToolType]
public sealed class CompanyTools(KashFlowClient client)
{
    [McpServerTool(Name = "get_company_details"), Description("Get KashFlow company details for the authenticated account.")]
    public async Task<string> GetCompanyDetails(CancellationToken cancellationToken)
        => JsonResults.Serialize(await client.GetCompanyDetailsAsync(cancellationToken));

    [McpServerTool(Name = "get_remote_login_url"), Description("Get a remote login URL for the KashFlow account (read).")]
    public async Task<string> GetRemoteLoginUrl(CancellationToken cancellationToken)
        => JsonResults.Serialize(new { url = await client.GetRemoteLoginURLAsync(cancellationToken) });
}
