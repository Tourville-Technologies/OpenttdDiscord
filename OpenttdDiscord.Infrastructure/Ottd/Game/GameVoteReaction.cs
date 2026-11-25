using OpenttdDiscord.Infrastructure.Guilds.Messages;
using OpenttdDiscord.Infrastructure.Ottd.Messages;

namespace OpenttdDiscord.Infrastructure.Ottd.Game;

internal sealed record GameVoteReaction(
    Guid ServerId,
    ulong GuildId,
    Guid VoteId,
    ulong UserId,
    bool Unpause) : IGuildMessage, IOttdServerMessage;
