#!/usr/bin/env python3
"""Emit MCP wrappers for KashFlowClient operations that are not hand-written.

Reads src/KashflowDLL/Generated/KashFlowClient.Operations.g.cs and the existing
[McpServerTool(Name = ...)] tools. Writes
src/KashflowMCP/Tools/AdditionalOperations.g.cs.
"""

from __future__ import annotations

import re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
CLIENT = ROOT / "src/KashflowDLL/Generated/KashFlowClient.Operations.g.cs"
TOOLS = ROOT / "src/KashflowMCP/Tools"
OUTPUT = TOOLS / "AdditionalOperations.cs"

# Account provisioning, auth, and binary/file payloads. The SOAP service has
# these operations; this server does not expose them.
SKIP_METHODS = {
    "createAccountAsync",
    "CreateAccount2Async",
    "AutoAuthIPAsync",
    "SageFileAsync",
    "SageFileImportAsync",
    "GetDigitaCSVFileAsync",
    "VCFToCustomerObjectsAsync",
}

SKIP_REASON = {
    "createAccountAsync": "creates a KashFlow login and takes a password and memorable word",
    "CreateAccount2Async": "creates a KashFlow account rather than accounting data",
    "AutoAuthIPAsync": "IP auto-authorisation, not an accounting operation",
    "SageFileAsync": "binary Sage file payload",
    "SageFileImportAsync": "Sage import job, not a record operation",
    "GetDigitaCSVFileAsync": "returns a Digita CSV file rather than records",
    "VCFToCustomerObjectsAsync": "binary vCard payload",
}


def snake(operation: str) -> str:
    text = operation.replace("_", "")
    for acronym, word in (("KPIs", "Kpis"), ("SaaS", "Saas"), ("VAT", "Vat"), ("URL", "Url"), ("CSV", "Csv"), ("ID", "Id")):
        text = text.replace(acronym, word)
    text = re.sub(r"([a-z0-9])([A-Z])", r"\1_\2", text)
    text = re.sub(r"([A-Z]+)([A-Z][a-z])", r"\1_\2", text)
    return re.sub(r"_+", "_", text).lower()


def is_read(operation: str) -> bool:
    return operation.startswith(("Get", "Is", "is", "Search"))


def split_params(parameter_list: str) -> list[str]:
    parts: list[str] = []
    depth = 0
    current: list[str] = []
    for char in parameter_list:
        if char == "<":
            depth += 1
        elif char == ">":
            depth -= 1
        elif char == "," and depth == 0:
            parts.append("".join(current).strip())
            current = []
            continue
        current.append(char)
    tail = "".join(current).strip()
    if tail:
        parts.append(tail)
    return [part for part in parts if part]


def parse_param(part: str) -> tuple[str, str]:
    part = part.split("=")[0].strip()
    name = part.split()[-1].lstrip("@")
    type_name = part[: part.rfind(name)].strip()
    return type_name, name


def take_type(source: str, index: int) -> tuple[str, int]:
    start = index
    if source.startswith("Task<", index):
        depth = 0
        while index < len(source):
            if source[index] == "<":
                depth += 1
            elif source[index] == ">":
                depth -= 1
                if depth == 0:
                    return source[start : index + 1], index + 1
            index += 1
        raise ValueError("unbalanced Task type")
    if source.startswith("Task", index):
        return "Task", index + len("Task")
    raise ValueError("expected Task at " + source[index : index + 20])


def parse_methods(source: str) -> list[dict]:
    methods = []
    cursor = 0
    marker = "public Task"
    while True:
        start = source.find(marker, cursor)
        if start < 0:
            break
        return_type, index = take_type(source, start + len("public "))
        while source[index].isspace():
            index += 1
        name_match = re.match(r"(\w+Async)\(", source[index:])
        if not name_match:
            cursor = start + len(marker)
            continue
        method = name_match.group(1)
        paren = index + name_match.end() - 1
        depth = 0
        end = paren
        for pos in range(paren, len(source)):
            if source[pos] == "(":
                depth += 1
            elif source[pos] == ")":
                depth -= 1
                if depth == 0:
                    end = pos
                    break
        params = source[paren + 1 : end]
        operation = method[: -len("Async")]
        parameters = []
        for part in split_params(params):
            type_name, name = parse_param(part)
            if name == "cancellationToken":
                continue
            parameters.append((type_name, name))
        summary = "Calls the KashFlow SOAP operation " + operation + "."
        previous = source.rfind("public Task", 0, start)
        window = source[previous + 1 : start] if previous >= 0 else source[:start]
        summaries = re.findall(r"<summary>(.*?)</summary>", window, flags=re.S)
        if summaries:
            text = re.sub(r"\s+", " ", summaries[-1]).strip()
            if text and not text.startswith("Calls the "):
                summary = text
        methods.append(
            {
                "method": method,
                "operation": operation,
                "return": return_type,
                "parameters": parameters,
                "summary": summary,
            }
        )
        cursor = end + 1
    return methods


def csharp_string(text: str) -> str:
    return text.replace("\\", "\\\\").replace('"', "'")


def wrapped_methods() -> set[str]:
    found = set()
    for path in TOOLS.glob("*.cs"):
        if path.name == OUTPUT.name:
            continue
        found.update(re.findall(r"Client\.(\w+Async)\(", path.read_text()))
    return found


def existing_tool_names() -> set[str]:
    found = set()
    for path in TOOLS.glob("*.cs"):
        if path.name == OUTPUT.name:
            continue
        found.update(re.findall(r'McpServerTool\(Name = "([^"]+)"', path.read_text()))
    return found


def sibling_operation(operation: str, operations: set[str]) -> str | None:
    for token in ("_TypeDefined", "_typeDefined", "TypeDefined"):
        if token in operation:
            candidate = operation.replace(token, "").replace("__", "_").strip("_")
            if candidate and candidate in operations and candidate != operation:
                return candidate
    return None


DOCUMENT_RECEIPT_OPS = {
    "InsertReceipt",
    "UpdateReceipt",
    "UpdateReceiptHeader",
    "UpdateReceipt2",
}

LINE_RECEIPT_OPS = {
    "InsertReceiptLine",
    "InsertReceiptLineFromNumber",
    "InsertReceiptLine2",
    "InsertReceiptLineFromNumber2",
}

DOCUMENT_CURRENCY_NOTE = (
    ". Line Rate and VatAmount are document currency. KashFlow stores SOAP receipt line amounts in GBP, "
    "so foreign lines are divided by ExchangeRate (foreign currency units per 1 GBP) before the call. "
    "ExchangeRate is required when CurrencyCode is not GBP. Header NetAmount and VATAmount stay in the document currency."
)

LINE_CURRENCY_NOTE = (
    ". Rate and VatAmount are GBP (KashFlow home currency) unless currencyCode is not GBP, "
    "in which case they are document currency and exchangeRate (foreign units per 1 GBP) is required."
)


def render_method(method: dict, read: bool) -> str:
    description = method["summary"]
    if not read:
        description = "Requires KashFlow__ReadOnly=false. " + description
    operation = method["operation"]
    if operation in DOCUMENT_RECEIPT_OPS:
        description += DOCUMENT_CURRENCY_NOTE
    if operation in LINE_RECEIPT_OPS:
        description += LINE_CURRENCY_NOTE
    line_name = None
    parameters = []
    call_args = []
    for type_name, name in method["parameters"]:
        hint = f"SOAP parameter {name} ({type_name})."
        if type_name == "Payment":
            hint += " PayInvoice is the document number, PayMethod the method id, PayAccount the bank account id, PayAmount the amount."
        if type_name == "BankTransaction":
            hint += " accid is the bank account id, moneyin and moneyout are the amounts, transactionType comes from get_bank_tx_types."
        if operation in LINE_RECEIPT_OPS and type_name in {"ReceiptLine2", "InvoiceLine"}:
            hint += " GBP unless currencyCode is a foreign code."
            line_name = name
        parameters.append(
            f'        [Description("{csharp_string(hint)}")] {type_name} {name}'
        )
        call_args.append("soapLine" if name == line_name else name)
    if line_name:
        parameters.append(
            '        [Description("Document currency. Omit or pass GBP when the line amounts are already GBP. When this is not GBP, Rate and VatAmount are document currency.")] string? currencyCode = null'
        )
        parameters.append(
            '        [Description("Foreign currency units per 1 GBP. Required when currencyCode is not GBP. Example: 1.3123 for USD. Do not pass GBP per foreign unit.")] decimal? exchangeRate = null'
        )
        parameters.append(
            '        [Description("Must be ForeignPerGbp when set. GbpPerForeign is rejected.")] string? rateConvention = null'
        )
    call_args.append("cancellationToken")
    signature = ",\n".join(parameters)
    if signature:
        signature += ",\n"
    signature += "        CancellationToken cancellationToken = default"
    body_call = f'session.Client.{method["method"]}({", ".join(call_args)})'
    if method["return"] == "Task":
        body = f"""        await {body_call};
        return JsonResults.Serialize(new {{ ok = true }});"""
    else:
        body = f"        return JsonResults.Serialize(await {body_call});"
    guard = "" if read else "        ToolGuard.EnsureWritable(runtime);\n"
    convert = ""
    if line_name:
        convert = (
            f"        var soapLine = ReceiptCurrency.ToSoapLine({line_name}, currencyCode, exchangeRate, rateConvention);\n"
        )
    return f"""    [McpServerTool(Name = "{method["tool"]}"), Description("{csharp_string(description)}")]
    public async Task<string> {method["operation"]}(
{signature})
    {{
{guard}{convert}{body}
    }}
"""


def main() -> None:
    source = CLIENT.read_text()
    methods = parse_methods(source)
    operations = {method["operation"] for method in methods}
    wrapped = wrapped_methods()
    names = existing_tool_names()
    skipped: list[str] = []
    selected: list[dict] = []
    used_names = set(names)
    for method in methods:
        full = method["method"]
        if full in SKIP_METHODS:
            skipped.append(f'{method["operation"]}: {SKIP_REASON[full]}')
            continue
        if any("byte[" in type_name for type_name, _ in method["parameters"]):
            skipped.append(f'{method["operation"]}: binary parameter')
            continue
        if any(name.lower() in {"password", "memorableword"} for _, name in method["parameters"]):
            skipped.append(f'{method["operation"]}: takes a password')
            continue
        sibling = sibling_operation(method["operation"], operations)
        if sibling:
            skipped.append(
                f'{method["operation"]}: legacy TypeDefined duplicate of {sibling}'
            )
            continue
        # Some writes are also called from hand-written tools (rollback, transfers).
        # They still need their own MCP tool.
        if full in wrapped and full not in {
            "InsertBankTransactionAsync",
            "DeleteBankTransactionAsync",
            "DeleteReceiptAsync",
            "DeleteReceiptPaymentAsync",
        }:
            skipped.append(f'{method["operation"]}: already wrapped by a hand-written tool')
            continue
        tool = snake(method["operation"])
        if tool in used_names:
            skipped.append(f'{method["operation"]}: tool name {tool} is already used')
            continue
        used_names.add(tool)
        method["tool"] = tool
        method["read"] = is_read(method["operation"])
        selected.append(method)

    reads = [method for method in selected if method["read"]]
    writes = [method for method in selected if not method["read"]]
    header = """// Generated by scripts/generate_operation_tools.py.
// Hand-written tools in this folder take precedence. Do not edit by hand.
#nullable enable

using System.ComponentModel;
using KashFlow;
using ModelContextProtocol.Server;

namespace KashflowMCP.Tools;

"""
    skip_comment = "\n".join("// " + line for line in skipped)
    read_body = "\n".join(render_method(method, read=True) for method in reads)
    write_body = "\n".join(render_method(method, read=False) for method in writes)
    text = f"""{header}{skip_comment}

/// <summary>Read SOAP operations that do not already have a hand-written tool.</summary>
[McpServerToolType]
public sealed class AdditionalReadTools(KashFlowSession session)
{{
{read_body}}}

/// <summary>Write SOAP operations. Registered only when KashFlow__ReadOnly is false.</summary>
[McpServerToolType]
public sealed class AdditionalWriteTools(KashFlowSession session, McpRuntimeOptions runtime)
{{
{write_body}}}
"""
    OUTPUT.write_text(text)
    print(f"methods {len(methods)} selected {len(selected)} read {len(reads)} write {len(writes)} skipped {len(skipped)}")
    print("wrote", OUTPUT)


if __name__ == "__main__":
    main()
