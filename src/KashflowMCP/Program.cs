// Marketplace install runs scripts/kashflow-mcp (see mcp.json). That launcher downloads
// the self-contained binary; this process does not require the .NET SDK.
//
// Credentials are optional at startup so hosts can call tools/list before setup.
// KashFlow__UserName and KashFlow__Password are the SOAP API username and password.
// Optional KashFlow__RestUserName / KashFlow__RestPassword (or KASHFLOW_REST_*) override those for REST login.
// KashFlow__MemorableWord (or KASHFLOW_MEMORABLE_WORD) enables REST tools (KfToken session).
// KashFlow__ReadOnly defaults to true. Passwords and memorable word are never logged.

using KashflowMCP;
using KashflowMCP.Tools;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = Host.CreateApplicationBuilder(args);

builder.Logging.AddConsole(options =>
{
    // stdout is reserved for MCP JSON-RPC.
    options.LogToStandardErrorThreshold = LogLevel.Trace;
});

builder.Configuration.AddEnvironmentVariables();

var session = KashFlowSession.FromConfiguration(builder.Configuration);
builder.Services.AddSingleton(session);
builder.Services.AddSingleton(new McpRuntimeOptions(session.ReadOnly));

var mcp = builder.Services
    .AddMcpServer(o =>
    {
        o.ServerInfo = new() { Name = "KashflowMCP", Version = "1.1.0" };
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
    .WithTools<BankTools>()
    .WithTools<AdditionalReadTools>();

if (!session.ReadOnly)
{
    mcp.WithTools<WriteTools>();
    mcp.WithTools<AdditionalWriteTools>();
}

// REST tools need memorable-word session auth. Writes still honour ReadOnly unless dryRun.
if (session.RestConfigured)
    mcp.WithTools<RestTools>();

Console.Error.WriteLine(
    $"KashflowMCP starting (ReadOnly={session.ReadOnly}, credentialsConfigured={session.CredentialsConfigured}, restConfigured={session.RestConfigured}). Password and memorable word are never logged.");

await builder.Build().RunAsync();
return 0;
