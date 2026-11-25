using Microsoft.Extensions.DependencyInjection;
using OpenttdDiscord.Infrastructure.Discord.Commands;
using OpenttdDiscord.Infrastructure.Modularity;
using OpenttdDiscord.Infrastructure.Ottd.Commands;
using OpenttdDiscord.Infrastructure.Ottd.Game;
using OpenttdDiscord.Infrastructure.Ottd.Runners;

namespace OpenttdDiscord.Infrastructure.Ottd
{
    internal class OttdModule : IModule
    {
        public void RegisterDependencies(IServiceCollection services)
        {
            services
                .RegisterCommands()
                .RegisterRunners();
        }
    }

#pragma warning disable SA1402 // File may only contain a single type
    internal static class OttdModuleExtensions
#pragma warning restore SA1402 // File may only contain a single type
    {
        public static IServiceCollection RegisterRunners(this IServiceCollection services)
        {
            services.AddSingleton<GameServiceControl>();
            services.AddSingleton<IGameVoteReactions, GameVoteReactions>();
            services.AddScoped<GameCommandRunner>();
            services.AddScoped<QueryServerRunner>();
            services.AddScoped<QueryDebugInfoRunner>();

            return services;
        }

        public static IServiceCollection RegisterCommands(this IServiceCollection services)
        {
            services.AddSingleton<IOttdSlashCommand>(_ => new GameCommand(admin: false));
            services.AddSingleton<IOttdSlashCommand>(_ => new GameCommand(admin: true));
            services.AddSingleton<IOttdSlashCommand, QueryServerCommand>();
            services.AddSingleton<IOttdSlashCommand, QueryDebugInfoCommand>();

            return services;
        }
    }
}
