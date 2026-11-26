# KashflowConnector

Typed .NET SOAP client (**KashflowDLL**) and a stdio [MCP](https://modelcontextprotocol.io) server (**KashflowMCP**) for the IRIS KashFlow accounting API. The server lists customers, suppliers, invoices, receipts, payments, products, nominals, journals, and bank accounts, and can insert a customer, supplier, invoice, purchase receipt, or journal when read-only mode is turned off.

Authentication for SOAP tools is the **SOAP API username and password**. Optional REST tools (bulk payments, bank-feed linking, purchase files, reconciliations) also need the account **memorable word** for KfToken session auth. REST `sessiontoken` uses the **web login password** (`KASHFLOW_REST_PASSWORD` / `KashFlow__RestPassword`) with the SOAP username (optional `KASHFLOW_REST_USERNAME` only if it differs). When RestPassword is unset, SOAP password is reused; the web password does **not** work for SOAP. Passwords and the memorable word are never logged.

## Install from the Cursor Marketplace

1. Install the **kashflow** plugin from Customize (or from this repository while it is in review).
2. Open the plugin configuration and set:
   - **KashFlow SOAP API username** (`KASHFLOW_USERNAME`) — SOAP API UserName.
   - **KashFlow SOAP API password** (`KASHFLOW_PASSWORD`) — SOAP API Password. Cursor treats this as a secret. The server never writes it to logs.
   - **KashFlow REST username** (`KASHFLOW_REST_USERNAME`) — optional. Defaults to `KASHFLOW_USERNAME`; usually leave empty.
   - **KashFlow REST web password** (`KASHFLOW_REST_PASSWORD`) — web login password for REST `sessiontoken` (often differs from the SOAP API password). Treated as a secret; never logged. Falls back to `KASHFLOW_PASSWORD` when empty.
   - **KashFlow REST memorable word** (`KASHFLOW_MEMORABLE_WORD`) — optional. Enables REST tools (`pay_purchases_bulk`, bank-feed link, purchase files, reconciliations, …). Treated as a secret; never logged.
   - **Read only** (`KASHFLOW_READ_ONLY`) — defaults to **true**.
 3. Cursor starts `scripts/kashflow-mcp`. That launcher picks the matching self-contained binary from this repository’s GitHub Releases (`v1.1.0`), checks `SHA256SUMS`, caches it under `~/.cache/kashflow-mcp` (or `%LOCALAPPDATA%\kashflow-mcp` on Windows), and execs it. An end user does not need the .NET SDK. The first launch needs network access to `github.com`.

`tools/list` succeeds before those values are saved. The first tool call that talks to KashFlow reports a clear error until the SOAP username and password are set. REST tools appear in `tools/list` only when the memorable word is configured. REST failures (login and endpoints such as purchase files) include KashFlow’s error text (for example `Invalid username or password`, or Dropbox/file authorisation messages) — no secrets.

`mcp.json` runs `scripts/kashflow-mcp` (a POSIX shell script, via `${CURSOR_PLUGIN_ROOT}`). Windows also gets `scripts/kashflow-mcp.cmd`, which runs `scripts/kashflow-mcp.ps1`. Hosts that resolve an extensionless command through `PATHEXT` (cmd.exe, and the npm-style lookup used by some MCP clients) pick the `.cmd` automatically. The Cursor plugin docs do not define a per-OS command, so that pair is the portable layout.

## Read-only and read-write

`KashFlow__ReadOnly` defaults to **true**.

- **true** (the marketplace default): only read tools are registered (`get_*`, `is_*`, `search_*`). The SOAP client also refuses every call that would change KashFlow.
- **false**: write tools are registered as well: `insert_customer`, `insert_supplier`, `insert_invoice`, `insert_receipt2`, `insert_journal`, `insert_receipt_payment`, `allocate_bank_payment_to_receipts`, `insert_fx_receipt_payment`, `write_off_receipt_rounding`, `transfer_between_bank_accounts`, and (when the memorable word is set) the REST write tools under [REST API](#rest-api).

Each write tool takes a structured object with documented fields (name, addresses, lines, nominals, and so on). You can still pass the previous full SOAP JSON string (`customerJson`, `supplierJson`, `invoiceJson`, `receiptJson`, `journalJson`). When that string is set, it is used and the structured fields are ignored, so extra text and checkbox fields on Customer still work.

## Purchase receipt payments

These tools record money paid against a purchase receipt (a supplier bill), not a sales invoice.

| Tool | Access | Purpose |
| --- | --- | --- |
| `get_bank_accounts` | read | Bank accounts. `accountId` is the internal id. `accountCode` is the nominal code (1206, 1209, …). `accountName` is the label. |
| `get_receipt_payment_methods` | read | Methods for receipt payments. `methodId` and `methodName`. |
| `get_receipt_payments` | read | Payments already on a receipt number, so a receipt is not paid twice. |
| `search_bank_transactions` | read | Find bank transactions by amount, tolerance, direction, date range, and optional accounts. |
| `insert_receipt_payment` | write (`KashFlow__ReadOnly=false`) | SOAP `InsertReceiptPayment`. |
| `allocate_bank_payment_to_receipts` | write | Split one bank amount across several receipts via SOAP (N bank lines; see [Custom functions](#custom-functions)). |
| `pay_purchases_bulk` | write + memorable word | REST batch pay: one bank debit for many receipts (see [REST](#rest-api)). |
| `link_bank_transactions_to_purchases` | write + memorable word | Link existing bank txs → receipts (see [REST](#rest-api)). |
| `update_purchases_bulk_payment` / `delete_purchases_bulk_payment` | write + memorable word | Edit or remove a bulk payment. |
| `update_purchase_payment_lines` | write + memorable word | In-place PaymentLines edit / bank-tx link. |
| `attach_purchase_file` / `delete_purchase_file` | write + memorable word | Attach or remove receipt files. |
| `insert_fx_receipt_payment` | write | FX receipt paid from GBP (see Custom functions). |
| `write_off_receipt_rounding` | write | Small balancing payment (see Custom functions). |

`insert_receipt_payment` parameters:

- `receiptNumber` (integer, required) — purchase receipt number. Sent as `PayInvoice`.
- `paymentDate` (date-time, required) — sent as `PayDate`.
- `amount` (number, required, greater than 0) — sent as `PayAmount`.
- `paymentMethodId` (integer, optional) — `methodId` from `get_receipt_payment_methods`. Sent as `PayMethod`.
- `paymentMethodName` (string, optional) — used only when `paymentMethodId` is omitted.
- `bankAccountId` (integer, optional) — `accountId` from `get_bank_accounts`. Sent as `PayAccount`.
- `bankAccountCode` (integer, optional) — nominal code such as 1206. Resolved to `accountId`. Used only when `bankAccountId` is omitted.
- `note` (string, optional) — sent as `PayNote`.

One of `paymentMethodId` or `paymentMethodName` is required. One of `bankAccountId` or `bankAccountCode` is required. When both forms are sent, the id is used.

## Other SOAP operations

Hand-written tools cover the common reads and the structured writes above. Every other KashFlow SOAP operation the client can call is also an MCP tool, generated by `scripts/generate_operation_tools.py` into `src/KashflowMCP/Tools/AdditionalOperations.cs`.

The tool name is the operation name in snake case (`UpdateCustomer` is `update_customer`, `InsertInvoicePayment` is `insert_invoice_payment`). Parameters use the SOAP names and types. Non-nullable values are required. `string?` may be omitted. A parameter whose type is a SOAP object (`Customer`, `Invoice`, `Payment`, `BankTransaction`, `Receipt2`, and so on) is one structured object whose fields are that type's properties.

Reads (`Get` / `Is` / `Search`) are always listed. Everything else is a write and is registered only when `KashFlow__ReadOnly` is false. The client also refuses those calls while read-only is on.

Not exposed, because they are not accounting-record operations or they duplicate a cleaner call:

- `createAccount` and `CreateAccount2` create a KashFlow login (password and memorable word).
- `AutoAuthIP` is IP auto-authorisation.
- `SageFile`, `SageFileImport`, `GetDigitaCSVFile`, and `VCFToCustomerObjects` are file or binary payloads.
- `*TypeDefined` operations are the legacy duplicate of the operation without that suffix (`GetInvoice` rather than `GetInvoice_TypeDefined`, and the same for invoices, receipts, and account defaults).

Sales invoice payments are `insert_invoice_payment` (`Payment`: `payInvoice` is the invoice number, `payMethod` the method id, `payAccount` the bank account id, `payAmount` the amount), `get_invoice_payments`, and `delete_invoice_payment` (`invoiceNumber`, `invoicePaymentNumber`). Purchase payment removal is `delete_receipt_payment` (`receiptPaymentNumber`). A single bank transaction is `insert_bank_transaction` (plus `update_bank_transaction` and `delete_bank_transaction`). Moving money between two bank accounts is the custom function `transfer_between_bank_accounts` (one source moneyout whose `transactionType` is the destination nominal id).

## Custom functions

Tools in this section are composed by this server. They are not one-to-one SOAP operations. Generated single-sided tools such as `insert_bank_transaction` stay under [Other SOAP operations](#other-soap-operations).

### `transfer_between_bank_accounts`

KashFlow records a bank-to-bank transfer as **one** `InsertBankTransaction` on the source account:

- `moneyout` = amount, `moneyin` = 0
- `transactionType` = the destination bank account's **nominal id** (`get_nominal_codes` → `id`), not a `get_bank_tx_types` value

KashFlow then shows that same line on the destination bank's feed. Posting a separate money-in on the destination doubles the transfer. This write is available only when `KashFlow__ReadOnly` is false. It does not replace `insert_bank_transaction`.

The destination nominal id is resolved from `toBankAccountId` / `toBankAccountCode` via `get_nominal_codes` (match `BankAccount.AccountCode` to `NominalCode.Code`). Pass `toNominalId` to set or override that id. Source codes such as 1206 use the same account-code lookup as `insert_receipt_payment`.

| Parameter | Required | Meaning |
| --- | --- | --- |
| `amount` | yes | Amount to move, greater than 0. |
| `transferDate` | yes | Date on the source bank transaction. |
| `fromBankAccountId` | one of id or code | Source `accountId` from `get_bank_accounts`. |
| `fromBankAccountCode` | one of id or code | Source nominal code such as 1206. Used when the id is omitted. |
| `toBankAccountId` | one of id, code, or `toNominalId` | Destination `accountId`. |
| `toBankAccountCode` | one of id, code, or `toNominalId` | Destination nominal code such as 1209. |
| `toNominalId` | one of id, code, or `toNominalId` | Destination `get_nominal_codes` id. Stored as `transactionType`. |
| `comment` | no | Stored on the source bank transaction. |

### `allocate_bank_payment_to_receipts`

Add-only. Validates before writing: allocations must sum exactly to `totalAmount` (to the penny), receipt numbers must be unique, each receipt must exist, and no allocation may exceed that receipt's GBP outstanding (book GBP value minus `AmountPaid`; for FX receipts book value is `(net + VAT) / ExchangeRate`). Then calls `InsertReceiptPayment` once per allocation. **Each insert creates its own bank-feed line** — this is not KashFlow UI “New batch payment”. For one bank debit covering many receipts use [`pay_purchases_bulk`](#pay_purchases_bulk). See [docs/multi-receipt-bank-payment.md](docs/multi-receipt-bank-payment.md). If a later insert fails, earlier successes are left in place and listed so they can be undone with `delete_receipt_payment`.

| Parameter | Required | Meaning |
| --- | --- | --- |
| `paymentDate` | yes | Date on every payment. |
| `totalAmount` | yes | Bank amount being split. |
| `allocations` | yes | Array of `{ receiptNumber, amount }` with `amount` &gt; 0. |
| `bankAccountId` / `bankAccountCode` | one | Paying bank account. |
| `paymentMethodId` / `paymentMethodName` | one | Payment method. |
| `note` | no | Stored on every payment. |

## REST API

REST tools are registered only when `KashFlow__MemorableWord` (or plugin `KASHFLOW_MEMORABLE_WORD` / env `KASHFLOW_MEMORABLE_WORD`) is set, in addition to the SOAP username and password. Writes still require `KashFlow__ReadOnly=false` (except `dryRun=true`). Session auth uses `POST`/`PUT /v2/sessiontoken` (memorable-word character challenge) then `Authorization: KfToken {SessionToken}`. The token is cached and refreshed on expiry / 401. REST login uses SOAP `UserName` by default and the **web** password from `KashFlow__RestPassword` / `KASHFLOW_REST_PASSWORD` (optional `RestUserName` only when it differs; when RestPassword is unset, SOAP password is tried). SOAP tools keep using `KashFlow__Password`. Tool failures surface KashFlow’s REST error text (no secrets).

KashFlow marks the REST API as unfinished / likely to break; prefer SOAP tools when they cover the job.

### `pay_purchases_bulk`

UI “New batch payment”: one bank-feed debit allocated across several purchase receipts via `POST /v2/purchases/bulk/payments`. Example: Starling `bankAccountCode` **1206**, payment date **2026-09-10**, `totalAmount` **91.28** = KF04199 **2.40** + KF04200 **88.88**.

| Parameter | Required | Meaning |
| --- | --- | --- |
| `paymentDate` | yes | Sent as `Date` (`yyyy-MM-dd`). |
| `totalAmount` | yes | Must equal the sum of allocations to the penny. |
| `allocations` | yes | `{ receiptNumber, amount }` → `PaymentItems[{ObjectNumber, Amount}]`. |
| `bankAccountId` / `bankAccountCode` | one | Paying bank → `AccountId`. |
| `paymentMethodId` / `paymentMethodName` | one | Payment method → `Method`. |
| `note` | no | Sent as `Comment`. |
| `dryRun` | no | When `true`, return the exact REST request body without writing. Default `false`. |

### `link_bank_transactions_to_purchases`

`PUT /v2/bankaccounts/{id}/transactionlist`. Assigns existing bank transactions to purchase receipts (no new bank line). Swagger: assign then delete the bank transaction (convert into a receipt payment).

| Parameter | Required | Meaning |
| --- | --- | --- |
| `assignments` | yes | `{ transactionId, receiptNumber, paymentMethodId? }`. |
| `bankAccountId` / `bankAccountCode` | one | Bank account path id. |
| `paymentMethodId` / `paymentMethodName` | one\* | Default method when an assignment omits `paymentMethodId`. |
| `dryRun` | no | Exact request body without writing. |

### `get_purchases_bulk_payment` / `update_purchases_bulk_payment` / `delete_purchases_bulk_payment`

`GET` / `PUT` / `DELETE /v2/purchases/bulk/payments/{number}`. Update does GET-then-merge so omitted fields keep current values. `dryRun` on PUT returns the exact body; on DELETE returns method + path (no body).

| Parameter | Required | Meaning |
| --- | --- | --- |
| `bulkPaymentNumber` | yes | Bulk payment number from `pay_purchases_bulk`. |
| `paymentDate`, `note`, bank/method, `allocations` | no (update) | Fields to change; `allocations` replaces the whole split list. |
| `dryRun` | no (writes) | Preview without writing. |

### `update_purchase_payment_lines`

`GET` then `PUT /v2/purchases/{number}` with `PaymentLines` (including optional `BankTransactionId`). Read-modify-write copies line items, supplier, currency, and dates so only payment lines change.

| Parameter | Required | Meaning |
| --- | --- | --- |
| `receiptNumber` | yes | Purchase receipt number. |
| `paymentLines` | yes | Edits: with `id` patch that line; without `id` append (`date`, `amount`, `accountId`, `method` required). |
| `replaceAll` | no | Replace the entire `PaymentLines` array. Default `false`. |
| `dryRun` | no | Exact PUT body without writing. |

### `list_purchase_files` / `attach_purchase_file` / `delete_purchase_file`

`GET /v2/purchases/{number}/files`, upload via `POST /v2/files?objectType=purchases` then `POST /v2/purchases/{number}/files`, delete via `DELETE /v2/purchases/{number}/file?fileName=`. Attach accepts `filePath` or `contentBase64` (+ `fileName`); max **10 MiB**. Multipart field name is `file` (swagger body undocumented — confirm live). Writes support `dryRun`.

### `list_bank_reconciliations` / `get_bank_reconciliation_metadata` / `get_bank_reconciliation`

Read-only reconciliation status: `GET /v2/bankaccounts/{id}/reconciliations`, `/reconciliations/metadata`, and `/reconciliations/{reconciliationId}`.

### `list_bank_account_transactions` / `list_bank_feed_transactions`

Richer bank-feed reads than SOAP `search_bank_transactions`. `GET /v2/bankaccounts/{id}/transactions` accepts `filterBy` (for example `Unallocated`), `includeFeedMatched`, date range, reconciled flag, and paging. `list_bank_feed_transactions` calls `/transactions/bfstransactions`.

### Forex settle

KashFlow REST has **no** UI Settle / FX gain-loss endpoint. Do not expect a REST settle tool; use the UI or the SOAP FX companion pattern (`insert_fx_receipt_payment`).

### `search_bank_transactions`

Read tool. Calls `GetBankTransactions` for each selected bank account (or all accounts) and filters client-side. Returns at most 200 matches with account code/name, transaction id, date, money in/out, comment, and type.

| Parameter | Required | Meaning |
| --- | --- | --- |
| `amount` | yes | Target amount. |
| `tolerance` | no | Absolute difference allowed (default 0). |
| `direction` | no | `in`, `out`, or `any` (default). |
| `dateFrom` / `dateTo` | yes | Inclusive date range. |
| `bankAccountIds` / `bankAccountCodes` | no | Limit to these accounts; omit both to search all. |

### `insert_fx_receipt_payment`

Add-only booking for a foreign-currency purchase receipt paid from a GBP bank account (the Logic Ethos pattern used on pairs such as KF04151/4165):

1. `InsertReceiptPayment` for `bankAmountGbp` from the bank account.
2. An **Other** payment on the same receipt with note `Currency Fluctuation` for `outstanding - bankAmountGbp` (may be negative).
3. A companion GBP purchase receipt for the same supplier on nominal **956** (Forex Gain/Loss), description `Loss/Gain on KF##### due to exchange rate fluctuations`, net = `-(step 2 amount)`, paid in full as Other with that same companion amount.

If a later step fails, payments and the companion receipt created by this call are deleted when possible.

### `write_off_receipt_rounding`

Add-only. Posts a balancing `InsertReceiptPayment` so a near-zero outstanding shows as paid.

- Outstanding = `net + VAT - amountPaid`.
- Refuses when outstanding is 0 or `abs(outstanding) > maxAmount` (default `0.05`).
- **Nominal attribution:** `nominal` is required (no default). It must be a `get_bank_accounts` `accountCode` (a bank/cash account mapped to that nominal). The payment uses method **Other** and `payAccount` = that account's `accountId`. Create or pick such an account in KashFlow before calling this tool.

| Parameter | Required | Meaning |
| --- | --- | --- |
| `receiptNumber` | yes | Purchase receipt number. |
| `paymentDate` | yes | Payment date. |
| `nominal` | yes | Bank `accountCode` / nominal code used as `payAccount`. |
| `maxAmount` | no | Max absolute outstanding (default 0.05). |
| `note` | no | Payment note (default `Rounding write-off`). |

## Configuration

| Plugin field | Environment variable | Meaning |
| --- | --- | --- |
| `KASHFLOW_USERNAME` | `KashFlow__UserName` | SOAP API username. |
| `KASHFLOW_PASSWORD` | `KashFlow__Password` | SOAP API password. Never logged. |
| `KASHFLOW_REST_USERNAME` | `KashFlow__RestUserName` (also accepts bare `KASHFLOW_REST_USERNAME`) | Optional. Defaults to SOAP username (usually enough). |
| `KASHFLOW_REST_PASSWORD` | `KashFlow__RestPassword` (also accepts bare `KASHFLOW_REST_PASSWORD`) | Web login password for REST. Never logged. Falls back to SOAP password when empty. |
| `KASHFLOW_MEMORABLE_WORD` | `KashFlow__MemorableWord` (also accepts bare `KASHFLOW_MEMORABLE_WORD`) | Optional REST memorable word. Enables REST tools. Never logged. |
| `KASHFLOW_READ_ONLY` | `KashFlow__ReadOnly` | `true` (default) or `false`. |

Optional, for a local checkout only (not part of the marketplace form): `KashFlow__Endpoint`, `KashFlow__RestBaseAddress` (default `https://api.kashflow.com/v2/`), and `KashFlow__TimeoutSeconds`. The SOAP endpoint defaults to `https://securedwebapp.com/api/service.asmx`.

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

Tag `v1.1.0` (the version in `.cursor-plugin/plugin.json`) and push it. `.github/workflows/release.yml` builds, tests, and uploads:

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
