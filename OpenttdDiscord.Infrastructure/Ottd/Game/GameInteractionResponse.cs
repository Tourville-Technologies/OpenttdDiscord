using Discord;
using OpenttdDiscord.Infrastructure.Discord.CommandResponses;

namespace OpenttdDiscord.Infrastructure.Ottd.Game;

internal sealed class GameInteractionResponse(
    bool ephemeral,
    Func<Task<GameReply>> getReply,
    IDiscordClient discord,
    IGameVoteReactions voteReactions) : InteractionResponseBase
{
    protected override async Task InternalExecute(IDiscordInteraction interaction)
    {
        await interaction.DeferAsync(ephemeral: ephemeral);
        var reply = await getReply();
        if (reply.OpenedVote is { } voteId && reply.VoteServerId is { } serverId && interaction.GuildId is { } guildId &&
            interaction.ChannelId is { } voteChannelId && await discord.GetChannelAsync(voteChannelId) is IMessageChannel voteChannel)
        {
            string text = reply.PingRoleId is { } pingRoleId ? $"{MentionUtils.MentionRole(pingRoleId)} {reply.Text}" : reply.Text;
            var mentions = new AllowedMentions(AllowedMentionTypes.None);
            if (reply.PingRoleId is { } allowedRoleId)
            {
                mentions.RoleIds = new List<ulong> { allowedRoleId };
            }

            IUserMessage message = await voteChannel.SendMessageAsync(text, allowedMentions: mentions);
            await voteReactions.Register(message, serverId, guildId, voteId);
            await interaction.DeleteOriginalResponseAsync();
            return;
        }

        if (reply.PingRoleId is { } roleId && interaction.ChannelId is { } channelId &&
            await discord.GetChannelAsync(channelId) is IMessageChannel channel)
        {
            // Send a new channel message so Discord can notify the role after the deferred response.
            var mentions = new AllowedMentions(AllowedMentionTypes.None) { RoleIds = new List<ulong> { roleId } };
            await channel.SendMessageAsync($"{MentionUtils.MentionRole(roleId)} {reply.Text}", allowedMentions: mentions);
            await interaction.DeleteOriginalResponseAsync();
            return;
        }

        if (reply.Profits is null)
        {
            await interaction.FollowupAsync(reply.Text, embed: reply.Status, ephemeral: ephemeral, allowedMentions: AllowedMentions.None);
            return;
        }

        using var chart = ProfitChart.Render(reply.Profits, reply.ProfitYear, reply.ProfitMonth);
        await interaction.FollowupWithFileAsync(chart, "operating-profit.png", reply.Text, ephemeral: ephemeral, allowedMentions: AllowedMentions.None);
    }
}
