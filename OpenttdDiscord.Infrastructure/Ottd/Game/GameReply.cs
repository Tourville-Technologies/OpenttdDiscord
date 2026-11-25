using Discord;

namespace OpenttdDiscord.Infrastructure.Ottd.Game;

internal sealed record GameReply(string Text)
{
    public Embed? Status { get; init; }

    public IReadOnlyList<CompanyProfit>? Profits { get; init; }

    public int ProfitYear { get; init; }

    public int ProfitMonth { get; init; }

    public Guid? OpenedVote { get; init; }

    public Guid? VoteServerId { get; init; }

    public ulong? PingRoleId { get; init; }
}
