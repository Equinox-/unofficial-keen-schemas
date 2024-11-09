using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using WikiClientLibrary.Client;
using WikiClientLibrary.Pages;
using WikiClientLibrary.Sites;

namespace SchemaBuilder.Data
{
    public class WikiWriter
    {
        private static readonly TimeSpan Backoff = TimeSpan.FromMinutes(3);
        private readonly ILogger<WikiWriter> _log;
        private readonly ILoggerFactory _loggerFactory;

        public WikiWriter(ILogger<WikiWriter> logger, ILoggerFactory loggerFactory)
        {
            _log = logger;
            _loggerFactory = loggerFactory;
        }

        public async Task Write(string name)
        {
            var dataPath = Path.GetFullPath(Path.Combine("data", name)) + Path.DirectorySeparatorChar;
            if (!Directory.Exists(dataPath))
                throw new ArgumentException($"Data directory does not exist {dataPath}");

            var endpoint = Environment.GetEnvironmentVariable("WIKI_ENDPOINT") ?? throw new ArgumentException("No WIKI_ENDPOINT set");
            var user = Environment.GetEnvironmentVariable("WIKI_USER") ?? throw new ArgumentException("No WIKI_USER set");
            var password = Environment.GetEnvironmentVariable("WIKI_PASS") ?? throw new ArgumentException("No WIKI_PASS set");

            using var client = new WikiClient(new HttpClientHandler());
            client.Timeout = Backoff;
            client.RetryDelay = Backoff;
            client.Logger = _loggerFactory.CreateLogger<WikiClient>();
            client.ClientUserAgent =
                $"unofficial-keen-schemas/{(typeof(WikiWriter).Assembly.GetCustomAttribute<AssemblyVersionAttribute>()?.Version ?? "unknown")} (https://github.com/Equinox-/unofficial-keen-schemas/issues/new)";

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
            _log.LogInformation($"User: {site.AccountInfo.Name}");

            foreach (var file in Directory.EnumerateFiles(dataPath).Select(Path.GetFullPath))
            {
                var pageName = UnescapePageName(TaskAvoidance.MakeRelativePath(dataPath, file));
                var fingerprint = TaskAvoidance.CachedFingerprint.Compute(file, default).Hash;
                var page = new WikiPage(site, pageName);
                await MaybeUpdate();
                continue;

                async Task MaybeUpdate()
                {
                    var attempts = 0;
                    for (var attempt = 0; attempt < 5; attempt++)
                    {
                        try
                        {
                            attempts++;
                            if (await AttemptUpdate())
                                return;
                        }
                        catch (Exception err)
                        {
                            if (attempts >= 5)
                                throw;
                            _log.LogWarning(err, "Update attempt failed, retrying");
                        }

                        await Task.Delay(Backoff);
                    }
                }

                async Task<bool> AttemptUpdate()
                {
                    await page.RefreshAsync(PageQueryOptions.None);
                    var revision = page.LastRevision;

                    const string prefix = "Updated by schema generator, hash is ";
                    if (revision != null)
                    {
                        if (revision.Comment.StartsWith(prefix + fingerprint))
                        {
                            _log.LogInformation($"Page {pageName} is unchanged");
                            return true;
                        }

                        if (!revision.Comment.StartsWith(prefix) && !revision.Comment.StartsWith("Allow bot updates"))
                        {
                            _log.LogWarning($"Page {pageName} has a non-automated change, please edit with a comment \"Allow bot updates\" to overwrite");
                            return true;
                        }
                    }

                    _log.LogInformation($"Updating page {pageName}");
                    page.Content = File.ReadAllText(file);
                    return await page.UpdateContentAsync(prefix + fingerprint, false, true, AutoWatchBehavior.None);
                }
            }
        }

        private static string UnescapePageName(string name)
        {
            var dest = new StringBuilder(name.Length);
            for (var i = 0; i < name.Length; i++)
            {
                var ch = name[i];
                if (ch == '_')
                {
                    dest.Append((char)Convert.ToUInt16(name.Substring(i + 1, 4), 16));
                    i += 4;
                }
                else
                    dest.Append(ch);
            }

            return dest.ToString();
        }
    }
}