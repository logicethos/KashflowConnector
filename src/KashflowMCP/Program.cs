// Cursor mcp.json (stdio) example — point "command" at a published self-contained binary:
//
// {
//   "mcpServers": {
//     "kashflow": {
//       "command": "/path/to/artifacts/publish/linux-x64/KashflowMCP",
//       "env": {
//         "KashFlow__UserName": "your-soap-api-username",
//         "KashFlow__Password": "your-soap-api-password",
//         "KashFlow__ReadOnly": "true"
//       }
//     }
//   }
// }
//
// SOAP UserName/Password are required (not the REST memorable word). ReadOnly defaults to true.

using KashFlow;
using KashflowMCP;
using KashflowMCP.Tools;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = Host.CreateApplicationBuilder(args);

builder.Logging.AddConsole(options =>
{
    // stdout is reserved for MCP JSON-RPC
    options.LogToStandardErrorThreshold = LogLevel.Trace;
});

builder.Configuration.AddEnvironmentVariables();

var kashFlowSection = builder.Configuration.GetSection("KashFlow");
var userName = kashFlowSection["UserName"];
var password = kashFlowSection["Password"];
var readOnly = kashFlowSection.GetValue("ReadOnly", defaultValue: true);
var timeoutSeconds = kashFlowSection.GetValue("TimeoutSeconds", defaultValue: 100);
var endpointText = kashFlowSection["Endpoint"];

if (string.IsNullOrWhiteSpace(userName) || string.IsNullOrWhiteSpace(password))
{
    Console.Error.WriteLine(
        "KashflowMCP requires KashFlow__UserName and KashFlow__Password (SOAP API credentials). " +
        "Optional: KashFlow__ReadOnly (default true).");
    return 1;
}

var clientOptions = new KashFlowClientOptions(userName, password)
{
    ReadOnly = readOnly,
    Timeout = TimeSpan.FromSeconds(timeoutSeconds > 0 ? timeoutSeconds : 100),
};

if (!string.IsNullOrWhiteSpace(endpointText) && Uri.TryCreate(endpointText, UriKind.Absolute, out var endpointUri))
{
    clientOptions = new KashFlowClientOptions(userName, password)
    {
        ReadOnly = readOnly,
        Timeout = TimeSpan.FromSeconds(timeoutSeconds > 0 ? timeoutSeconds : 100),
        Endpoint = endpointUri,
    };
}

builder.Services.AddSingleton(clientOptions);
builder.Services.AddSingleton(_ => new KashFlowClient(clientOptions));
builder.Services.AddSingleton(new McpRuntimeOptions(readOnly));

var mcp = builder.Services
    .AddMcpServer(o =>
    {
        o.ServerInfo = new() { Name = "KashflowMCP", Version = "1.0.0" };
    })
    .WithStdioServerTransport()
    .WithTools<CompanyTools>()
    .WithTools<CustomerTools>()
    .WithTools<SupplierTools>()
    .WithTools<InvoiceTools>()
    .WithTools<ReceiptTools>()
    .WithTools<PaymentTools>()
    .WithTools<ProductTools>()
    .WithTools<NominalTools>()
    .WithTools<JournalTools>()
    .WithTools<BankTools>();

if (!readOnly)
    mcp.WithTools<WriteTools>();

Console.Error.WriteLine($"KashflowMCP starting (ReadOnly={readOnly}). Password is never logged.");

await builder.Build().RunAsync();
return 0;

