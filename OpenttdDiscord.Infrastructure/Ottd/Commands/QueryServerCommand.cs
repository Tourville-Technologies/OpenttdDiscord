using Discord;
using OpenttdDiscord.Infrastructure.Discord.Commands;
using OpenttdDiscord.Infrastructure.Ottd.Runners;

namespace OpenttdDiscord.Infrastructure.Ottd.Commands
{
    internal class QueryServerCommand : OttdSlashCommandBase<QueryServerRunner>
    {
        public QueryServerCommand()
            : base("query-server")
        {
        }

        protected override void Configure(SlashCommandBuilder builder)
        {
            builder
                .WithDescription("Displays basic information about the server");
        }
    }
}
