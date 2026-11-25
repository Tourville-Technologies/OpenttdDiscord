using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OpenttdDiscord.Domain.Servers;

namespace OpenttdDiscord.Infrastructure.Ottd.Game;

internal sealed class GameServiceControl(IConfiguration configuration, ILogger<GameServiceControl> logger)
{
    private readonly ConcurrentDictionary<Guid, byte> running = new();

    public async Task<GameReply> Execute(OttdServer server, string action)
    {
        string guild = server.GuildId.ToString(CultureInfo.InvariantCulture);

        // An array avoids treating characters in a registered server name as configuration separators.
        var settings = configuration.GetSection("OpenTTD:Services").GetChildren().FirstOrDefault(entry =>
            entry["GuildId"] == guild && string.Equals(entry["Server"], server.Name, StringComparison.OrdinalIgnoreCase));
        var args = settings?.GetSection(action).GetChildren().Select(value => value.Value).ToArray();
        if (args is not { Length: > 0 } || string.IsNullOrWhiteSpace(args[0]) || !Path.IsPathFullyQualified(args[0]!))
        {
            return new GameReply($"Service {action} is not configured for this server.");
        }

        if (!running.TryAdd(server.Id, 0))
        {
            return new GameReply("A service operation is already running for this server.");
        }

        try
        {
            var startInfo = new ProcessStartInfo(args[0]!) { UseShellExecute = false };
            foreach (string? argument in args.Skip(1))
            {
                startInfo.ArgumentList.Add(argument ?? string.Empty);
            }

            using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start the configured service command.");
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested)
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }

                return new GameReply($"Service {action} timed out. Check the host to confirm its state.");
            }

            return process.ExitCode == 0
                ? new GameReply($"Service {action} completed.")
                : new GameReply($"Service {action} failed with exit code {process.ExitCode}.");
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            logger.LogWarning(ex, "Service {Action} failed for OpenTTD server {ServerId}", action, server.Id);
            return new GameReply($"Could not run service {action}. Check the bot host logs.");
        }
        finally
        {
            running.TryRemove(server.Id, out _);
        }
    }
}
