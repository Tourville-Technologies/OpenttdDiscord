using Discord;
using OpenttdDiscord.Infrastructure.Discord.CommandResponses;

namespace OpenttdDiscord.Infrastructure.Ottd.Game;

internal sealed class GameInteractionResponse(bool ephemeral, Func<Task<GameReply>> getReply, IDiscordClient discord) : InteractionResponseBase
{
    protected override async Task InternalExecute(IDiscordInteraction interaction)
    {
        await interaction.DeferAsync(ephemeral: ephemeral);
        var reply = await getReply();
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

        using var chart = ProfitChart.Render(reply.Profits);
        await interaction.FollowupWithFileAsync(chart, "operating-profit.svg", reply.Text, ephemeral: ephemeral, allowedMentions: AllowedMentions.None);
    }
}
