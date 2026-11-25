using Discord;
using Discord.WebSocket;
using LanguageExt.UnitsOfMeasure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenttdDiscord.Base.Ext;
using OpenttdDiscord.Infrastructure.Discord.CommandResponses;
using OpenttdDiscord.Infrastructure.Discord.CommandRunners;
using OpenttdDiscord.Infrastructure.Discord.Commands;
using OpenttdDiscord.Infrastructure.Discord.ModalRunners;
using OpenttdDiscord.Infrastructure.Discord.Modals;
using OpenttdDiscord.Validation;

namespace OpenttdDiscord.Infrastructure.Discord
{
    internal class DiscordInteractionService : IDiscordInteractionService
    {
        private readonly ILogger logger;
        private readonly DiscordSocketClient client;
        private readonly IServiceProvider serviceProvider;
        private readonly ValidationErrorEmbedBuilder validationEmbedBuilder = new();
        private readonly Dictionary<string, IOttdSlashCommand> commands = new();
        private readonly Dictionary<string, Type> associatedModalRunners = new();

        public DiscordInteractionService(
            IServiceProvider serviceProvider,
            ILogger<DiscordInteractionService> logger,
            DiscordSocketClient client,
            IEnumerable<IOttdSlashCommand> commands,
            IEnumerable<IAssociatedModalRunners> associatedModalRunners
        )
        {
            this.logger = logger;
            this.serviceProvider = serviceProvider;
            this.client = client;
            client.SlashCommandExecuted += Client_SlashCommandExecuted;
            client.ModalSubmitted += ModalSubmitted;
            foreach (var c in commands)
            {
                this.commands.Add(
                    c.Name,
                    c);
            }

            IEnumerable<KeyValuePair<string, Type>> modalRunners = associatedModalRunners
                .SelectMany(x => x.AssociatedModalRunners);

            foreach (var modalRunner in modalRunners)
            {
                this.associatedModalRunners.Add(
                    modalRunner.Key,
                    modalRunner.Value);
            }
        }

        public async Task Register()
        {
            await RegisterCommands();
            await PruneCommands();
        }

        private async Task PruneCommands()
        {
            var existingCommands = await client.GetGlobalApplicationCommandsAsync();

            foreach (var c in existingCommands)
            {
                string name = c.Name;
                if (commands.ContainsKey(name))
                {
                    continue;
                }

                logger.LogInformation($"Removing dangling command {name}");
                await c.DeleteAsync();
                logger.LogInformation($"{name} deleted!");
            }
        }

        private async Task RegisterCommands()
        {
            var existingCommands = (await client.GetGlobalApplicationCommandsAsync())
                .ToDictionary(x => x.Name);

            foreach (var c in commands.Values)
            {
                var props = c.Build();

                if (existingCommands.TryGetValue(c.Name, out var existing))
                {
                    var configuredOptions = props.Options.IsSpecified ? props.Options.Value : Enumerable.Empty<ApplicationCommandOptionProperties>();
                    if (OptionsMatch(configuredOptions, existing.Options))
                    {
                        continue;
                    }

                    await existing.DeleteAsync();
                    logger.LogInformation("Re-registering {Command} with updated options", c.Name);
                }

                try
                {
                    logger.LogInformation($"Registering {c}");
                    await client.CreateGlobalApplicationCommandAsync(props);
                    logger.LogInformation($"Registered {c}");
                }
                catch (Exception ex)
                {
                    logger.LogError(
                        ex,
                        $"Something when wrong while registering {c}");
                }
            }
        }

        private static bool OptionsMatch(IEnumerable<ApplicationCommandOptionProperties> configured, IEnumerable<IApplicationCommandOption> registered)
        {
            return configured.Count() == registered.Count() && Enumerable.Zip(configured, registered, (expected, actual) =>
                expected.Name == actual.Name &&
                expected.Type == actual.Type &&
                OptionsMatch(
                    expected.Options ?? Enumerable.Empty<ApplicationCommandOptionProperties>(),
                    actual.Options ?? Enumerable.Empty<IApplicationCommandOption>())).All(matches => matches);
        }

        private Task Client_SlashCommandExecuted(SocketSlashCommand arg)
        {
            // Deferred game actions must not hold Discord's gateway event loop.
            _ = Task.Run(() => ProcessSlashCommand(arg));
            return Task.CompletedTask;
        }

        private async Task ProcessSlashCommand(SocketSlashCommand arg)
        {
            try
            {
                await RunSlashCommand(arg);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Discord slash command {Command} failed", arg.CommandName);
            }
        }

        private async Task RunSlashCommand(SocketSlashCommand arg)
        {
            logger.LogDebug(
                "{0} executing {1}",
                arg.User.Username,
                arg.CommandName);
            var command = this.commands[arg.Data.Name];
            using var scope = serviceProvider.CreateScope();
            IOttdSlashCommandRunner runner = command.CreateRunner(scope.ServiceProvider);
            logger.LogDebug("Created runner");

            Task<IInteractionResponse> responseTask = GetSlashCommandResponse(
                arg,
                runner);

            await HandleResponseTask(
                arg,
                responseTask);
        }

        private async Task ModalSubmitted(SocketModal arg)
        {
            string name = arg.Data.CustomId;
            logger.LogDebug(
                "{0} executing {1}",
                arg.User.Username,
                name);

            var modalRunnerType = this.associatedModalRunners[name];
            using var scope = serviceProvider.CreateScope();
            IOttdModalRunner runner = (IOttdModalRunner) scope.ServiceProvider.GetRequiredService(modalRunnerType);
            logger.LogDebug($"Created runner {runner.GetType()} for {name}");

            Task<IInteractionResponse> responseTask = GetModalResponse(
                arg,
                runner);

            await HandleResponseTask(
                arg,
                responseTask);
        }

        private async Task HandleResponseTask(
            IDiscordInteraction interaction,
            Task<IInteractionResponse> responseTask)
        {
            Task timeoutTask = Task.Delay(
                2.Seconds()
                    .ToTimeSpan());

            await Task.WhenAny(
                timeoutTask,
                responseTask);

            if (responseTask.IsCompletedSuccessfully)
            {
                await ExecuteResponse(
                    interaction,
                    responseTask.Result);
            }
            else
            {
                await ExecuteResponse(
                    interaction,
                    new TextResponse("Command has time outed :("));
            }
        }

        private async Task<IInteractionResponse> GetSlashCommandResponse(
            SocketSlashCommand arg,
            IOttdSlashCommandRunner runner)
        {
            var response = (await runner.Run(arg))
                .IfLeft(GenerateErrorResponse);
            return response;
        }

        private async Task<IInteractionResponse> GetModalResponse(
            SocketModal arg,
            IOttdModalRunner runner)
        {
            var response = (await runner.Run(arg))
                .IfLeft(GenerateErrorResponse);
            return response;
        }

        private async Task ExecuteResponse(
            IDiscordInteraction interaction,
            IInteractionResponse response)
        {
            (await response.Execute(interaction))
                .MapLeft(
                    (IError error) =>
                    {
                        if (error is ExceptionError ee)
                        {
                            logger.LogError(
                                ee.Exception,
                                $"Something went wrong while executing some command {interaction}.");
                        }

                        logger.LogWarning(
                            $"{interaction.User.Username} executed unsuccessfully {interaction} - {error.Reason}");
                        return error;
                    })
                .Map(
                    unit =>
                    {
                        logger.LogInformation($"{interaction.User.Username} executed successfully {interaction}");
                        return unit;
                    });
        }

        private IInteractionResponse GenerateErrorResponse(
            IError error)
        {
            string text =
                error is HumanReadableError ? $"Error: {error.Reason}" : "Something went wrong :(";

            if (error is ExceptionError ee)
            {
                logger.LogError(
                    ee.Exception,
                    $"Something went wrong while executing some interaction.");
            }

            if (error is ValidationError ve)
            {
                return new EmbedResponse(validationEmbedBuilder.BuildEmbed(ve));
            }

            return new TextResponse(text);
        }
    }
}