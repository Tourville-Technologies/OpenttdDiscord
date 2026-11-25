using OpenttdDiscord.Infrastructure.Guilds.Messages;
using OpenttdDiscord.Infrastructure.Ottd.Messages;

namespace OpenttdDiscord.Infrastructure.Ottd.Game;

internal sealed record GameRequest(
    Guid ServerId,
    ulong GuildId,
    ulong UserId,
    ulong ChannelId,
    string Action,
    int? Company,
    string? Vote) : IGuildMessage, IOttdServerMessage;
