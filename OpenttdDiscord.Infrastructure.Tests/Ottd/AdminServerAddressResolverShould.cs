using System.Net;
using OpenttdDiscord.Infrastructure.Ottd;

namespace OpenttdDiscord.Infrastructure.Tests.Ottd;

public class AdminServerAddressResolverShould
{
    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("::1")]
    public void PreserveNumericAddressesWithoutDnsLookup(string address)
    {
        bool lookedUp = false;

        string result = AdminServerAddressResolver.Resolve(address, _ =>
        {
            lookedUp = true;
            return System.Array.Empty<IPAddress>();
        });

        Assert.Equal(address, result);
        Assert.False(lookedUp);
    }

    [Fact]
    public void PreferAnIpv4AddressForAHostname()
    {
        string result = AdminServerAddressResolver.Resolve(
            "host.docker.internal",
            _ => new[] { IPAddress.Parse("2001:db8::1"), IPAddress.Parse("192.0.2.10") });

        Assert.Equal("192.0.2.10", result);
    }

    [Fact]
    public void FallBackToIpv6WhenNoIpv4AddressExists()
    {
        string result = AdminServerAddressResolver.Resolve(
            "openttd.example.test",
            _ => new[] { IPAddress.Parse("2001:db8::5") });

        Assert.Equal("2001:db8::5", result);
    }
}
