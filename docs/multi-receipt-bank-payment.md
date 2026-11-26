# Multi-receipt payment → single bank line

## Goal

Pay several purchase receipts from **one** bank transaction (KashFlow UI “New batch payment”), e.g. one £91.28 Starling debit covering KF04199 (£2.40) and KF04200 (£88.88) on 10 Sep 2026.

## SOAP (still cannot do this)

| Operation | Multi-receipt single bank line? |
| --- | --- |
| `InsertReceiptPayment` | No — each call adds its own bank row |
| `AllocateAdvancePaymentToReceipt` | No — one whole bank tx → one receipt |
| `InsertBankTransaction` | Creates the bank row only |

`allocate_bank_payment_to_receipts` therefore creates **N bank-feed lines**.

## REST (implemented)

Tool: **`pay_purchases_bulk`** → `POST https://api.kashflow.com/v2/purchases/bulk/payments`

Requires:

- SOAP `KashFlow__UserName` / `KashFlow__Password`
- REST `KashFlow__MemorableWord` (or `KASHFLOW_MEMORABLE_WORD`) for KfToken session auth
- `KashFlow__ReadOnly=false` for a real write (`dryRun=true` validates without writing)

Auth flow: `POST /v2/sessiontoken` → memorable-word character challenge → `PUT /v2/sessiontoken` → `Authorization: KfToken {SessionToken}` ([Authentication](https://www.kashflow.com/developers/rest-api/authentication/), [Session token](https://www.kashflow.com/developers/rest-api/sessiontoken/), [Bulk Payments](https://www.kashflow.com/developers/rest-api/bulk-payment/)).

Example dry-run body for the Starling case (account id and method id resolved at runtime):

```json
{
  "Date": "2026-09-10",
  "Comment": "optional note",
  "AccountId": 12345,
  "Method": 678,
  "PaymentItems": [
    { "ObjectNumber": 4199, "Amount": 2.40 },
    { "ObjectNumber": 4200, "Amount": 88.88 }
  ]
}
```

## Live tests still needed

- Confirm memorable-word login works with the same API password as SOAP.
- Confirm one `purchases/bulk/payments` POST yields a single bank-feed debit equal to the sum (and how `BankTransactionId` appears on payment lines).
- Cross-supplier batches (UI help says “same supplier”; REST docs do not clearly forbid mixed suppliers).
- Rollback via `delete_purchases_bulk_payment` vs SOAP `DeleteReceiptPayment`.

## Related REST tools (built)

| Tool | Endpoint |
| --- | --- |
| `link_bank_transactions_to_purchases` | `PUT /v2/bankaccounts/{id}/transactionlist` |
| `update_purchases_bulk_payment` / `delete_purchases_bulk_payment` | `PUT` / `DELETE /v2/purchases/bulk/payments/{number}` |
| `update_purchase_payment_lines` | `GET`+`PUT /v2/purchases/{number}` (`PaymentLines`, optional `BankTransactionId`) |
| `attach_purchase_file` / `list_purchase_files` / `delete_purchase_file` | `/files` + `/purchases/{number}/files` |
| `list_bank_reconciliations` (+ metadata / one) | `/bankaccounts/{id}/reconciliations…` |
| `list_bank_account_transactions` | `GET …/transactions` (`filterBy=Unallocated`, `includeFeedMatched`, …) |

**Not in REST:** UI Settle / FX gain-loss (no swagger settle endpoint). Use the UI or SOAP `insert_fx_receipt_payment`.
