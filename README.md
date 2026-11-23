# KashflowConnector

Typed .NET SOAP client (**KashflowDLL**) and a stdio [MCP](https://modelcontextprotocol.io) server (**KashflowMCP**) for the IRIS KashFlow accounting API. The server lists customers, suppliers, invoices, receipts, payments, products, nominals, journals, and bank accounts, and can insert a customer, supplier, invoice, purchase receipt, or journal when read-only mode is turned off.

Authentication is the **SOAP API username and password**. It is not the REST “memorable word”. The password is never logged.

## Install from the Cursor Marketplace

1. Install the **kashflow** plugin from Customize (or from this repository while it is in review).
2. Open the plugin configuration and set:
   - **KashFlow SOAP API username** (`KASHFLOW_USERNAME`) — SOAP API UserName.
   - **KashFlow SOAP API password** (`KASHFLOW_PASSWORD`) — SOAP API Password. Cursor treats this as a secret. The server never writes it to logs.
   - **Read only** (`KASHFLOW_READ_ONLY`) — defaults to **true**.
3. Cursor starts `scripts/kashflow-mcp`. That launcher picks the matching self-contained binary from this repository’s GitHub Releases (`v1.0.0`), checks `SHA256SUMS`, caches it under `~/.cache/kashflow-mcp` (or `%LOCALAPPDATA%\kashflow-mcp` on Windows), and execs it. An end user does not need the .NET SDK. The first launch needs network access to `github.com`.

`tools/list` succeeds before those values are saved. The first tool call that talks to KashFlow reports a clear error until the SOAP username and password are set.

`mcp.json` runs `scripts/kashflow-mcp` (a POSIX shell script, via `${CURSOR_PLUGIN_ROOT}`). Windows also gets `scripts/kashflow-mcp.cmd`, which runs `scripts/kashflow-mcp.ps1`. Hosts that resolve an extensionless command through `PATHEXT` (cmd.exe, and the npm-style lookup used by some MCP clients) pick the `.cmd` automatically. The Cursor plugin docs do not define a per-OS command, so that pair is the portable layout.

## Read-only and read-write

`KashFlow__ReadOnly` defaults to **true**.

- **true** (the marketplace default): only read tools are registered (`get_*`, `is_*`, `search_*`). The SOAP client also refuses every call that would change KashFlow.
- **false**: the five write tools are registered as well: `insert_customer`, `insert_supplier`, `insert_invoice`, `insert_receipt2`, `insert_journal`.

Each write tool takes a structured object with documented fields (name, addresses, lines, nominals, and so on). You can still pass the previous full SOAP JSON string (`customerJson`, `supplierJson`, `invoiceJson`, `receiptJson`, `journalJson`). When that string is set, it is used and the structured fields are ignored, so extra text and checkbox fields on Customer still work.

## Configuration

| Plugin field | Environment variable | Meaning |
| --- | --- | --- |
| `KASHFLOW_USERNAME` | `KashFlow__UserName` | SOAP API username. Not the REST memorable word. |
| `KASHFLOW_PASSWORD` | `KashFlow__Password` | SOAP API password. Never logged. |
| `KASHFLOW_READ_ONLY` | `KashFlow__ReadOnly` | `true` (default) or `false`. |

Optional, for a local checkout only (not part of the marketplace form): `KashFlow__Endpoint` and `KashFlow__TimeoutSeconds`. The endpoint defaults to `https://securedwebapp.com/api/service.asmx`.

Config also loads from the `KashFlow` section of `appsettings.json`. Do not commit live credentials. `.env.example` lists the variable names with empty values.

## Reviewer testing

Use a KashFlow **SOAP API** test account. Leave the real username and password out of the repository and out of this file.

- SOAP API username: `<test-account-username>`
- SOAP API password: `<test-account-password>`
- Read only: `true` for the first three prompts. Set it to `false` only for the fourth, and only against a sandbox company.

`tools/list` can be checked in MCP logs before the test account is entered. It should show the read tools and, while read-only is true, none of the `insert_*` tools.

Example prompts:

1. List the customers in this KashFlow account and include each customer’s code.
2. Show the company details and the unpaid invoices.
3. What are the bank account balances?
4. With read-only turned off: create a customer named “Marketplace Test”, code `MKTTEST`, email `test@example.com`, and payment terms of 14 days. Stop there — do not create an invoice unless I confirm.

## Solution layout

```
KashflowConnector.slnx
├── src/KashflowDLL          # Assembly/namespace: KashFlow (SOAP client, 192 ops)
├── src/KashflowMCP          # net10.0 stdio MCP server
├── tests/KashflowDLL.Tests
├── tests/KashflowMCP.Tests  # tools/list, write schemas, launcher
├── .cursor-plugin/plugin.json
├── mcp.json
└── scripts/kashflow-mcp     # release downloader used by the plugin
```

## Build and test

Developers need the .NET 10 SDK. Marketplace users do not.

```bash
dotnet build KashflowConnector.slnx -c Release
dotnet test KashflowConnector.slnx -c Release --no-build
```

## Publish release binaries

Tag `v1.0.0` (the version in `.cursor-plugin/plugin.json`) and push it. `.github/workflows/release.yml` builds, tests, and uploads:

- `KashflowMCP-<rid>.zip` for `linux-x64`, `linux-arm64`, `win-x64`, `win-arm64`, `osx-x64`, `osx-arm64`
- `SHA256SUMS`

To produce the same archives locally:

```bash
./scripts/publish.sh
```

Outputs land in `artifacts/publish/<rid>/` and `artifacts/release/`. Those directories are gitignored.

For a local server without a GitHub release, point the launcher at a published binary:

```bash
export KASHFLOW_MCP_BIN=/absolute/path/to/KashflowMCP
```

## Licence

MIT. Copyright 2026 Logic Ethos Ltd. See [LICENSE](LICENSE).
