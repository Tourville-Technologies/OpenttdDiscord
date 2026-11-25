using Discord;
using OpenttdDiscord.Infrastructure.Discord.Commands;

namespace OpenttdDiscord.Infrastructure.Ottd.Game;

internal sealed class GameCommand(bool admin) : OttdSlashCommandBase<GameCommandRunner>(admin ? "game-admin" : "game")
{
    protected override void Configure(SlashCommandBuilder builder)
    {
        if (admin)
        {
            builder.WithDescription("Manage an OpenTTD server")
                .WithDefaultMemberPermissions(GuildPermission.Administrator)
                .AddOption(Subcommand("start", "Start the configured OpenTTD service"))
                .AddOption(Subcommand("stop", "Stop the configured OpenTTD service"))
                .AddOption(Subcommand("restart", "Restart the configured OpenTTD service"))
                .AddOption(Subcommand("clear-password", "Remove a company's password").AddOption(CompanyOption(true)))
                .AddOption(Subcommand("ping-role", "Set or clear the role notified when an unpause vote opens")
                    .AddOption("role", ApplicationCommandOptionType.Role, "Role to ping. Omit this option to clear the saved role."));
            return;
        }

        builder.WithDescription("View and resume an OpenTTD game")
            .AddOption(Subcommand("status", "View the current game and its connected players"))
            .AddOption(Subcommand("profit", "Show the operating-profit graph for all companies or a selected company")
                .AddOption(CompanyOption(false, "company-id")))
            .AddOption(Subcommand("my-profit", "Get the profit graphs for companies your linked clients play in"))
            .AddOption(Subcommand("link", "Link your current OpenTTD client to Discord"))
            .AddOption(Subcommand("reset-password", "Clear the password for a company your linked client plays in").AddOption(CompanyOption(false)))
            .AddOption(Subcommand("unpause", "Open or vote in a poll to unpause the game")
                .AddOption(new SlashCommandOptionBuilder()
                    .WithName("vote")
                    .WithDescription("Vote yes to open the poll or yes/no to cast your vote")
                    .WithType(ApplicationCommandOptionType.String)
                    .AddChoice("Yes", "yes")
                    .AddChoice("No", "no")));
    }

    private static SlashCommandOptionBuilder Subcommand(string name, string description)
    {
        return new SlashCommandOptionBuilder().WithName(name).WithDescription(description)
            .WithType(ApplicationCommandOptionType.SubCommand);
    }

    private static SlashCommandOptionBuilder CompanyOption(bool required, string name = "company")
    {
        return new SlashCommandOptionBuilder().WithName(name).WithDescription("Company number shown in OpenTTD")
            .WithType(ApplicationCommandOptionType.Integer).WithRequired(required).WithMinValue(1).WithMaxValue(254);
    }
}
