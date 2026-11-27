using Discord;
using OpenttdDiscord.Infrastructure.Ottd.Game;

namespace OpenttdDiscord.Infrastructure.Tests.Ottd;

public class GameInteractionResponseShould
{
    [Fact]
    public void PublishAdministratorPauseCommands()
    {
        var command = new GameCommand(admin: true).Build();
        var optionNames = command.Options.Value.Select(option => option.Name);

        Assert.Contains("pause", optionNames);
        Assert.Contains("unpause", optionNames);
        Assert.Contains("softpause", optionNames);
        Assert.Contains("pausestatus", optionNames);
    }

    [Fact]
    public void PublishGameDateCommand()
    {
        var command = new GameCommand(admin: false).Build();

        Assert.Contains("get-date", command.Options.Value.Select(option => option.Name));
    }

    [Theory]
    [InlineData("game", "status", false)]
    [InlineData("game", "profit", false)]
    [InlineData("game", "my-profit", false)]
    [InlineData("game", "unpause", false)]
    [InlineData("game", "link", true)]
    [InlineData("game", "reset-password", true)]
    [InlineData("game-admin", "start", true)]
    public void ShowOnlySensitiveCommandResponsesToTheRunningUser(string command, string action, bool expected)
    {
        Assert.Equal(expected, GameCommandRunner.IsEphemeral(command, action));
    }

    [Theory]
    [InlineData("game", "unpause", "unpause")]
    [InlineData("game-admin", "unpause", "admin-unpause")]
    [InlineData("game-admin", "pause", "admin-pause")]
    [InlineData("game-admin", "softpause", "admin-softpause")]
    [InlineData("game-admin", "pausestatus", "admin-pausestatus")]
    [InlineData("game-admin", "restart", "restart")]
    public void OnlyRouteAdministratorPauseCommandsAroundVoting(string command, string action, string expected)
    {
        Assert.Equal(expected, GameCommandRunner.NormalizeAction(command, action));
    }

    [Fact]
    public async Task SendVoteAnnouncementWithOnlyTheConfiguredRoleAllowed()
    {
        const ulong roleId = 42;
        var discord = Substitute.For<IDiscordClient>();
        var interaction = Substitute.For<IDiscordInteraction>();
        var channel = Substitute.For<IMessageChannel>();
        var message = Substitute.For<IUserMessage>();
        var reactions = Substitute.For<IGameVoteReactions>();
        interaction.ChannelId.Returns(17UL);
        interaction.GuildId.Returns(18UL);
        discord.GetChannelAsync(17).Returns(channel);
        channel.SendMessageAsync(Arg.Any<string>(), false, null, null, Arg.Any<AllowedMentions>()).Returns(message);
        var voteId = Guid.NewGuid();
        var serverId = Guid.NewGuid();
        var reply = new GameReply("Vote opened.")
        {
            PingRoleId = roleId,
            OpenedVote = voteId,
            VoteServerId = serverId,
        };
        var response = new GameInteractionResponse(false, () => Task.FromResult(reply), discord, reactions);

        var result = await response.Execute(interaction);

        Assert.True(result.IsRight);
        await channel.Received(1).SendMessageAsync(
            "<@&42> Vote opened.",
            allowedMentions: Arg.Is<AllowedMentions>(mentions =>
                mentions.RoleIds != null && mentions.RoleIds.SequenceEqual(new[] { roleId }) &&
                (mentions.UserIds == null || mentions.UserIds.Count == 0) && mentions.AllowedTypes == AllowedMentionTypes.None));
        await interaction.Received(1).DeleteOriginalResponseAsync();
        await reactions.Received(1).Register(message, serverId, 18, voteId);
    }
}
