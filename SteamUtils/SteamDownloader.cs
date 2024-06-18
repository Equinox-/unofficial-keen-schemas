using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SteamKit2;
using SteamKit2.Internal;
using static SteamKit2.SteamClient;
using static SteamKit2.SteamUser;
using static SteamKit2.SteamApps;
using static SteamKit2.SteamApps.PICSProductInfoCallback;
using Timer = System.Timers.Timer;

namespace SchemaService.SteamUtils
{
    public static class SteamDownloaderFactory
    {
        public static void AddSteamDownloader(this IServiceCollection collection, SteamConfiguration config)
        {
            var categoryCleaner = new Regex("^[0-9a-f]+/");
            collection.AddSingleton(svc =>
            {
                var logFactory = svc.GetRequiredService<ILoggerFactory>();
                DebugLog.ClearListeners();
                DebugLog.AddListener((category, msg) =>
                {
                    try
                    {
                        category = categoryCleaner.Replace(category, "");
                        logFactory.CreateLogger("SteamKit2." + category).LogInformation("{Message}", msg);
                    }
                    catch
                    {
                        Console.WriteLine($"Failed to log {category}: {msg}");
                    }
                });
                DebugLog.Enabled = true;
                return new SteamClient(config);
            });
            collection.AddSingleton<CdnPool>();
            collection.AddSingleton<SteamDownloader>();
        }
    }

    public class SteamDownloader
    {
        private static readonly int MaxWorkers = Environment.ProcessorCount;

        private const string LockFile = DistFileCache.CacheDir + "\\lock";

        private readonly SteamClient _client;
        private readonly SteamUser _user;
        private readonly SteamApps _apps;
        private readonly SteamCloud _cloud;
        private readonly SteamContent _content;
        private readonly SteamUnifiedMessages _unifiedMessages;
        private readonly CallbackPump _callbacks;
        private readonly SteamUnifiedMessages.UnifiedService<IPublishedFile> _publishedFiles;
        private readonly SemaphoreSlim _downloadLock = new SemaphoreSlim(1);

        private LoggedOnCallback _loginDetails;

        private readonly ConcurrentDictionary<uint, byte[]> _depotKeys = new ConcurrentDictionary<uint, byte[]>();

        private readonly ConcurrentDictionary<uint, PICSProductInfo> _appInfos =
            new ConcurrentDictionary<uint, PICSProductInfo>();

        private readonly ILogger<SteamDownloader> _log;

        private bool IsLoggedIn => _loginDetails != null;
        public CdnPool CdnPool { get; }

        public SteamDownloader(ILogger<SteamDownloader> log, SteamClient client, CdnPool cdnPool)
        {
            _log = log;
            _client = client;
            _user = _client.GetHandler<SteamUser>();
            _apps = _client.GetHandler<SteamApps>();
            _cloud = _client.GetHandler<SteamCloud>();
            _content = _client.GetHandler<SteamContent>();
            _unifiedMessages = _client.GetHandler<SteamUnifiedMessages>();
            _publishedFiles = _unifiedMessages.CreateService<IPublishedFile>();
            CdnPool = cdnPool;


            _callbacks = new CallbackPump(_client);
            _callbacks.CallbackReceived += CallbacksOnCallbackReceived;
        }

        public async Task<byte[]> GetDepotKeyAsync(uint appId, uint depotId)
        {
            if (_depotKeys.TryGetValue(depotId, out var depotKey))
                return depotKey;

            var depotKeyResult = await _apps.GetDepotDecryptionKey(depotId, appId).ToTask();
            _depotKeys[depotId] = depotKeyResult.DepotKey;
            return depotKeyResult.DepotKey;
        }

        private async Task<PICSProductInfo> GetAppInfoAsync(uint appId)
        {
            if (_appInfos.TryGetValue(appId, out var appInfo))
                return appInfo;

            var productResult = await _apps.PICSGetProductInfo(new PICSRequest
            {
                ID = appId
            }, null);
            _appInfos[appId] = productResult.Results[0].Apps[appId];
            return _appInfos[appId];
        }

        private void CallbacksOnCallbackReceived(ICallbackMsg obj)
        {
            switch (obj)
            {
                case DisconnectedCallback discon:
                    if (!discon.UserInitiated)
                        OnDisconnect();
                    break;
            }
        }

        private async Task<ulong> GetManifestForBranch(uint appId, uint depotId, string branch, string branchPassword = null)
        {
            var appInfo = await GetAppInfoAsync(appId);
            var id = appInfo.GetManifestId(depotId, branch);
            if (id > 0)
                return id;
            var encryptedManifestId = appInfo.GetEncryptedManifestId(depotId, branch);
            if (branchPassword == null)
                throw new Exception($"No password provided for branch {branch}");
            var branchPasswords = await _apps.CheckAppBetaPassword(appId, branchPassword).ToTask();
            var key = branchPasswords.BetaPasswords[branch];
            if (key == null)
                throw new Exception($"Invalid password for branch {branch}");
            var manifestBytes = CryptoHelper.SymmetricDecryptECB(encryptedManifestId, key);
            return BitConverter.ToUInt64(manifestBytes, 0);
        }

        private async Task<DepotManifest> GetManifestAsync(uint appId, uint depotId, ulong manifestId, string branch)
        {
            if (!IsLoggedIn)
                throw new InvalidOperationException("The Steam client is not logged in.");

            var depotKey = await GetDepotKeyAsync(appId, depotId);
            var cdnClient = CdnPool.TakeClient();
            int attempts = 0;
            while (true)
            {
                var manifestRequestCode = await _content.GetManifestRequestCode(depotId, appId, manifestId, branch);
                var server = await CdnPool.TakeServer();
                try
                {
                    var manifest = await cdnClient.DownloadManifestAsync(depotId, manifestId, manifestRequestCode, server, depotKey);
                    CdnPool.ReturnServer(server);
                    return manifest;
                }
                catch (SteamKitWebRequestException)
                {
                    // ignore server errors + don't return it so the server isn't used again.
                    if (attempts++ > 5)
                        throw;
                }
            }
        }

        #region Auth

        /// <summary>
        /// Connect to Steam and log in with the given details, or anonymously if none are provided.
        /// </summary>
        /// <param name="details">User credentials.</param>
        /// <returns>Login details.</returns>
        /// <exception cref="Exception"></exception>
        public async Task<LoggedOnCallback> LoginAsync(LogOnDetails details = default)
        {
            if (_loginDetails != null)
                throw new InvalidOperationException("Already logged in.");

            _callbacks.Start();
            _client.Connect();

            var connectResult = await _callbacks
                .WaitForAsync(x => x is ConnectedCallback || x is DisconnectedCallback);

            if (connectResult is DisconnectedCallback)
                throw new Exception("Failed to connect to Steam.");

            if (details == null)
                _user.LogOnAnonymous();
            else
                _user.LogOn(details);

            var loginResult = await _callbacks.WaitForAsync<LoggedOnCallback>();
            if (loginResult.Result != EResult.OK)
                throw new Exception($"Failed to log into Steam: {loginResult.Result:G}");

            await CdnPool.Initialize((int)loginResult.CellID);
            _loginDetails = loginResult;
            return loginResult;
        }

        /// <summary>
        /// Log out the client and disconnect from Steam.
        /// </summary>
        public async Task LogoutAsync()
        {
            if (_loginDetails == null)
                return;

            _user.LogOff();
            _client.Disconnect();

            await _callbacks.WaitForAsync<DisconnectedCallback>();
            OnDisconnect();
        }

        private void OnDisconnect()
        {
            _callbacks.Stop();
            _loginDetails = null;

            _appInfos.Clear();
            _depotKeys.Clear();
        }

        #endregion

        public readonly struct InstallResult
        {
            public readonly HashSet<string> InstalledFiles;

            public InstallResult(HashSet<string> installedFiles)
            {
                InstalledFiles = installedFiles;
            }
        }

        public async Task<InstallResult> InstallAppAsync(uint appId, uint depotId, string branch, string installPath,
            Predicate<string> installFilter, string debugName, string branchPassword = null, string installPrefix = "")
        {
            var manifestId = await GetManifestForBranch(appId, depotId, branch, branchPassword);
            return await InstallInternalAsync(appId, depotId, manifestId, installPath, installFilter, debugName,
                branch, installPrefix);
        }

        private async Task<InstallResult> InstallInternalAsync(uint appId, uint depotId, ulong manifestId,
            string installPath, Predicate<string> installFilter, string debugName,
            string branch, string installPrefix)
        {
            var localCache = new DistFileCache();
            var localCacheFile = Path.Combine(installPath, DistFileCache.CacheDir, depotId.ToString());

            if (File.Exists(localCacheFile))
            {
                try
                {
                    using var fs = File.OpenRead(localCacheFile);
                    localCache = (DistFileCache)DistFileCache.Serializer.Deserialize(fs);
                }
                catch
                {
                    // ignored
                }
            }


            // Ensure local file cache contains up to date information for all files:
            if (File.Exists(installPath))
                foreach (var filePath in Directory.GetFiles(installPath, "*", SearchOption.AllDirectories)
                             .Where(x => !Directory.Exists(x))
                             .Select(x => x.Substring(installPath.Length).TrimStart('/', '\\'))
                             .Where(x => !x.StartsWith(DistFileCache.CacheDir) && installFilter(x)))
                {
                    if (!localCache.TryGet(filePath, out var metadata))
                        localCache.Add(metadata = new DistFileInfo { Path = filePath });
                    metadata.RepairData(installPath);
                }

            foreach (var file in localCache.Files)
                file.RepairData(installPath);

            Directory.CreateDirectory(installPath);
            Directory.CreateDirectory(Path.Combine(installPath, DistFileCache.CacheDir));

            var lockFile = Path.Combine(installPath, LockFile);
            var result = new InstallResult(new HashSet<string>());
            Stream lockFileHandle = null;
            try
            {
                try
                {
                    lockFileHandle = File.Create(lockFile);
                }
                catch (Exception err)
                {
                    throw new InvalidOperationException(
                        $"A job may already be in progress on this install ({debugName}).If you're sure there isn't one, delete {lockFile}",
                        err);
                }

                // Get installation details from Steam
                var manifest = await GetManifestAsync(appId, depotId, manifestId, branch);

                var job = InstallJob.Upgrade(_log, appId, depotId, installPath, localCache, manifest, installFilter, result.InstalledFiles, installPrefix);
                if (job.IsNoOp)
                {
                    _log.LogInformation("Installing {Name}, already up to date", debugName);
                    return result;
                }

                await _downloadLock.WaitAsync();
                try
                {
                    using var timer = new Timer(3000);
                    timer.AutoReset = true;
                    timer.Elapsed += (sender, args) => _log.LogInformation($"Installing {debugName} progress: {job.ProgressRatio:0.00%}");
                    timer.Start();
                    await job.Execute(this, MaxWorkers);
                }
                finally
                {
                    _downloadLock.Release();
                }


                using var fs = File.Create(localCacheFile);
                DistFileCache.Serializer.Serialize(fs, localCache);
            }
            finally
            {
                lockFileHandle?.Dispose();
            }

            return result;
        }

        public async Task<Dictionary<ulong, PublishedFileDetails>> LoadModDetails(uint appId, IEnumerable<ulong> modIds)
        {
            var req = new CPublishedFile_GetDetails_Request
                { appid = appId, includechildren = true, includemetadata = true };
            req.publishedfileids.AddRange(modIds);
            var response = await _publishedFiles.SendMessage(svc => svc.GetDetails(req));
            return response.GetDeserializedResponse<CPublishedFile_GetDetails_Response>().publishedfiledetails
                .ToDictionary(item => item.publishedfileid);
        }

        public async Task<CPublishedFile_GetItemInfo_Response.WorkshopItemInfo> InstallModAsync(uint appId, ulong modId, string installPath,
            Predicate<string> filter, string debugName)
        {
            var appInfo = await GetAppInfoAsync(appId);
            var workshopDepot = appInfo.GetWorkshopDepot();
            var req = new CPublishedFile_GetItemInfo_Request { appid = appId };
            req.workshop_items.Add(new CPublishedFile_GetItemInfo_Request.WorkshopItem { published_file_id = modId });
            var response = await _publishedFiles.SendMessage(svc => svc.GetItemInfo(req));
            var responseDecoded = response.GetDeserializedResponse<CPublishedFile_GetItemInfo_Response>();
            if (responseDecoded.private_items.Contains(modId))
                throw new InvalidOperationException($"Failed to latest publication of mod {modId} ({debugName}) -- it appears to be private");
            var result = responseDecoded.workshop_items
                .FirstOrDefault(x => x.published_file_id == modId);
            if (result == null)
                throw new InvalidOperationException($"Failed to latest publication of mod {modId} ({debugName})");

            await InstallInternalAsync(appId, workshopDepot, result.manifest_id, installPath, filter, debugName, null, "");
            return result;
        }
    }
}