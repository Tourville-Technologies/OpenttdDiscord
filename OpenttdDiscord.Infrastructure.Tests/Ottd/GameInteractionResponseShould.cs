using Discord;
using OpenttdDiscord.Infrastructure.Ottd.Game;

namespace OpenttdDiscord.Infrastructure.Tests.Ottd;

public class GameInteractionResponseShould
{
    [Fact]
    public async Task SendVoteAnnouncementWithOnlyTheConfiguredRoleAllowed()
    {
        const ulong roleId = 42;
        var discord = Substitute.For<IDiscordClient>();
        var interaction = Substitute.For<IDiscordInteraction>();
        var channel = Substitute.For<IMessageChannel>();
        interaction.ChannelId.Returns(17UL);
        discord.GetChannelAsync(17).Returns(channel);
        var reply = new GameReply("Vote opened.") { PingRoleId = roleId };
        var response = new GameInteractionResponse(false, () => Task.FromResult(reply), discord);

        var result = await response.Execute(interaction);

        Assert.True(result.IsRight);
        await channel.Received(1).SendMessageAsync(
            "<@&42> Vote opened.",
            allowedMentions: Arg.Is<AllowedMentions>(mentions =>
                mentions.RoleIds != null && mentions.RoleIds.SequenceEqual(new[] { roleId }) &&
                (mentions.UserIds == null || mentions.UserIds.Count == 0) && mentions.AllowedTypes == AllowedMentionTypes.None));
        await interaction.Received(1).DeleteOriginalResponseAsync();
    }
}
