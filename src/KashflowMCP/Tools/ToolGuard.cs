using KashFlow;

namespace KashflowMCP.Tools;

internal static class ToolGuard
{
    public static void EnsureWritable(McpRuntimeOptions runtime)
    {
        if (runtime.ReadOnly)
            throw new InvalidOperationException(
                "This tool is unavailable while KashFlow__ReadOnly is true (default). Set KashFlow__ReadOnly=false to enable writes.");
    }
}
