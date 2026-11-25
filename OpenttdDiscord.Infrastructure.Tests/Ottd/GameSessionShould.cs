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
    private ulong generation = 1;
    private int resumes;
    private string resetResult = "cleared";

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

        var result = await session.FinishVote(open.OpenedVote!.Value);

        Assert.Contains("did not pass", result!.Text);
        Assert.Equal(0, resumes);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task ResumeWhenAtLeastHalfOfVotesAreYes(int noVotes)
    {
        var open = await session.Execute(Vote(1));
        session.RecordVote(open.OpenedVote!.Value, 1, true);
        for (int index = 0; index < noVotes; index++)
        {
            session.RecordVote(open.OpenedVote.Value, (ulong)index + 2, false);
        }

        Assert.Equal(0, resumes);
        var result = await session.FinishVote(open.OpenedVote!.Value);

        Assert.Contains("Vote passed", result!.Text);
        Assert.Equal(1, resumes);
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
    }

    [Fact]
    public async Task RejectAnApprovedVoteFromBeforeTheServerEmptied()
    {
        var open = await session.Execute(Vote(1));
        session.RecordVote(open.OpenedVote!.Value, 1, true);
        session.RecordVote(open.OpenedVote.Value, 2, true);
        generation++;

        var result = await session.FinishVote(open.OpenedVote!.Value);

        Assert.Contains("game state changed", result!.Text);
        Assert.Equal(0, resumes);
    }

    [Fact]
    public async Task ShowOnlyTheLinkedClientsCurrentCompany()
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
        Assert.Null(afterDisconnect.Profits);
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
        Assert.EndsWith(" 3 42", Assert.Single(passwordRequests));
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
        Assert.Contains("must be playing in company #4", wrongCompany.Text);
        Assert.Empty(passwordRequests);
    }

    [Fact]
    public async Task ReportACompanyChangeDetectedByTheGameServer()
    {
        var player = new Player(42, "Player", "localhost", 2, DateTimeOffset.UtcNow);
        client.QueryServerStatus().Returns(new ServerStatus(null!, new Dictionary<uint, Player> { [player.ClientId] = player }));
        var user = Vote(1);
        await LinkPlayer(user, player);
        resetResult = "not_in_company";

        var result = await session.Execute(user with { Action = "reset-password" });

        Assert.Contains("must still be playing in company #3", result.Text);
        Assert.EndsWith(" 3 42", Assert.Single(passwordRequests));
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
            var result = new AdminRconEvent(prefix + resetResult);
            Assert.True(match(result));
            return Task.FromResult<IAdminEvent>(result);
        }

        Assert.False(match(new AdminRconEvent("OTTD_DISCORD unrelated end")));
        Assert.False(match(new AdminRconEvent(prefix + $"state vote 1 {generation}")));
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
