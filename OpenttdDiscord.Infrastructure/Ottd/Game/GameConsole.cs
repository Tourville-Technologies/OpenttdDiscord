using System.Globalization;
using OpenTTDAdminPort;
using OpenTTDAdminPort.Events;
using OpenTTDAdminPort.Messages;

namespace OpenttdDiscord.Infrastructure.Ottd.Game;

internal sealed class GameConsole(IAdminPortClient client)
{
    public const string ReplyPrefix = "OTTD_DISCORD ";

    public async Task<(string State, int Clients, ulong Generation, IReadOnlyList<CompanyProfit> Companies)> Snapshot(bool includeCompanies)
    {
        var lines = await Run("discord_snapshot", includeCompanies ? string.Empty : " state", "end");
        string[] state = lines[0].Split(' ');
        int players = int.Parse(state[2], CultureInfo.InvariantCulture);
        ulong generation = ulong.Parse(state[3], CultureInfo.InvariantCulture);
        var companies = lines.Skip(1).SkipLast(1).Select(ParseCompany).ToArray();
        return (state[1], players, generation, companies);
    }

    public async Task<string> Resume(ulong generation)
    {
        var lines = await Run("discord_resume", " " + generation.ToString(CultureInfo.InvariantCulture), null);
        return lines[0];
    }

    public async Task<bool> ClearPassword(int company)
    {
        var lines = await Run("discord_clear_password", " " + company.ToString(CultureInfo.InvariantCulture), null);
        return lines[0] == "cleared";
    }

    public async Task<string> ClearLinkedCompanyPassword(int company, uint clientId)
    {
        string args = FormattableString.Invariant($" {company} {clientId}");
        var lines = await Run("discord_clear_password", args, null);
        return lines[0];
    }

    private static CompanyProfit ParseCompany(string line)
    {
        string[] parts = line["company ".Length..].Split('|', 3);
        int id = int.Parse(parts[0], CultureInfo.InvariantCulture);
        long[] quarters = parts[1].Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(value => long.Parse(value, CultureInfo.InvariantCulture)).ToArray();
        return new CompanyProfit(id, parts[2], quarters);
    }

    private async Task<IReadOnlyList<string>> Run(string command, string args, string? end)
    {
        string nonce = Guid.NewGuid().ToString("N");
        string prefix = ReplyPrefix + nonce + " ";
        var lines = new List<string>();

        bool Match(IAdminEvent message)
        {
            if (message is not AdminRconEvent reply || !reply.Message.StartsWith(prefix, StringComparison.Ordinal))
            {
                return false;
            }

            string payload = reply.Message[prefix.Length..];
            lines.Add(payload);
            return end is null || payload == end;
        }

        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(4));
        await client.WaitForEvent(new AdminRconMessage($"{command} {nonce}{args}"), Match, TimeSpan.FromSeconds(4), cancellation.Token);
        return lines;
    }
}
