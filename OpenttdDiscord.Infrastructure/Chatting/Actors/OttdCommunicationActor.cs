using Akka.Actor;
using LanguageExt;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTTDAdminPort;
using OpenTTDAdminPort.Events;
using OpenTTDAdminPort.Game;
using OpenTTDAdminPort.MainActor.StateData;
using OpenTTDAdminPort.Messages;
using OpenttdDiscord.Base.Ext;
using OpenttdDiscord.Domain.Servers;
using OpenttdDiscord.Infrastructure.Akkas;
using OpenttdDiscord.Infrastructure.Chatting.Messages;
using OpenttdDiscord.Infrastructure.Ottd.Messages;

namespace OpenttdDiscord.Infrastructure.Chatting.Actors
{
    internal class OttdCommunicationActor : ReceiveActorBase
    {
        private readonly OttdServer server;
        private readonly ulong channelId;
        private readonly IAdminPortClient client;
        private readonly IAkkaService akkaService;

        private readonly Dictionary<uint, byte> companiesByClient = new();
        private readonly Dictionary<byte, string> companyNames = new();

        private Option<IActorRef> chatChannel = Option<IActorRef>.None;

        public OttdCommunicationActor(
            IServiceProvider serviceProvider,
            ulong channelId,
            OttdServer server,
            IAdminPortClient client)
            : base(serviceProvider)
        {
            this.server = server;
            this.channelId = channelId;
            this.client = client;
            this.akkaService = SP.GetRequiredService<IAkkaService>();

            Ready();
            Self.Tell(new InitOttdCommunicationActor());
        }

        public static Props Create(IServiceProvider sp, ulong channelId, OttdServer server, IAdminPortClient client) =>
            Props.Create(() => new OttdCommunicationActor(sp, channelId, server, client));

        private void Ready()
        {
            ReceiveAsync<InitOttdCommunicationActor>(InitOttdCommunicationActor);
            Receive<HandleOttdMessage>(HandleOttdMessage);
            Receive<AdminChatMessageEvent>(HandleChatMessage);
            Receive<AdminClientInfoEvent>(msg => companiesByClient[msg.Player.ClientId] = msg.Player.PlayingAs);
            Receive<AdminClientJoinEvent>(HandleJoin);
            Receive<AdminClientDisconnectEvent>(HandleDisconnect);
            Receive<AdminClientUpdateEvent>(HandleCompanyChange);
            Receive<AdminCompanyInfoEvent>(msg => companyNames[msg.Company.Id] = msg.Company.Name);
            Receive<AdminCompanyUpdateEvent>(msg => companyNames[msg.Company.Id] = msg.Company.Name);
            Receive<AdminCompanyRemovalEvent>(msg => companyNames.Remove(msg.Company.Id));
            Receive<AdminServerConnectionLost>(_ =>
            {
                companiesByClient.Clear();
                companyNames.Clear();
            });
            ReceiveIgnore<IAdminEvent>();
        }

        private async Task InitOttdCommunicationActor(InitOttdCommunicationActor _)
        {
            var msg = new GetCreateChatChannel(channelId);
            var self = Self;

            (await (
            from chatManager in akkaService.SelectActor(MainActors.Paths.ChatChannelManager)
            from channelActor in chatManager.TryAsk<IActorRef>(msg)
            from _1 in AssignChannelActor(channelActor).ToAsync()
            from _2 in channelActor.TryAsk(new RegisterToChatChannel(self))
            select _2
            )).ThrowIfError();

            parent.Tell(new SubscribeToAdminEvents(self));
            if (await client.GetMainData() is ConnectedData connected)
            {
                foreach (var player in connected.Players.Values)
                {
                    companiesByClient[player.ClientId] = player.PlayingAs;
                }

                foreach (var company in connected.Companies.Values)
                {
                    companyNames[company.Id] = company.Name;
                }
            }
        }

        private EitherUnit AssignChannelActor(IActorRef channelActor)
        {
            this.chatChannel = Option<IActorRef>.Some(channelActor);
            return Unit.Default;
        }

        private void HandleChatMessage(AdminChatMessageEvent msg)
        {
            if (msg.Player.ClientId == 1)
            {
                // we ignore server messages
                return;
            }

            if (msg.NetworkAction is not NetworkAction.NETWORK_ACTION_CHAT and not NetworkAction.NETWORK_ACTION_SERVER_MESSAGE)
            {
                return;
            }

            if (msg.ChatDestination != ChatDestination.DESTTYPE_BROADCAST)
            {
                return;
            }

            this.chatChannel.TellMany(new HandleOttdMessage(server, msg.Player.Name, msg.Message));
        }

        private void HandleJoin(AdminClientJoinEvent message)
        {
            var player = message.Player;
            if (player.ClientId == 1)
            {
                return;
            }

            companiesByClient[player.ClientId] = player.PlayingAs;
            RelayNotice($"{player.Name} has joined the game.");
            if (player.PlayingAs < 253)
            {
                RelayCompanyJoin(player);
            }
        }

        private void HandleDisconnect(AdminClientDisconnectEvent message)
        {
            if (companiesByClient.Remove(message.Player.ClientId) && message.Player.ClientId != 1)
            {
                RelayNotice($"{message.Player.Name} has left the game.");
            }
        }

        private void HandleCompanyChange(AdminClientUpdateEvent message)
        {
            var player = message.Player;
            if (companiesByClient.TryGetValue(player.ClientId, out byte previous) && previous != player.PlayingAs && player.ClientId != 1)
            {
                if (player.PlayingAs < 253)
                {
                    RelayCompanyJoin(player);
                }
                else
                {
                    RelayNotice($"{player.Name} is now a spectator.");
                }
            }

            companiesByClient[player.ClientId] = player.PlayingAs;
        }

        private void RelayCompanyJoin(Player player)
        {
            string number = $"#{player.PlayingAs + 1}";
            string company = companyNames.TryGetValue(player.PlayingAs, out string? name) ? $"{name} ({number})" : number;
            RelayNotice($"{player.Name} has joined company {company}.");
        }

        private void RelayNotice(string text)
            => chatChannel.TellMany(new HandleOttdMessage(server, "OpenTTD", text, DiscordOnly: true));

        private void HandleOttdMessage(HandleOttdMessage msg)
        {
            if (msg.Server == server || msg.DiscordOnly)
            {
                return;
            }

            string message = $"[{msg.Server.Name}] {msg.Username}: {msg.Message}";

            client.SendMessage(new AdminChatMessage(NetworkAction.NETWORK_ACTION_CHAT, ChatDestination.DESTTYPE_BROADCAST, default, message));
            parent.Tell(msg);
        }

        protected override void PostStop()
        {
            base.PostStop();
            var self = Self;
            parent.Tell(new UnsubscribeFromAdminEvents(self));
            chatChannel.Some(a => a.Tell(new UnregisterFromChatChannel(self)));
            logger.LogInformation($"Removing Ottd communication Actor for {channelId}");
        }
    }
}
