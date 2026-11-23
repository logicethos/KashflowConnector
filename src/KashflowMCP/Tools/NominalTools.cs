using System.ComponentModel;
using KashFlow;
using ModelContextProtocol.Server;

namespace KashflowMCP.Tools;

[McpServerToolType]
public sealed class NominalTools(KashFlowClient client)
{
    [McpServerTool(Name = "get_nominal_codes"), Description("List nominal codes.")]
    public async Task<string> GetNominalCodes(CancellationToken cancellationToken)
        => JsonResults.Serialize(await client.GetNominalCodesAsync(cancellationToken));

    [McpServerTool(Name = "get_nominal_codes_extended"), Description("List extended nominal codes.")]
    public async Task<string> GetNominalCodesExtended(CancellationToken cancellationToken)
        => JsonResults.Serialize(await client.GetNominalCodesExtendedAsync(cancellationToken));

    [McpServerTool(Name = "get_nominal_ledger"), Description("Get nominal ledger lines for a date range and nominal ID.")]
    public async Task<string> GetNominalLedger(
        [Description("Start date")] DateTime startDate,
        [Description("End date")] DateTime endDate,
        [Description("Nominal ID")] int nominalId,
        CancellationToken cancellationToken)
        => JsonResults.Serialize(await client.GetNominalLedgerAsync(startDate, endDate, nominalId, cancellationToken));

    [McpServerTool(Name = "get_vat_rates"), Description("List VAT rates.")]
    public async Task<string> GetVatRates(CancellationToken cancellationToken)
        => JsonResults.Serialize(await client.GetVATRatesAsync(cancellationToken));

    [McpServerTool(Name = "get_trial_balance"), Description("Get trial balance for a date range.")]
    public async Task<string> GetTrialBalance(
        [Description("Start date")] DateTime startDate,
        [Description("End date")] DateTime endDate,
        CancellationToken cancellationToken)
        => JsonResults.Serialize(await client.GetTrialBalanceAsync(startDate, endDate, cancellationToken));
}
