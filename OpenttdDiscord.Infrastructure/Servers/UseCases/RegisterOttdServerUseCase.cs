using LanguageExt;
using Microsoft.Extensions.Logging;
using OpenttdDiscord.Base.Ext;
using OpenttdDiscord.Domain.Security;
using OpenttdDiscord.Domain.Servers;
using OpenttdDiscord.Domain.Servers.UseCases;
using OpenttdDiscord.Infrastructure.Akkas;
using OpenttdDiscord.Infrastructure.Servers.Messages;
using OpenttdDiscord.Validation;
using OpenttdDiscord.Validation.Ottd;

namespace OpenttdDiscord.Infrastructure.Servers.UseCases
{
    internal class RegisterOttdServerUseCase : UseCaseBase, IRegisterOttdServerUseCase
    {
        private readonly IOttdServerRepository ottdServerRepository;
        private readonly ILogger logger;
        private readonly OttdValidator<OttdServerValidator, OttdServer> validator = new(new());
        private readonly IAkkaService akkaService;

        public RegisterOttdServerUseCase(
            ILogger<RegisterOttdServerUseCase> logger,
            IOttdServerRepository ottdServerRepository,
            IAkkaService akkaService)
        {
            this.ottdServerRepository = ottdServerRepository;
            this.akkaService = akkaService;
            this.logger = logger;
        }

        public EitherAsyncUnit Execute(User user, OttdServer server)
        {
            logger.LogTrace("Executing with {0} for\n{1}", user, server);

            return
                from _0 in CheckIfHasCorrectUserLevel(user, UserLevel.Admin).ToAsync()
                from _1 in validator.Validate(server).ToAsync()
                from _2 in EnsureNoServerRegistered(server.GuildId)
                from _3 in ottdServerRepository.InsertServer(server).ToAsync()
                from selection in akkaService.SelectActor(MainActors.Paths.Guilds)
                from _4 in selection.TellExt(new InformAboutServerRegistration(server)).ToAsync()
                select _4;
        }

        private EitherAsyncUnit EnsureNoServerRegistered(ulong guildId)
            => ottdServerRepository.GetServersForGuild(guildId).Bind(servers =>
                servers.Count == 0
                    ? EitherAsyncUnit.Right(Unit.Default)
                    : EitherAsyncUnit.Left(new HumanReadableError("This Discord server already has an OpenTTD server registered.")));
    }
}
