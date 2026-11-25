using System.Diagnostics.CodeAnalysis;
using Discord;
using LanguageExt;
using OpenttdDiscord.Infrastructure.Discord.Modals;

namespace OpenttdDiscord.Infrastructure.AutoReplies.Modals
{
    [ExcludeFromCodeCoverage]
    internal class SetWelcomeMessageModal : OttdModalBase
    {
        public string InitialWelcomeMessage { get; }

        public SetWelcomeMessageModal(Option<string> initialWelcomeMessage)
            : base(AutoReplyModals.SetWelcomeMessage)
        {
            this.InitialWelcomeMessage = initialWelcomeMessage
                .IfNone(string.Empty);
        }

        protected override void Configure(ModalBuilder builder)
        {
            builder
                .WithTitle("Set welcome message")
                .AddTextInput(
                    "Welcome message",
                    "content",
                    TextInputStyle.Paragraph,
                    value: InitialWelcomeMessage);
        }
    }
}