using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using SchemaBuilder.Data;
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
                    svc.AddSingleton<DataExtractor>();
                    svc.AddSingleton<DocReader>();
                    svc.AddSingleton<WikiWriter>();
                    svc.AddSingleton<WikiClientFactory>();
                    svc.AddSingleton<WikiSchemaConfigReader>();
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
                    case "data":
                    {
                        var data = host.Services.GetRequiredService<DataExtractor>();
                        await data.Generate(args[1]);
                        break;
                    }
                    case "wiki":
                    {
                        var writer = host.Services.GetRequiredService<WikiWriter>();
                        await writer.Write(args[1]);
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