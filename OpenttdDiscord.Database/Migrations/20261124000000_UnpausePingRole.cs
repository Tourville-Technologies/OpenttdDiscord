using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace OpenttdDiscord.Database.Migrations;

[DbContext(typeof(OttdContext))]
[Migration("20261124000000_UnpausePingRole")]
[ExcludeFromCodeCoverage]
public sealed class UnpausePingRole : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<decimal>(
            name: "UnpausePingRoleId",
            table: "Servers",
            type: "numeric(20,0)",
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "UnpausePingRoleId", table: "Servers");
    }
}
