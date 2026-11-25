using System.Diagnostics.CodeAnalysis;
using Discord;
using OpenttdDiscord.Infrastructure.Chatting.Runners;
using OpenttdDiscord.Infrastructure.Discord.Commands;

namespace OpenttdDiscord.Infrastructure.Chatting.Commands
{
    [ExcludeFromCodeCoverage]
    internal class RegisterChatChannelCommand : OttdSlashCommandBase<RegisterChatChannelRunner>
    {
        public RegisterChatChannelCommand()
            : base("register-chat-channel")
        {
        }

        protected override void Configure(SlashCommandBuilder builder)
        {
            builder
                .WithDescription("Register channel on which this message will be sent as chat channel for given server.");
        }
    }
}
