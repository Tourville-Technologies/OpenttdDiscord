using System.Diagnostics.CodeAnalysis;
using Discord;
using OpenttdDiscord.Infrastructure.Chatting.Runners;
using OpenttdDiscord.Infrastructure.Discord.Commands;

namespace OpenttdDiscord.Infrastructure.Chatting.Commands
{
    [ExcludeFromCodeCoverage]
    internal class UnregisterChatChannelCommand : OttdSlashCommandBase<UnregisterChatChannelRunner>
    {
        public UnregisterChatChannelCommand()
            : base("unregister-chat-channel")
        {
        }

        protected override void Configure(SlashCommandBuilder builder)
        {
            builder
                .WithDescription("Removes given server from chat channel completely");
        }
    }
}
