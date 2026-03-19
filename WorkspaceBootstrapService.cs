using DecompilerServer.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DecompilerServer;

public sealed class WorkspaceBootstrapService(
    ILogger<WorkspaceBootstrapService> log,
    DecompilerWorkspace workspace) : BackgroundService
{
    private readonly ILogger<WorkspaceBootstrapService> _log = log;
    private readonly DecompilerWorkspace _workspace = workspace;

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var restored = _workspace.RestoreRegisteredContexts();

        if (restored.Count == 0)
        {
            _log.LogInformation("No registered workspace aliases found.");
            return Task.CompletedTask;
        }

        foreach (var result in restored)
        {
            if (result.Loaded)
            {
                _log.LogInformation("Loaded registered context {Alias} -> {AssemblyPath}", result.ContextAlias, result.AssemblyPath);
            }
            else
            {
                _log.LogWarning("Failed to load registered context {Alias} -> {AssemblyPath}: {ErrorMessage}",
                    result.ContextAlias,
                    result.AssemblyPath,
                    result.ErrorMessage);
            }
        }

        return Task.CompletedTask;
    }
}
