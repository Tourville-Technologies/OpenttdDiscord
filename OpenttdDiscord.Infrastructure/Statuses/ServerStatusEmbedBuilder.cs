using Discord;
using OpenTTDAdminPort;
using OpenTTDAdminPort.Game;

namespace OpenttdDiscord.Infrastructure.Statuses
{
    public class ServerStatusEmbedBuilder
    {
        public Embed CreateServerStatusEmbed(ServerStatus serverStatus, AdminServerInfo info)
        {
            string mapName = string.IsNullOrEmpty(info.MapName) ? "Random map" : info.MapName;

            EmbedBuilder embedBuilder = new();
            embedBuilder.WithTitle($"{info.ServerName} Status");

            var clients = serverStatus.Players.Values.Where(player => player.ClientId != 1).ToArray();
            embedBuilder.AddField("Players", clients.Length, true);
            embedBuilder.AddField("Map Size", $"{info.MapWidth}x{info.MapHeight}", true);
            embedBuilder.AddField("Year", info.Date, true);

            embedBuilder.AddField("Map Name", mapName, true);
            embedBuilder.AddField("Climate", info.Landscape.ToHumanReadable(), true);

            string players = string.Join('\n', clients.Select(player => player.Name));
            if (players.Length > 1024)
            {
                players = players[..1020] + " …";
            }

            if (!string.IsNullOrEmpty(players))
            {
                embedBuilder.AddField("Players", players, false);
            }

            var embed = embedBuilder.Build();
            return embed;
        }
    }
}
