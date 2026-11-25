using OpenttdDiscord.Domain.Servers;
using OpenttdDiscord.Infrastructure.Servers;

namespace OpenttdDiscord.Infrastructure.Tests.Servers;

public class GetSingleServerShould
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task ResolveOnlyASingleRegistrationFromTheInvokingGuild(int count)
    {
        const ulong guildId = 10;
        var repository = Substitute.For<IOttdServerRepository>();
        var servers = Enumerable.Range(1, count)
            .Select(id => new OttdServer(Guid.NewGuid(), guildId, "127.0.0.1", $"Server {id}", 3977, "test"))
            .ToList();
        repository.GetServersForGuild(guildId).Returns(servers);

        var result = await repository.GetSingleServer(guildId);

        Assert.Equal(count == 1, result.IsRight);
        if (count == 1)
        {
            Assert.Equal(servers[0], result.Case);
        }
    }
}
