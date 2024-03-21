using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SteamKit2;
using SteamKit2.CDN;

namespace SchemaService.SteamUtils
{
    public class CdnPool
    {
        private readonly ILogger<CdnPool> _log;
        private readonly SteamClient _client;
        private int _cellId;
        private readonly ConcurrentBag<Client> _clientBag = new ConcurrentBag<Client>();
        private readonly List<Server> _servers = new List<Server>();

        public CdnPool(ILogger<CdnPool> log, SteamClient client)
        {
            _log = log;
            _client = client;
        }

        /// <summary>
        /// Initializes stuff needed to download content from the Steam content servers.
        /// </summary>
        /// <returns></returns>
        public async Task Initialize(int cellId)
        {
            _cellId = cellId;
            Client.RequestTimeout = TimeSpan.FromSeconds(10);
            ServicePointManager.DefaultConnectionLimit = Math.Max(ServicePointManager.DefaultConnectionLimit, 100);
            await RefreshServers();
        }

        public Client TakeClient()
        {
            if (_servers == null)
                return null;

            if (!_clientBag.TryTake(out var client))
            {
                client = new Client(_client);
            }

            return client;
        }

        public void ReturnClient(Client client)
        {
            _clientBag.Add(client);
        }

        private void SortServers()
        {
            _servers.Sort((a, b) => a.WeightedLoad.CompareTo(b.WeightedLoad));
        }

        private async Task RefreshServers()
        {
            var servers = await ContentServerDirectoryService
                .LoadAsync(_client.Configuration, _cellId, CancellationToken.None)
                .ConfigureAwait(false);
            lock (_servers)
            {
                _servers.Clear();
                foreach (var server in servers)
                    if (!server.UseAsProxy && !server.SteamChinaOnly)
                        _servers.Add(server);
                _log.LogInformation($"Got {_servers.Count} CDN servers.");
                SortServers();
            }
        }

        public async Task<Server> TakeServer()
        {
            bool refresh;
            lock (_servers)
            {
                refresh = _servers.Count == 0;
            }

            if (refresh)
                await RefreshServers();
            Server server;
            lock (_servers)
            {
                server = _servers[0];
                _servers.RemoveAt(0);
            }

            return server;
        }

        public void ReturnServer(Server server)
        {
            lock (_servers)
            {
                _servers.Add(server);
                SortServers();
            }
        }
    }
}