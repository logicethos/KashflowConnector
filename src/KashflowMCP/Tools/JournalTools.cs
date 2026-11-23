using System.ComponentModel;
using KashFlow;
using ModelContextProtocol.Server;

namespace KashflowMCP.Tools;

[McpServerToolType]
public sealed class JournalTools(KashFlowSession session)
{
    [McpServerTool(Name = "get_journals"), Description("List journal entries.")]
    public async Task<string> GetJournals(CancellationToken cancellationToken)
        => JsonResults.Serialize(await session.Client.GetJournalsAsync(cancellationToken));

    [McpServerTool(Name = "get_journal"), Description("Get a journal entry by journal number.")]
    public async Task<string> GetJournal(
        [Description("Journal number")] int journalNumber,
        CancellationToken cancellationToken)
        => JsonResults.Serialize(await session.Client.GetJournalAsync(journalNumber, cancellationToken));
}
