using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using SchemaBuilder.Schema;
using SchemaService.SteamUtils;
using SteamKit2;
using SteamKit2.Discovery;

namespace SchemaBuilder
{
    internal class Program
    {
        public static async Task Main(string[] args)
        {
            using var host = new HostBuilder()
                .ConfigureServices(svc =>
                {
                    svc.AddSteamDownloader(SteamConfiguration.Create(x => x
                        .WithProtocolTypes(ProtocolTypes.WebSocket)
                        .WithServerListProvider(new MemoryServerListProvider())));
                    svc.AddSingleton<GameManager>();
                    svc.AddHostedService<GameManager>();
                    svc.AddSingleton<SchemaGenerator>();
                    svc.AddSingleton<DocReader>();
                })
                .Build();
            await host.StartAsync();
            try
            {
                switch (args[0])
                {
                    case "schema":
                    {
                        var schemas = host.Services.GetRequiredService<SchemaGenerator>();
                        await schemas.Generate(args[1]);
                        break;
                    }
                    default:
                        throw new ArgumentException($"Unsupported mode: {args[0]}");
                }
            }
            finally
            {
                await host.StopAsync();
            }
        }
    }
}