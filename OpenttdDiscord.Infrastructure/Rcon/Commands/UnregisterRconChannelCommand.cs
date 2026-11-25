using Discord;
using OpenttdDiscord.Infrastructure.Discord.Commands;
using OpenttdDiscord.Infrastructure.Rcon.Runners;

namespace OpenttdDiscord.Infrastructure.Rcon.Commands
{
    internal class UnregisterRconChannelCommand : OttdSlashCommandBase<UnregisterRconChannelRunner>
    {
        public UnregisterRconChannelCommand()
            : base("unregister-rcon-channel")
        {
        }

        protected override void Configure(SlashCommandBuilder builder)
        {
            builder
                .WithDescription("Unregisters rcon channel for given discord channel and server");
        }
    }
}
