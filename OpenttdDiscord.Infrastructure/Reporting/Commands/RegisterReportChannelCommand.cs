using Discord;
using OpenttdDiscord.Infrastructure.Discord.Commands;
using OpenttdDiscord.Infrastructure.Reporting.Runners;

namespace OpenttdDiscord.Infrastructure.Reporting.Commands
{
    internal class RegisterReportChannelCommand : OttdSlashCommandBase<RegisterReportChannelRunner>
    {
        public RegisterReportChannelCommand()
            : base("register-report-channel")
        {
        }

        protected override void Configure(SlashCommandBuilder builder)
        {
            builder
                .WithDescription("Creates a Discord channel for OpenTTD player reports");
        }
    }
}
