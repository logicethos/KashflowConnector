# KashflowConnector

Typed .NET SOAP client library (**KashflowDLL**) and stdio [MCP](https://modelcontextprotocol.io) server (**KashflowMCP**) for the IRIS KashFlow API.

> KashFlow authentication for this connector is the **SOAP API UserName/Password**, not the REST “memorable word”.

## Solution layout

```
KashflowConnector.slnx
├── src/KashflowDLL   # Assembly/namespace: KashFlow (drop-in SOAP client, 192 ops)
├── src/KashflowMCP   # net10.0 stdio MCP server
└── tests/KashflowDLL.Tests
```

## Build & test

```bash
dotnet build KashflowConnector.slnx -c Release
dotnet test KashflowConnector.slnx -c Release --no-build
```

## Read-only mode (default safe)

`KashFlow__ReadOnly` defaults to **true**.

- When `true`: only read MCP tools are registered (Get / Is / Search). The underlying `KashFlowClientOptions.ReadOnly` also blocks write SOAP calls.
- When `false`: optional write tools are registered (`insert_customer`, `insert_supplier`, `insert_invoice`, `insert_receipt2`, `insert_journal`).

```bash
export KashFlow__UserName='your-soap-api-username'
export KashFlow__Password='your-soap-api-password'
export KashFlow__ReadOnly=true   # default
```

Config also works via `appsettings.json` / `KashFlow` section (never commit live credentials).

## Cursor MCP (stdio)

Publish a self-contained binary, then register it:

```json
{
  "mcpServers": {
    "kashflow": {
      "command": "/absolute/path/to/artifacts/publish/linux-x64/KashflowMCP",
      "env": {
        "KashFlow__UserName": "your-soap-api-username",
        "KashFlow__Password": "your-soap-api-password",
        "KashFlow__ReadOnly": "true"
      }
    }
  }
}
```

## Publish self-contained binaries

```bash
./scripts/publish.sh
```

Outputs land in `artifacts/publish/<rid>/` for:

- `linux-x64`, `linux-arm64`
- `win-x64`, `win-arm64`
- `osx-x64`, `osx-arm64`

Release zips are produced under `artifacts/release/`.

## MCP read tools

Company, customers, suppliers, invoices, receipts, payments, products, nominals, journals, bank accounts (see tool names such as `get_customers`, `get_invoice`, `get_bank_accounts`, …).
