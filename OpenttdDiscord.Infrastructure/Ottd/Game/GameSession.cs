using System.Security.Cryptography;
using OpenTTDAdminPort;
using OpenTTDAdminPort.Events;
using OpenTTDAdminPort.Game;
using OpenTTDAdminPort.MainActor.StateData;
using OpenttdDiscord.Infrastructure.Statuses;

namespace OpenttdDiscord.Infrastructure.Ottd.Game;

internal sealed class GameSession(IAdminPortClient client)
{
    public const string LinkPrefix = "!discord-link ";
    private const int MinimumVoteCount = 4;
    public static readonly TimeSpan VoteDuration = TimeSpan.FromMinutes(5);

    private readonly GameConsole console = new(client);
    private readonly Dictionary<string, (ulong User, DateTimeOffset Expires)> linkCodes = new(StringComparer.Ordinal);
    private readonly Dictionary<uint, ulong> linkedClients = new();
    private readonly Dictionary<ulong, bool> votes = new();

    private DateTimeOffset voteEnds;
    private Guid voteId = Guid.Empty;
    private ulong voteGeneration;

    public void Clear()
    {
        linkedClients.Clear();
        linkCodes.Clear();
        ClearVote();
    }

    public void Disconnect(uint clientId)
    {
        linkedClients.Remove(clientId);
    }

    public bool ConsumeLink(AdminChatMessageEvent message)
    {
        if (!message.Message.StartsWith(LinkPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        if (message.Player.ClientId != 1 &&
            IsPublicChat(message) &&
            linkCodes.Remove(message.Message[LinkPrefix.Length..].Trim(), out var link) &&
            link.Expires > DateTimeOffset.UtcNow)
        {
            linkedClients[message.Player.ClientId] = link.User;
        }

        return true;
    }

    public async Task<GameReply> Execute(GameRequest request)
    {
        if (await client.GetMainData() is not ConnectedData)
        {
            return new GameReply("The OpenTTD admin port is offline or still connecting.");
        }

        if (request.Action == "clear-password" && request.Company is >= 1 and <= 254)
        {
            bool cleared = await console.ClearPassword(request.Company.Value);
            return new GameReply(cleared
                ? $"Password cleared for company #{request.Company}."
                : $"Company #{request.Company} must be an existing human company.");
        }

        if (request.Action == "reset-password")
        {
            return await ResetLinkedCompanyPassword(request);
        }

        if (request.Action == "status")
        {
            return await Status();
        }

        var snapshot = await console.Snapshot(includeCompanies: request.Action is "profit" or "my-profit");
        switch (request.Action)
        {
            case "link":
            {
                return CreateLink(request.UserId);
            }

            case "unpause":
            {
                return OpenVote(snapshot.State, snapshot.Clients, snapshot.Generation);
            }

            case "profit":
            {
                return request.Company is { } companyId
                    ? Profits(snapshot.Companies.Where(company => company.Id == companyId).ToArray(), snapshot.Year, snapshot.Month)
                    : Profits(snapshot.Companies, snapshot.Year, snapshot.Month);
            }

            case "my-profit":
            {
                var players = await GetLinkedCompanyPlayers(request.UserId);
                var companies = players.Select(player => player.PlayingAs + 1).ToHashSet();
                return companies.Count == 0
                    ? new GameReply("Use `/game link` to link your current OpenTTD session, then join a company.")
                    : Profits(snapshot.Companies.Where(company => companies.Contains(company.Id)).ToArray(), snapshot.Year, snapshot.Month);
            }

            default:
            {
                return new GameReply("Unknown game action.");
            }
        }
    }

    private async Task<GameReply> ResetLinkedCompanyPassword(GameRequest request)
    {
        var players = await GetLinkedCompanyPlayers(request.UserId);
        if (players.Length == 0)
        {
            return new GameReply("Use `/game link` to link your current OpenTTD session, then join a company.");
        }

        var companies = players.Select(player => player.PlayingAs + 1).Distinct().Order().ToArray();
        if (request.Company is null && companies.Length > 1)
        {
            string choices = string.Join(", ", companies.Select(company => $"#{company}"));
            return new GameReply($"Choose a company number with the `company` option. Your linked clients are in {choices}.");
        }

        int companyId = request.Company ?? companies[0];
        var member = players.FirstOrDefault(player => player.PlayingAs + 1 == companyId);
        if (member is null)
        {
            return new GameReply($"A linked OpenTTD client must be playing in company #{companyId}.");
        }

        string result = await console.ClearLinkedCompanyPassword(companyId, member.ClientId);
        return result switch
        {
            "cleared" => new GameReply($"Password cleared for company #{companyId}. You can set a new one in OpenTTD."),
            "not_in_company" => new GameReply($"A linked OpenTTD client must still be playing in company #{companyId}."),
            _ => new GameReply($"Company #{companyId} must be an existing human company."),
        };
    }

    private async Task<Player[]> GetLinkedCompanyPlayers(ulong userId)
    {
        var status = await client.QueryServerStatus();
        return status.Players.Values
            .Where(player => player.PlayingAs < 253 && linkedClients.TryGetValue(player.ClientId, out ulong user) && user == userId)
            .ToArray();
    }

    private static bool IsPublicChat(AdminChatMessageEvent message)
        => message.ChatDestination == ChatDestination.DESTTYPE_BROADCAST &&
            message.NetworkAction is NetworkAction.NETWORK_ACTION_CHAT or NetworkAction.NETWORK_ACTION_SERVER_MESSAGE;

    private async Task<GameReply> Status()
    {
        var status = await client.QueryServerStatus();
        var embed = new ServerStatusEmbedBuilder().CreateServerStatusEmbed(status, status.AdminServerInfo);
        try
        {
            var snapshot = await console.Snapshot(includeCompanies: false);
            string state = snapshot.State switch
            {
                "vote" => "Paused until an unpause vote passes",
                "paused" => "Paused",
                _ => "Running",
            };
            return new GameReply($"{state} · {snapshot.Clients} connected") { Status = embed };
        }
        catch (TimeoutException)
        {
            return new GameReply("Connected. Live pause state requires the companion OpenTTD patch.") { Status = embed };
        }
    }

    private GameReply CreateLink(ulong user)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        foreach (string expired in linkCodes.Where(pair => pair.Value.Expires <= now || pair.Value.User == user)
                     .Select(pair => pair.Key).ToArray())
        {
            linkCodes.Remove(expired);
        }

        string code = Convert.ToHexString(RandomNumberGenerator.GetBytes(12));
        linkCodes.Add(code, (user, now.AddMinutes(5)));
        return new GameReply($"Enter `{LinkPrefix}{code}` in OpenTTD's **All** chat within five minutes. The server delivers the code privately to the bot.");
    }

    public async Task<GameReply?> FinishVote(Guid id)
    {
        if (id != voteId)
        {
            return null;
        }

        int yes = votes.Values.Count(vote => vote);
        int no = votes.Count - yes;
        voteId = Guid.Empty;
        votes.Clear();

        if (yes + no == 0)
        {
            return new GameReply("Unpause vote did not pass because no votes were cast.");
        }

        if (yes + no < MinimumVoteCount)
        {
            return new GameReply($"Unpause vote did not pass ({yes} yes / {no} no). At least {MinimumVoteCount} votes are required.");
        }

        if (yes < no)
        {
            return new GameReply($"Unpause vote did not pass ({yes} yes / {no} no).");
        }

        var snapshot = await console.Snapshot(includeCompanies: false);
        if (snapshot.Generation != voteGeneration || snapshot.Clients == 0 || snapshot.State != "vote")
        {
            return new GameReply("The game state changed during the vote. Open a new vote when players are connected.");
        }

        string result = await console.Resume(voteGeneration);
        return result switch
        {
            "resumed" => new GameReply($"Vote passed ({yes} yes / {no} no). OpenTTD is resuming."),
            "empty" => new GameReply("The players left before OpenTTD could resume."),
            "blocked" => new GameReply("OpenTTD is paused for another reason. Resolve it before starting another vote."),
            _ => new GameReply("OpenTTD is no longer waiting for this vote."),
        };
    }

    public void RecordVote(Guid id, ulong userId, bool unpause)
    {
        if (id == voteId && DateTimeOffset.UtcNow < voteEnds)
        {
            votes[userId] = unpause;
        }
    }

    private GameReply OpenVote(string state, int clients, ulong generation)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        if (state != "vote" || clients == 0 || generation != voteGeneration)
        {
            ClearVote();
        }

        if (state != "vote")
        {
            return new GameReply("The game is not waiting for a Discord unpause vote.");
        }

        if (clients == 0)
        {
            return new GameReply("Join the paused game before opening an unpause vote.");
        }

        if (voteId != Guid.Empty && now >= voteEnds)
        {
            return new GameReply("Voting has closed. The bot is counting the votes.");
        }

        if (voteId != Guid.Empty)
        {
            return new GameReply($"An unpause vote is already open and closes <t:{voteEnds.ToUnixTimeSeconds()}:R>.");
        }

        voteEnds = now + VoteDuration;
        voteGeneration = generation;
        voteId = Guid.NewGuid();
        votes.Clear();
        return new GameReply(
            $"Unpause vote closes <t:{voteEnds.ToUnixTimeSeconds()}:R>. React with ✅ to unpause or ❌ to keep the game paused. At least {MinimumVoteCount} votes are required, and at least 50% must be ✅.") { OpenedVote = voteId };
    }

    private void ClearVote()
    {
        voteId = Guid.Empty;
        votes.Clear();
    }

    private static GameReply Profits(IReadOnlyList<CompanyProfit> companies, int year, int month)
    {
        return companies.Count == 0
            ? new GameReply("No matching companies are available.")
            : new GameReply("Operating profit by completed quarter, in OpenTTD base currency.")
            {
                Profits = companies,
                ProfitYear = year,
                ProfitMonth = month,
            };
    }
}
