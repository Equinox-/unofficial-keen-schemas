using System;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using WikiClientLibrary.Client;
using WikiClientLibrary.Sites;

namespace SchemaBuilder
{
    public class WikiClientFactory
    {
        private static readonly TimeSpan Backoff = TimeSpan.FromMinutes(3);
        private readonly ILogger<WikiClientFactory> _log;
        private readonly ILoggerFactory _loggerFactory;

        public WikiClientFactory(ILogger<WikiClientFactory> logger, ILoggerFactory loggerFactory)
        {
            _log = logger;
            _loggerFactory = loggerFactory;
        }

        public  Task WithClient(string endpoint, Func<WikiSite, Task> callback) => WithClient(endpoint, null, null, callback);

        public async Task WithClient(string endpoint, string user, string password, Func<WikiSite, Task> callback)
        {
            using var client = new WikiClient(new HttpClientHandler());
            client.Timeout = Backoff;
            client.RetryDelay = Backoff;
            client.Logger = _loggerFactory.CreateLogger<WikiClient>();
            client.ClientUserAgent =
                $"unofficial-keen-schemas/{typeof(WikiClientFactory).Assembly.GetCustomAttribute<AssemblyVersionAttribute>()?.Version ?? "unknown"} (https://github.com/Equinox-/unofficial-keen-schemas/issues/new)";

            var site = new WikiSite(client, new SiteOptions
            {
                ApiEndpoint = endpoint,
                AccountAssertion = AccountAssertionBehavior.AssertBot,
            }, user, password)
            {
                Logger = _loggerFactory.CreateLogger<WikiSite>()
            };
            await site.Initialization;
            _log.LogInformation($"API Version: {site.SiteInfo.Version}");
            _log.LogInformation($"User: {site.AccountInfo?.Name ?? "none"}");

            await callback(site);
        }
    }
}