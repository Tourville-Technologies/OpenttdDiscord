using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;
using OpenttdDiscord.Database.Statuses;
using OpenttdDiscord.Domain.Servers;

namespace OpenttdDiscord.Database.Ottd.Servers;

public record OttdServerEntity(
Guid Id,
ulong GuildId,
string Ip,
string Name,
int AdminPort,
string AdminPortPassword
)
{
    public ulong? UnpausePingRoleId { get; set; }

    public List<StatusMonitorEntity> Monitors { get; set; } = default!;

    public OttdServerEntity(OttdServer ottdServer)
        : this(
        ottdServer.Id,
        ottdServer.GuildId,
        ottdServer.Ip,
        ottdServer.Name,
        ottdServer.AdminPort,
        ottdServer.AdminPortPassword)
    {
        UnpausePingRoleId = ottdServer.UnpausePingRoleId;
    }

    public OttdServer ToDomain()
    {
        return new(
            Id,
            GuildId,
            Ip,
            Name,
            AdminPort,
            AdminPortPassword) { UnpausePingRoleId = UnpausePingRoleId };
    }

    [ExcludeFromCodeCoverage]
    public static void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<OttdServerEntity>()
            .HasKey(x => x.Id);

        modelBuilder.Entity<OttdServerEntity>()
            .HasIndex(x => x.Name)
            .IsUnique(true);
    }
}
