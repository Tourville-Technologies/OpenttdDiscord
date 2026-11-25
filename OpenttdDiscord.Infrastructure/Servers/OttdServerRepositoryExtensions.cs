using OpenttdDiscord.Base.Ext;
using OpenttdDiscord.Domain.Servers;

namespace OpenttdDiscord.Infrastructure.Servers;

internal static class OttdServerRepositoryExtensions
{
    public static EitherAsync<IError, OttdServer> GetSingleServer(this IOttdServerRepository repository, ulong guildId)
        => repository.GetServersForGuild(guildId).Bind(servers => servers.Count switch
        {
            0 => EitherAsync<IError, OttdServer>.Left(new HumanReadableError("Register an OpenTTD server in this Discord server first.")),
            1 => EitherAsync<IError, OttdServer>.Right(servers[0]),
            _ => EitherAsync<IError, OttdServer>.Left(new HumanReadableError("Multiple OpenTTD servers are registered here. Keep one registration to use this command.")),
        });
}
