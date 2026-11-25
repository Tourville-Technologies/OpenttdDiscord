using System.Collections.Concurrent;
using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Logging;
using OpenttdDiscord.Infrastructure.Akkas;

namespace OpenttdDiscord.Infrastructure.Ottd.Game;

public interface IGameVoteReactions
{
    Task Register(IUserMessage message, Guid serverId, ulong guildId, Guid voteId);

    void Close(Guid voteId);
}

internal sealed class GameVoteReactions : IGameVoteReactions
{
    public static readonly Emoji Yes = new("✅");
    public static readonly Emoji No = new("❌");

    private readonly ConcurrentDictionary<ulong, VoteReference> votes = new();
    private readonly DiscordSocketClient discord;
    private readonly IAkkaService akka;
    private readonly ILogger<GameVoteReactions> logger;

    public GameVoteReactions(DiscordSocketClient discord, IAkkaService akka, ILogger<GameVoteReactions> logger)
    {
        this.discord = discord;
        this.akka = akka;
        this.logger = logger;
        discord.ReactionAdded += ReactionAdded;
    }

    public async Task Register(IUserMessage message, Guid serverId, ulong guildId, Guid voteId)
    {
        votes[message.Id] = new VoteReference(serverId, guildId, voteId);
        await message.AddReactionAsync(Yes);
        await message.AddReactionAsync(No);
    }

    public void Close(Guid voteId)
    {
        foreach (var vote in votes.Where(pair => pair.Value.VoteId == voteId).ToArray())
        {
            votes.TryRemove(vote.Key, out _);
        }
    }

    private async Task ReactionAdded(
        Cacheable<IUserMessage, ulong> message,
        Cacheable<IMessageChannel, ulong> channel,
        SocketReaction reaction)
    {
        if (reaction.UserId == discord.CurrentUser.Id ||
            (reaction.User.IsSpecified && reaction.User.Value.IsBot) ||
            !votes.TryGetValue(message.Id, out var vote))
        {
            return;
        }

        bool? unpause = reaction.Emote.Name switch
        {
            "✅" => true,
            "❌" => false,
            _ => null,
        };
        if (unpause is null)
        {
            return;
        }

        var selection = await akka.SelectActor(MainActors.Paths.Guilds);
        selection.IfRight(actor => actor.Tell(new GameVoteReaction(
            vote.ServerId,
            vote.GuildId,
            vote.VoteId,
            reaction.UserId,
            unpause.Value)));
        selection.IfLeft(error => logger.LogWarning("Could not record Discord unpause vote: {Error}", error));
    }

    private sealed record VoteReference(Guid ServerId, ulong GuildId, Guid VoteId);
}
