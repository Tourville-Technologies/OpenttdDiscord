namespace OpenttdDiscord.Infrastructure.Ottd.Game;

// OpenTTD returns the most recently completed quarter first.
internal sealed record CompanyProfit(int Id, string Name, IReadOnlyList<long> Quarters, uint Colour = 0);
