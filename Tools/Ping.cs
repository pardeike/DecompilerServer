using System.ComponentModel;
using ModelContextProtocol.Server;
using DecompilerServer.Services;

namespace DecompilerServer;

public static class PingTool
{
    [McpServerTool, Description("Connectivity check. Returns 'pong' and current MVID if loaded.")]
    public static string Ping()
    {
        return ResponseFormatter.TryExecute(() =>
        {
            var contextManager = ServiceLocator.ContextManager;
            var timeUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            var result = new
            {
                pong = true,
                mvid = contextManager.IsLoaded ? contextManager.Mvid : null,
                timeUnix = timeUnix
            };

            return result;
        });
    }
}
