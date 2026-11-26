# Multi-receipt payment → single bank line

Investigation (docs / generated SOAP client only; no live KashFlow writes).

## Goal

Pay several purchase receipts from **one** bank transaction (KashFlow UI “New batch payment”), e.g. one £91.28 Starling debit covering KF04199 (£2.40) and KF04200 (£88.88).

Today `allocate_bank_payment_to_receipts` calls SOAP `InsertReceiptPayment` once per receipt, which creates **one bank-feed line per receipt**.

## SOAP API (supported by this connector)

Sources: [service.asmx operations](https://securedwebapp.com/api/service.asmx), generated client `src/KashflowDLL/Generated/KashFlowClient.Operations.g.cs`, messages in `Messages.g.cs`.

| Operation | Shape | Multi-receipt single bank line? |
| --- | --- | --- |
| `InsertReceiptPayment` | One `Payment` → one receipt | No — each call adds its own bank row (observed: bank `txid` = receipt DB id for positive payments) |
| `AllocateAdvancePaymentToReceipt` | `ReceiptNumber` + `BankTxId` only (no amount) | No — allocates **one whole** bank transaction to **one** receipt |
| `AllocateAdvancePaymentToInvoice` | Same for sales invoices | Same 1:1 limit |
| `InsertBankTransaction` | Single bank row (`moneyin`/`moneyout`, optional `SupplierId` / `CustomerId` for advances) | Creates the bank row only; does not attach multiple receipts |

Official advance-payment APIs were added so you can `InsertBankTransaction` then allocate later ([changelog](https://www.kashflow.com/change-log/a-few-improvements-to-the-api/)). Bank UI assignment also refuses one transaction → many invoices: [Assigning transactions](https://www.kashflow.com/support/kb/assigning-transactions-to-invoicespurchases/) (“This will not allow you to assign 1 transaction to multiple invoices.”).

There is **no** SOAP operation that posts a batch purchase payment or attaches several receipt allocations to one bank transaction id.

## UI “pay multiple” / batch payment

KashFlow UI: **Purchases → New batch payment** (also documented as “Record a payment for multiple purchases”).

- [IRIS help](https://help-iris.co.uk/accountancy/kashflow/purchases/payments/record-batch-purchase-payments.htm): one payment for multiple purchases with the same supplier; “a batch payment entry will be added to your chosen bank account.”
- [KB](https://www.kashflow.com/support/kb/recording-batch-payment-on-purchases/): same behaviour.
- Ideas portal: invoice side uses “New Batch Payment” ([KF-I-4176](https://kashflow.ideas.aha.io/ideas/KF-I-4176)).

## REST API (not wired in this repo)

`POST https://api.kashflow.com/v2/purchases/bulk/payments` is the documented equivalent of UI batch pay: one payment against multiple purchases (`Date`, `AccountId`, `Method`, `PaymentItems[{ObjectNumber, Amount}]`). Docs: [Bulk Payments](https://www.kashflow.com/developers/rest-api/bulk-payment/).

Purchase payment lines also expose `BulkId` / `BulkPaymentNumber` / `BankTransactionId` on the purchases entity ([Purchases](https://www.kashflow.com/developers/rest-api/api-purchases/)).

### Why this connector cannot call it with current credentials

1. REST auth is **not** SOAP UserName/Password alone. It needs a session token: POST `/v2/sessiontoken` → memorable-word character challenge → PUT → `Authorization: KfToken {SessionToken}` ([Authentication](https://www.kashflow.com/developers/rest-api/authentication/), [Session token](https://www.kashflow.com/developers/rest-api/sessiontoken/)).
2. This MCP/plugin only stores SOAP credentials and explicitly is **not** the REST memorable word (`README.md`, `.cursor-plugin/plugin.json`).
3. KashFlow’s own REST pages warn the API is unfinished / likely to break; Apideck and others still integrate SOAP for that reason.

So a `pay_receipts_from_single_bank_line` tool is **not implementable** on the SOAP surface this project supports, and REST would require new auth (memorable word or an IRIS-issued external token) plus a separate HTTP client.

## Conclusion

| Path | Verdict |
| --- | --- |
| SOAP composition (`InsertReceiptPayment` × N, or advance allocate) | Cannot produce one bank line for N receipts |
| REST `purchases/bulk/payments` | Matches UI batch pay, but blocked by auth + unstable REST disclaimer |
| Tool implementation in this PR | **Not done** — avoid a half-baked REST path |

Workaround for agents today: use the KashFlow UI batch payment, or accept N bank lines via `allocate_bank_payment_to_receipts` / `insert_receipt_payment`.

## Live tests still needed (if REST is ever adopted)

- Confirm memorable-word login works with the same API password as SOAP.
- Confirm one `purchases/bulk/payments` POST yields a single bank-feed debit equal to the sum (and how `BankTransactionId` appears on payment lines).
- Cross-supplier batches (UI help says “same supplier”; REST docs do not clearly forbid mixed suppliers).
- Rollback / DELETE of a bulk payment vs `DeleteReceiptPayment`.
