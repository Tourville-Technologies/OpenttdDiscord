using Akka.Actor;
using Discord;
using Microsoft.Extensions.Logging;
using OpenttdDiscord.Base.Ext;
using OpenttdDiscord.Domain.Roles.UseCases;
using OpenttdDiscord.Domain.Security;
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
    IGameVoteReactions voteReactions,
    IGetRoleLevelUseCase roleLevels,
    IRegisterRoleUseCase registerRole,
    ILogger<GameCommandRunner> logger) : IOttdSlashCommandRunner
{
    public EitherAsync<IError, IInteractionResponse> Run(ISlashCommandInteraction command)
    {
        if (command.GuildId is null || command.ChannelId is null)
        {
            return new HumanReadableError("Run this command in a Discord server.");
        }

        var subcommand = command.Data.Options.Single();
        var options = subcommand.Options?.ToDictionary(option => option.Name, option => option.Value) ?? new();
        string companyOption = subcommand.Name == "profit" ? "company-id" : "company";
        int? company = options.GetValueOrDefault(companyOption) is long number ? checked((int)number) : null;
        var role = options.GetValueOrDefault("role") as IRole;
        if (role is not null && (role.Guild.Id != command.GuildId || role.Id == command.GuildId))
        {
            return new HumanReadableError("Select a role from this Discord server other than @everyone.");
        }

        bool ephemeral = IsEphemeral(command.Data.Name, subcommand.Name);
        return RightAsync<IError, IInteractionResponse>(
            new GameInteractionResponse(ephemeral, () => RunOnServer(command, subcommand.Name, company, role), discord, voteReactions));
    }

    internal static bool IsEphemeral(string commandName, string action)
        => commandName == "game-admin" || action is "link" or "reset-password";

    private async Task<GameReply> RunOnServer(ISlashCommandInteraction command, string action, int? company, IRole? role)
    {
        bool adminCommand = command.Data.Name == "game-admin";
        if (adminCommand)
        {
            var level = await roleLevels.Execute(command.User);
            bool allowed = level.Match(value => value == UserLevel.Admin, _ => false);
            if (!allowed)
            {
                return new GameReply("A configured game-admin role or Discord Administrator permission is required.");
            }

            if (action == "admin-role")
            {
                if (role is null)
                {
                    return new GameReply("Select a Discord role to administer the game.");
                }

                var registered = await registerRole.Execute(command.GuildId!.Value, role, UserLevel.Admin);
                return registered.Match(
                    _ => new GameReply($"{role.Mention} can now use `/game-admin` commands."),
                    error => new GameReply(error is HumanReadableError ? error.Reason : "Could not save the game-admin role."));
            }
        }

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

        action = NormalizeAction(command.Data.Name, action);

        var request = new GameRequest(server.Id, server.GuildId, command.User.Id, command.ChannelId!.Value, action, company);
        try
        {
            var selected = await akka.SelectActor(MainActors.Paths.Guilds);
            var reply = await selected.Right().Ask<GameReply>(request, TimeSpan.FromSeconds(15));
            return reply.OpenedVote.HasValue
                ? reply with
                {
                    Text = $"**{Format.Sanitize(server.Name)}** · {reply.Text}",
                    PingRoleId = server.UnpausePingRoleId,
                    VoteServerId = server.Id,
                }
                : reply;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "OpenTTD game command {Action} failed for server {ServerId}", action, server.Id);
            return new GameReply("The bot could not reach the registered OpenTTD server. Try again after it reconnects.");
        }
    }

    internal static string NormalizeAction(string commandName, string action)
        => commandName == "game-admin" && action is "pause" or "unpause" or "softpause" or "pausestatus" ? "admin-" + action : action;
}
