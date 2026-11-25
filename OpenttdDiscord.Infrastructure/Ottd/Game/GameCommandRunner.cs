using Akka.Actor;
using Discord;
using Microsoft.Extensions.Logging;
using OpenttdDiscord.Base.Ext;
using OpenttdDiscord.Domain.Servers;
using OpenttdDiscord.Infrastructure.Akkas;
using OpenttdDiscord.Infrastructure.Discord.CommandResponses;
using OpenttdDiscord.Infrastructure.Discord.CommandRunners;
using OpenttdDiscord.Infrastructure.Servers;

namespace OpenttdDiscord.Infrastructure.Ottd.Game;

internal sealed class GameCommandRunner(
    IOttdServerRepository servers,
    IAkkaService akka,
    GameServiceControl serviceControl,
    IDiscordClient discord,
    ILogger<GameCommandRunner> logger) : IOttdSlashCommandRunner
{
    public EitherAsync<IError, IInteractionResponse> Run(ISlashCommandInteraction command)
    {
        if (command.GuildId is null || command.ChannelId is null)
        {
            return new HumanReadableError("Run this command in a Discord server.");
        }

        if (command.Data.Name == "game-admin" && command.User is not IGuildUser { GuildPermissions.Administrator: true })
        {
            return new HumanReadableError("Discord Administrator permission is required.");
        }

        var subcommand = command.Data.Options.Single();
        var options = subcommand.Options?.ToDictionary(option => option.Name, option => option.Value) ?? new();
        string companyOption = subcommand.Name == "profit" ? "company-id" : "company";
        int? company = options.GetValueOrDefault(companyOption) is long number ? checked((int)number) : null;
        string? vote = options.GetValueOrDefault("vote") as string;

        var role = options.GetValueOrDefault("role") as IRole;
        if (role is not null && (role.Guild.Id != command.GuildId || role.Id == command.GuildId))
        {
            return new HumanReadableError("Select a role from this Discord server other than @everyone.");
        }

        return RightAsync<IError, IInteractionResponse>(
            new GameInteractionResponse(subcommand.Name != "unpause", () => RunOnServer(command, subcommand.Name, company, vote, role), discord));
    }

    private async Task<GameReply> RunOnServer(ISlashCommandInteraction command, string action, int? company, string? vote, IRole? role)
    {
        var lookup = await servers.GetSingleServer(command.GuildId!.Value);
        if (lookup.IsLeft)
        {
            return lookup.Match(_ => throw new InvalidOperationException(), error => new GameReply(error is HumanReadableError ? error.Reason : "Could not look up the registered server."));
        }

        OttdServer server = lookup.Right();
        if (action is "start" or "stop" or "restart")
        {
            return await serviceControl.Execute(server, action);
        }

        if (action == "ping-role")
        {
            var saved = await servers.SetUnpausePingRole(server.Id, role?.Id);
            return saved.Match(
                _ => new GameReply(role is null ? "Unpause vote ping role cleared." : $"Unpause votes will ping {role.Mention}."),
                error => new GameReply(error is HumanReadableError ? error.Reason : "Could not save the unpause vote ping role."));
        }

        var request = new GameRequest(server.Id, server.GuildId, command.User.Id, command.ChannelId!.Value, action, company, vote);
        try
        {
            var selected = await akka.SelectActor(MainActors.Paths.Guilds);
            var reply = await selected.Right().Ask<GameReply>(request, TimeSpan.FromSeconds(15));
            return reply.OpenedVote.HasValue
                ? reply with { Text = $"**{Format.Sanitize(server.Name)}** · {reply.Text}", PingRoleId = server.UnpausePingRoleId }
                : reply;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "OpenTTD game command {Action} failed for server {ServerId}", action, server.Id);
            return new GameReply("The bot could not reach the registered OpenTTD server. Try again after it reconnects.");
        }
    }
}
