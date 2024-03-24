using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using SchemaService.SteamUtils;
using SteamKit2;

namespace SchemaBuilder
{
    internal class Program
    {
        public static async Task Main(string[] args)
        {
            using var host = new HostBuilder()
                .ConfigureServices(svc =>
                {
                    svc.AddSteamDownloader(SteamConfiguration.Create(x => { }));
                    svc.AddSingleton<GameManager>();
                    svc.AddHostedService<GameManager>();
                    svc.AddSingleton<SchemaGenerator>();
                    svc.AddSingleton<DocReader>();
                    svc.AddSingleton<PostprocessUnordered>();
                })
                .Build();
            await host.StartAsync();
            var schemas = host.Services.GetRequiredService<SchemaGenerator>();
            try
            {
                await schemas.Generate(args[0]);
            }
            finally
            {
                await host.StopAsync();
            }
        }
    }
}