using Akka.Actor;
using OpenTTDAdminPort;
using OpenTTDAdminPort.Events;
using OpenTTDAdminPort.Game;
using OpenTTDAdminPort.MainActor.Messages;
using OpenTTDAdminPort.MainActor.StateData;
using OpenTTDAdminPort.Messages;
using OpenttdDiscord.Infrastructure.Ottd.Game;

namespace OpenttdDiscord.Infrastructure.Tests.Ottd;

public class GameSessionShould
{
    private readonly IAdminPortClient client = Substitute.For<IAdminPortClient>();
    private readonly GameSession session;
    private readonly List<string> passwordRequests = new();
    private readonly List<string> pauseRequests = new();
    private ulong generation = 1;
    private int resumes;

    public GameSessionShould()
    {
        var info = new ServerInfo("localhost", 3977, "test");
        var state = new ConnectedData(ActorRefs.Nobody, info, "test", ActorRefs.Nobody, new Dictionary<AdminUpdateType, AdminUpdateSetting>(),
            null!, 1, new Dictionary<uint, Player>(), new Dictionary<byte, Company>(), new Dictionary<uint, Player>());
        client.GetMainData().Returns(Task.FromResult<IMainData>(state));
        client.WaitForEvent(Arg.Any<IAdminMessage>(), Arg.Any<Func<IAdminEvent, bool>>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(call => HandleRcon(call.Arg<IAdminMessage>(), call.Arg<Func<IAdminEvent, bool>>()));
        session = new GameSession(client);
    }

    [Fact]
    public async Task CountOnlyOneBallotPerDiscordUser()
    {
        long startedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var open = await session.Execute(Vote(1));
        long earliestClose = startedAt + 300;
        long latestClose = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 300;
        int timestampStart = open.Text.IndexOf("<t:", StringComparison.Ordinal) + 3;
        long closingTime = long.Parse(open.Text[timestampStart..].Split(':')[0], System.Globalization.CultureInfo.InvariantCulture);
        Assert.InRange(closingTime, earliestClose, latestClose);

        session.RecordVote(open.OpenedVote!.Value, 1, true);
        var updated = await session.Execute(Vote(1));
        Assert.Null(updated.OpenedVote);
        session.RecordVote(open.OpenedVote.Value, 1, false);
        session.RecordVote(open.OpenedVote.Value, 2, false);
        session.RecordVote(open.OpenedVote.Value, 3, true);
        session.RecordVote(open.OpenedVote.Value, 4, false);

        var result = await session.FinishVote(open.OpenedVote!.Value);

        Assert.Contains("did not pass", result!.Text);
        Assert.Equal(0, resumes);
    }

    [Theory]
    [InlineData(4, 0)]
    [InlineData(3, 1)]
    [InlineData(2, 2)]
    public async Task ResumeWhenAtLeastFourVoteAndAtLeastHalfAreYes(int yesVotes, int noVotes)
    {
        var open = await session.Execute(Vote(1));
        ulong userId = 1;
        for (int index = 0; index < yesVotes; index++)
        {
            session.RecordVote(open.OpenedVote!.Value, userId++, true);
        }

        for (int index = 0; index < noVotes; index++)
        {
            session.RecordVote(open.OpenedVote!.Value, userId++, false);
        }

        Assert.Equal(0, resumes);
        var result = await session.FinishVote(open.OpenedVote!.Value);

        Assert.Contains("Vote passed", result!.Text);
        Assert.Equal(1, resumes);
    }

    [Fact]
    public async Task KeepTheGamePausedWhenFewerThanFourUsersVote()
    {
        var open = await session.Execute(Vote(1));
        session.RecordVote(open.OpenedVote!.Value, 1, true);
        session.RecordVote(open.OpenedVote.Value, 2, true);
        session.RecordVote(open.OpenedVote.Value, 3, true);

        var result = await session.FinishVote(open.OpenedVote.Value);

        Assert.Contains("At least 4 votes are required", result!.Text);
        Assert.Equal(0, resumes);
    }

    [Fact]
    public async Task KeepTheGamePausedWhenNobodyVotes()
    {
        var open = await session.Execute(Vote(1));

        var result = await session.FinishVote(open.OpenedVote!.Value);

        Assert.Contains("no votes were cast", result!.Text);
        Assert.Equal(0, resumes);
    }

    [Fact]
    public async Task ShowAllCompaniesByDefaultOrFilterByCompanyId()
    {
        var request = Vote(1) with { Action = "profit" };
        var all = await session.Execute(request);
        var selected = await session.Execute(request with { Company = 3 });
        var missing = await session.Execute(request with { Company = 7 });

        Assert.Equal(new[] { 3, 4 }, all.Profits!.Select(company => company.Id));
        var company = Assert.Single(selected.Profits!);
        Assert.Equal(3, company.Id);
        Assert.Equal(new long[] { 100, -45 }, company.Quarters);
        Assert.Null(missing.Profits);
        Assert.Contains("No matching companies", missing.Text);
        Assert.Contains("2026-11-26", all.Text);
        Assert.Contains("Q4, month 2 of 3, day 26", all.Text);
        Assert.Contains("36 in-game days until Q1", all.Text);
    }

    [Fact]
    public async Task ShowTheCurrentGameDateAndQuarterProgress()
    {
        var result = await session.Execute(Vote(1) with { Action = "get-date" });

        Assert.Contains("2026-11-26", result.Text);
        Assert.Contains("Q4, month 2 of 3, day 26", result.Text);
        Assert.Contains("36 in-game days until Q1", result.Text);
    }

    [Fact]
    public async Task RejectAnApprovedVoteFromBeforeTheServerEmptied()
    {
        var open = await session.Execute(Vote(1));
        session.RecordVote(open.OpenedVote!.Value, 1, true);
        session.RecordVote(open.OpenedVote.Value, 2, true);
        session.RecordVote(open.OpenedVote.Value, 3, true);
        session.RecordVote(open.OpenedVote.Value, 4, true);
        generation++;

        var result = await session.FinishVote(open.OpenedVote!.Value);

        Assert.Contains("game state changed", result!.Text);
        Assert.Equal(0, resumes);
    }

    [Fact]
    public async Task RetainTheLinkedCompanyWhenTheClientDisconnects()
    {
        var player = new Player(42, "Player", "localhost", 2, DateTimeOffset.UtcNow);
        var players = new Dictionary<uint, Player> { [player.ClientId] = player };
        client.QueryServerStatus().Returns(new ServerStatus(null!, players));
        var user = Vote(1);
        await LinkPlayer(user, player);

        var result = await session.Execute(user with { Action = "my-profit" });
        var company = Assert.Single(result.Profits!);
        Assert.Equal(3, company.Id);
        Assert.Equal(new long[] { 100, -45 }, company.Quarters);

        session.Disconnect(player.ClientId);
        var afterDisconnect = await session.Execute(user with { Action = "my-profit" });
        var retained = Assert.Single(afterDisconnect.Profits!);
        Assert.Equal(3, retained.Id);
    }

    [Fact]
    public async Task ClearThePasswordForTheLinkedClientsCompany()
    {
        var player = new Player(42, "Player", "localhost", 2, DateTimeOffset.UtcNow);
        client.QueryServerStatus().Returns(new ServerStatus(null!, new Dictionary<uint, Player> { [player.ClientId] = player }));
        var user = Vote(1);
        await LinkPlayer(user, player);

        var result = await session.Execute(user with { Action = "reset-password" });

        Assert.Contains("Password cleared for company #3", result.Text);
        Assert.EndsWith(" 3", Assert.Single(passwordRequests));
    }

    [Fact]
    public async Task RejectPasswordResetsFromUnlinkedUsersOrForOtherCompanies()
    {
        var player = new Player(42, "Player", "localhost", 2, DateTimeOffset.UtcNow);
        client.QueryServerStatus().Returns(new ServerStatus(null!, new Dictionary<uint, Player> { [player.ClientId] = player }));
        var user = Vote(1);
        await LinkPlayer(user, player);

        var stranger = await session.Execute(user with { UserId = 2, Action = "reset-password", Company = 3 });
        var wrongCompany = await session.Execute(user with { Action = "reset-password", Company = 4 });

        Assert.Contains("/game link", stranger.Text);
        Assert.Contains("not linked to company #4", wrongCompany.Text);
        Assert.Empty(passwordRequests);
    }

    [Fact]
    public async Task RetainCompaniesJoinedAfterLinking()
    {
        var player = new Player(42, "Player", "localhost", 2, DateTimeOffset.UtcNow);
        var user = Vote(1);
        await LinkPlayer(user, player);
        session.UpdateCompany(player with { PlayingAs = 3 });
        session.Disconnect(player.ClientId);

        var result = await session.Execute(user with { Action = "my-profit" });

        Assert.Equal(new[] { 3, 4 }, result.Profits!.Select(company => company.Id));
    }

    [Fact]
    public async Task LetAdministratorsPauseAndUnpauseWithoutAVote()
    {
        var request = Vote(1);

        var paused = await session.Execute(request with { Action = "admin-pause" });
        var resumed = await session.Execute(request with { Action = "admin-unpause" });

        Assert.Contains("paused by an administrator", paused.Text);
        Assert.Contains("unpaused by an administrator", resumed.Text);
        Assert.Equal(new[] { "discord_force_pause", "discord_force_resume" }, pauseRequests);
    }

    [Fact]
    public async Task LetAdministratorsRestoreAutomaticPopulationPausing()
    {
        var result = await session.Execute(Vote(1) with { Action = "admin-softpause" });

        Assert.Contains("Automatic population-based pausing has been restored", result.Text);
        Assert.Equal(new[] { "discord_soft_pause" }, pauseRequests);
    }

    [Fact]
    public async Task ShowAdministratorsThePauseSystemStatus()
    {
        var result = await session.Execute(Vote(1) with { Action = "admin-pausestatus" });

        Assert.Contains("Pause system status", result.Text);
        Assert.Contains("Vote lock: off", result.Text);
        Assert.Contains("OpenTTD pause locks: none", result.Text);
        Assert.Equal(new[] { "discord_pause_status" }, pauseRequests);
    }

    private async Task LinkPlayer(GameRequest user, Player player)
    {
        var link = await session.Execute(user with { Action = "link" });
        int codeStart = link.Text.IndexOf(GameSession.LinkPrefix, StringComparison.Ordinal);
        string code = link.Text[codeStart..].Split('`')[0];

        Assert.True(session.ConsumeLink(new AdminChatMessageEvent(
            player, ChatDestination.DESTTYPE_BROADCAST, NetworkAction.NETWORK_ACTION_SERVER_MESSAGE, code)));
    }

    private static GameRequest Vote(ulong userId)
        => new(Guid.Empty, 7, userId, 8, "unpause", null);

    private Task<IAdminEvent> HandleRcon(IAdminMessage message, Func<IAdminEvent, bool> match)
    {
        string[] command = ((AdminRconMessage)message).Command.Split(' ');
        string prefix = $"OTTD_DISCORD {command[1]} ";
        if (command[0] == "discord_resume")
        {
            resumes++;
            var resumed = new AdminRconEvent(prefix + "resumed");
            Assert.True(match(resumed));
            return Task.FromResult<IAdminEvent>(resumed);
        }

        if (command[0] == "discord_clear_password")
        {
            passwordRequests.Add(((AdminRconMessage)message).Command);
            var result = new AdminRconEvent(prefix + "cleared");
            Assert.True(match(result));
            return Task.FromResult<IAdminEvent>(result);
        }

        if (command[0] is "discord_force_pause" or "discord_force_resume" or "discord_soft_pause" or "discord_pause_status")
        {
            pauseRequests.Add(command[0]);
            string response = command[0] switch
            {
                "discord_force_pause" => "paused",
                "discord_force_resume" => "resumed",
                "discord_soft_pause" => "normal",
                _ => "State: running|Active company players: 3|Vote lock: off|Administrative unpause override: off|Low-population control: inactive (3+ active players)|OpenTTD pause locks: none",
            };
            var result = new AdminRconEvent(prefix + response);
            Assert.True(match(result));
            return Task.FromResult<IAdminEvent>(result);
        }

        Assert.False(match(new AdminRconEvent("OTTD_DISCORD unrelated end")));
        Assert.False(match(new AdminRconEvent(prefix + $"state vote 1 {generation} 2026 10 26 36")));
        if (command.Length == 2)
        {
            Assert.False(match(new AdminRconEvent(prefix + "company 3|100,-45|Rail & Road")));
            Assert.False(match(new AdminRconEvent(prefix + "company 4||New Company")));
        }

        var end = new AdminRconEvent(prefix + "end");
        Assert.True(match(end));
        return Task.FromResult<IAdminEvent>(end);
    }
}
