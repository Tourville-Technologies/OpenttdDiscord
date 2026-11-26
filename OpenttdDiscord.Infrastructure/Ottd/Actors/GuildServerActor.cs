using Akka.Actor;
using Discord;
using Discord.WebSocket;
using LanguageExt;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTTDAdminPort;
using OpenTTDAdminPort.Events;
using OpenttdDiscord.Base.Basics;
using OpenttdDiscord.Base.Ext;
using OpenttdDiscord.Domain.Chatting.UseCases;
using OpenttdDiscord.Domain.Security;
using OpenttdDiscord.Domain.Servers;
using OpenttdDiscord.Domain.Statuses;
using OpenttdDiscord.Domain.Statuses.UseCases;
using OpenttdDiscord.Infrastructure.Akkas;
using OpenttdDiscord.Infrastructure.Chatting;
using OpenttdDiscord.Infrastructure.Chatting.Actors;
using OpenttdDiscord.Infrastructure.Chatting.Messages;
using OpenttdDiscord.Infrastructure.Discord.Messages;
using OpenttdDiscord.Infrastructure.EventLogs.Actors;
using OpenttdDiscord.Infrastructure.EventLogs.Messages;
using OpenttdDiscord.Infrastructure.Maintenance.Actors;
using OpenttdDiscord.Infrastructure.Ottd.Game;
using OpenttdDiscord.Infrastructure.Ottd.Messages;
using OpenttdDiscord.Infrastructure.Statuses.Actors;
using OpenttdDiscord.Infrastructure.Statuses.Messages;
using Serilog;

namespace OpenttdDiscord.Infrastructure.Ottd.Actors
{
    internal partial class GuildServerActor : ReceiveActorBase, IWithTimers
    {
        private readonly OttdServer server;
        private readonly AdminPortClient client;
        private readonly IAkkaService akkaService;
        private readonly GameSession game;
        private readonly IGetStatusMonitorsForServerUseCase getStatusMonitorsForServerUseCase;
        private readonly IUpdateStatusMonitorUseCase updateStatusMonitorUseCase;
        private readonly IGetChatChannelUseCase getChatChannelUseCase;
        private readonly ExtDictionary<ulong, IActorRef> statusMonitorActors = new();
        private readonly ExtDictionary<ulong, ChattingActors> chatChannelActors = new();
        private readonly System.Collections.Generic.HashSet<IActorRef> adminEventSubscribers = new();
        private bool receivedAdminEvents;
        private Option<IActorRef> chatStorageActor = Option<IActorRef>.None;
        private Option<IActorRef> healthCheckACtor = Option<IActorRef>.None;

        public ITimerScheduler Timers { get; set; } = default!;

        public GuildServerActor(
            IServiceProvider serviceProvider,
            OttdServer server)
            : base(serviceProvider)
        {
            this.server = server;

            this.getStatusMonitorsForServerUseCase = SP.GetRequiredService<IGetStatusMonitorsForServerUseCase>();
            this.updateStatusMonitorUseCase = SP.GetRequiredService<IUpdateStatusMonitorUseCase>();
            this.getChatChannelUseCase = SP.GetRequiredService<IGetChatChannelUseCase>();
            this.akkaService = SP.GetRequiredService<IAkkaService>();

            string adminAddress = AdminServerAddressResolver.Resolve(server.Ip);
            client = new(new AdminPortClientSettings()
            {
                WatchdogInterval = TimeSpan.FromSeconds(5),
            },
            new(adminAddress, server.AdminPort, server.AdminPortPassword),
            logging => logging.AddSerilog());

            game = new GameSession(client);

            RconConstructor();
            ReportConstructor();
            AutoReplyConstructor(client);

            Ready();
            RconReady();
            ReportReady();
            AutoReplyReady();
            Self.Tell(new InitGuildServerActorMessage());
        }

        private void Ready()
        {
            ReceiveAsync<InitGuildServerActorMessage>(InitGuildServerActorMessage);
            Receive<ExecuteServerAction>(ExecuteServerAction);
            Receive<RegisterStatusMonitor>(RegisterStatusMonitor);
            ReceiveAsync<RemoveStatusMonitor>(RemoveStatusMonitor);
            ReceiveAsync<UpdateStatusMonitor>(UpdateStatusMonitor);
            Receive<RegisterChatChannel>(RegisterChatChannel);
            ReceiveAsync<UnregisterChatChannel>(UnregisterChatChannel);

            Receive<KillDanglingActor>(msg => msg.commandActor.GracefulStop(TimeSpan.FromSeconds(1)));
            ReceiveAsync<GameRequest>(HandleGameRequest);
            Receive<GameVoteReaction>(reaction => game.RecordVote(reaction.VoteId, reaction.UserId, reaction.Unpause));
            ReceiveAsync<CloseGameVote>(FinishGameVote);
            ReceiveAsync<AdminServerConnectionLost>(ReconnectSecure);
            Receive<IAdminEvent>(HandleAdminEvent);
            Receive<SubscribeToAdminEvents>(m => adminEventSubscribers.Add(m.Subscriber));
            Receive<UnsubscribeFromAdminEvents>(m => adminEventSubscribers.Remove(m.subscriber));
            Receive<RetrieveEventLog>(RedirectMessageToEventLog);
            Receive<HandleDiscordMessage>(RedirectMessageToEventLog);
            Receive<HandleOttdMessage>(RedirectMessageToEventLog);
        }

        public static Props Create(IServiceProvider sp, OttdServer server)
            => Props.Create(() => new GuildServerActor(sp, server));

        protected override void PostStop()
        {
            client.Disconnect().Wait();
            base.PostStop();
        }

        private async Task InitGuildServerActorMessage(InitGuildServerActorMessage _)
        {
            logger.LogInformation($"Connecting to {server.Name} on {server.Ip}:{server.AdminPort}");
            client.SetAdminEventHandler(ev => self.Tell(ev));
            await client.ConnectSecure();

            var monitors = (await getStatusMonitorsForServerUseCase.Execute(User.Master, server.Id))
                .ThrowIfError()
                .Right();

            foreach (var monitor in monitors)
            {
                CreateStatusMonitor(monitor);
                logger.LogInformation($"Created monitor for {server.Name} on channel {monitor.ChannelId}");
            }

            var chatChannels = (await getChatChannelUseCase.Execute(User.Master, server.Id))
                .ThrowIfError()
                .Right();

            foreach (var cc in chatChannels)
            {
                RegisterChatChannel(new(cc));
            }

            await RconInit();
            await ReportInit();

            chatStorageActor = Some(Context.ActorOf(EventStorageActor.Create(SP, server, client)));
            healthCheckACtor = Some(
                Context.ActorOf(
                    OttdServerHealthCheckActor.Create(
                        client,
                        server,
                        SP)));
        }

        private void HandleAdminEvent(IAdminEvent message)
        {
            receivedAdminEvents = true;
            if (message is AdminChatMessageEvent chat && game.ConsumeLink(chat))
            {
                return;
            }

            if (message is AdminClientDisconnectEvent disconnect)
            {
                game.Disconnect(disconnect.Player.ClientId);
            }

            if (message is AdminClientUpdateEvent update)
            {
                game.UpdateCompany(update.Player);
            }

            adminEventSubscribers.TellMany(message);
        }

        private async Task ReconnectSecure(AdminServerConnectionLost message)
        {
            adminEventSubscribers.TellMany(message);
            if (!receivedAdminEvents)
            {
                return;
            }

            receivedAdminEvents = false;
            game.Clear();

            // The AdminPort library's automatic retry uses legacy authentication.
            await client.Disconnect();
            await client.ConnectSecure();
        }

        private async Task HandleGameRequest(GameRequest request)
        {
            var sender = Sender;
            try
            {
                var reply = await game.Execute(request);
                if (reply.OpenedVote is { } voteId)
                {
                    Timers.StartSingleTimer(voteId, new CloseGameVote(voteId, request.ChannelId), GameSession.VoteDuration);
                }

                sender.Tell(reply);
            }
            catch (TimeoutException)
            {
                sender.Tell(new GameReply("OpenTTD did not answer. Check the admin connection and companion OpenTTD patch."));
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Game command {Action} failed for server {ServerId}", request.Action, server.Id);
                sender.Tell(new GameReply("OpenTTD could not complete the command."));
            }
        }

        private async Task FinishGameVote(CloseGameVote vote)
        {
            SP.GetRequiredService<IGameVoteReactions>().Close(vote.Id);
            GameReply? result;
            try
            {
                result = await game.FinishVote(vote.Id);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Could not close the vote for OpenTTD server {ServerId}", server.Id);
                result = new GameReply("Could not complete the unpause vote. Check whether OpenTTD is connected before voting again.");
            }

            if (result is null)
            {
                return;
            }

            try
            {
                var discord = SP.GetRequiredService<DiscordSocketClient>();
                if (await discord.GetChannelAsync(vote.ChannelId) is IMessageChannel channel)
                {
                    await channel.SendMessageAsync(result.Text, allowedMentions: AllowedMentions.None);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Could not post vote result for OpenTTD server {ServerId}", server.Id);
            }
        }

        private void ExecuteServerAction(ExecuteServerAction cmd)
        {
            Props props = cmd.CreateCommandActorProps(SP, server, client);
            var commandActor = Context.ActorOf(props);
            Timers.StartSingleTimer(commandActor, new KillDanglingActor(commandActor), cmd.TimeOut);
            commandActor.Tell(cmd);
        }

        private void RegisterStatusMonitor(RegisterStatusMonitor msg)
        {
            CreateStatusMonitor(msg.StatusMonitor);
            Sender.Tell(Unit.Default);
        }

        private EitherUnit CreateStatusMonitor(StatusMonitor monitor)
        {
            Props props = StatusMonitorActor.Create(server, monitor, client, SP);
            IActorRef actor = Context.ActorOf(props);
            statusMonitorActors.Add(monitor.ChannelId, actor);
            return Unit.Default;
        }

        private async Task RemoveStatusMonitor(RemoveStatusMonitor rmv)
        {
            var statusMonitor = statusMonitorActors.TryGetValueAs<IActorRef>(rmv.ChannelId);

            if (statusMonitor.IsNone)
            {
                throw new ActorNotFoundException();
            }

            var actor = statusMonitor.Value();
            actor.Forward(rmv);
            await actor.GracefulStop(TimeSpan.FromSeconds(1));
            statusMonitorActors.Remove(rmv.ChannelId);
            Sender.Tell(Unit.Default);
        }

        private async Task UpdateStatusMonitor(UpdateStatusMonitor usm)
        {
            var self = Self;
            var monitorResult = await this.updateStatusMonitorUseCase.Execute(User.Master, usm.UpdatedMonitor);
            await monitorResult.MapAsync(async monitor =>
            {
                await this.RemoveStatusMonitor(new(usm.UpdatedMonitor.ServerId, usm.UpdatedMonitor.GuildId, usm.UpdatedMonitor.ChannelId));
                self.Tell(new RegisterStatusMonitor(monitor));
                return Unit.Default;
            });
        }

        private void RegisterChatChannel(RegisterChatChannel rcc)
        {
            if (chatChannelActors.ContainsKey(rcc.chatChannel.ChannelId))
            {
                logger.LogWarning($"Chat channel {server.Name} - {rcc.chatChannel.ChannelId} already registered");
                return;
            }

            var discord = Context.ActorOf(DiscordCommunicationActor.Create(SP, rcc.chatChannel.ChannelId, client, server), $"discordCommunication-{rcc.chatChannel.ChannelId}");
            var openttd = Context.ActorOf(OttdCommunicationActor.Create(SP, rcc.chatChannel.ChannelId, server, client), $"ottdCommunication-{rcc.chatChannel.ChannelId}");

            var chattingActors = new ChattingActors(rcc.chatChannel, discord, openttd);
            chatChannelActors.Add(rcc.chatChannel.ChannelId, chattingActors);
            logger.LogInformation($"Registered chat channel {rcc.chatChannel.ChannelId} for {server.Name}");
        }

        private async Task UnregisterChatChannel(UnregisterChatChannel ucc)
        {
            if (!chatChannelActors.ContainsKey(ucc.ChannelId))
            {
                logger.LogError($"Chat channel {server.Name} - {ucc.ChannelId} already removed");
                return;
            }

            ChattingActors actors = chatChannelActors.GetValueAs<ChattingActors>(ucc.ChannelId);

            await Task.WhenAll(
                actors.Discord.GracefulStop(TimeSpan.FromSeconds(1)),
                actors.Ottd.GracefulStop(TimeSpan.FromSeconds(1))
                );

            chatChannelActors.Remove(ucc.ChannelId);

            await
            (from chatManager in akkaService.SelectActor(MainActors.Paths.ChatChannelManager)
             from _1 in chatManager.TellExt(ucc).ToAsync()
             select _1);
        }

        private void RedirectMessageToEventLog(object msg)
        {
            if (chatStorageActor.IsNone)
            {
                return;
            }

            var actor = (IActorRef)chatStorageActor.Case!;
            actor.Forward(msg);
        }

        private sealed record CloseGameVote(Guid Id, ulong ChannelId);
    }
}
