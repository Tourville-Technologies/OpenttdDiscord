using System.Globalization;
using OpenTTDAdminPort;
using OpenTTDAdminPort.Events;
using OpenTTDAdminPort.Messages;

namespace OpenttdDiscord.Infrastructure.Ottd.Game;

internal sealed class GameConsole(IAdminPortClient client)
{
    public const string ReplyPrefix = "OTTD_DISCORD ";

    public async Task<(string State, int Clients, ulong Generation, int Year, int Month, int Day, int DaysToNextQuarter, IReadOnlyList<CompanyProfit> Companies)> Snapshot(bool includeCompanies)
    {
        var lines = await Run("discord_snapshot", includeCompanies ? string.Empty : " state", "end");
        string[] state = lines[0].Split(' ');
        int players = int.Parse(state[2], CultureInfo.InvariantCulture);
        ulong generation = ulong.Parse(state[3], CultureInfo.InvariantCulture);
        int year = state.Length > 4 ? int.Parse(state[4], CultureInfo.InvariantCulture) : 0;
        int month = state.Length > 5 ? int.Parse(state[5], CultureInfo.InvariantCulture) : 0;
        int day = state.Length > 6 ? int.Parse(state[6], CultureInfo.InvariantCulture) : 1;
        int daysToNextQuarter = state.Length > 7 ? int.Parse(state[7], CultureInfo.InvariantCulture) : 0;
        var companies = lines.Skip(1).SkipLast(1).Select(ParseCompany).ToArray();
        return (state[1], players, generation, year, month, day, daysToNextQuarter, companies);
    }

    public async Task<string> Resume(ulong generation)
    {
        var lines = await Run("discord_resume", " " + generation.ToString(CultureInfo.InvariantCulture), null);
        return lines[0];
    }

    public async Task<string> SetPaused(bool paused)
    {
        string command = paused ? "discord_force_pause" : "discord_force_resume";
        var lines = await Run(command, string.Empty, null);
        return lines[0];
    }

    public async Task<string> RestoreAutomaticPause()
    {
        var lines = await Run("discord_soft_pause", string.Empty, null);
        return lines[0];
    }

    public async Task<string> PauseStatus()
    {
        var lines = await Run("discord_pause_status", string.Empty, null);
        return lines[0];
    }

    public async Task<bool> ClearPassword(int company)
    {
        var lines = await Run("discord_clear_password", " " + company.ToString(CultureInfo.InvariantCulture), null);
        return lines[0] == "cleared";
    }

    private static CompanyProfit ParseCompany(string line)
    {
        string[] parts = line["company ".Length..].Split('|', 4);
        int id = int.Parse(parts[0], CultureInfo.InvariantCulture);
        long[] quarters = parts[1].Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(value => long.Parse(value, CultureInfo.InvariantCulture)).ToArray();
        if (parts.Length == 3)
        {
            return new CompanyProfit(id, parts[2], quarters);
        }

        uint colour = uint.Parse(parts[2], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return new CompanyProfit(id, parts[3], quarters, colour);
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
