using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DecompilerServer;

public sealed class StartupLogService(ILogger<StartupLogService> log) : BackgroundService
{
	private readonly ILogger<StartupLogService> _log = log;

	protected override Task ExecuteAsync(CancellationToken stoppingToken)
	{
		_log.LogInformation("Background service online.");
		return Task.CompletedTask;
	}
}