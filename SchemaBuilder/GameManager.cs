using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SchemaService.SteamUtils;

namespace SchemaBuilder
{
    public class GameManager : IHostedService
    {
        private const bool Skip = false;

        private readonly SteamDownloader _steam;
        private readonly ILogger<GameManager> _log;

        private readonly string _rootDir;

        public GameManager(SteamDownloader steam, ILogger<GameManager> log)
        {
            _steam = steam;
            _log = log;
            _rootDir = Path.GetFullPath("./");
        }

        private const string BinariesDir = "DedicatedServer64";

        public async Task<string> RestoreGame(Game game, string branch)
        {
            var info = GameInfo.Games[game];
            branch ??= info.SteamBranch;
            _log.LogInformation($"Installing game {game}, branch {branch}");
            var installDir = Path.Combine(_rootDir, "game", game.ToString(), branch);
            if (Skip)
                return installDir;
            await _steam.InstallAppAsync(info.SteamDedicatedAppId, info.SteamDedicatedDepotId, branch, installDir, 4,
                path => path.StartsWith(BinariesDir), game.ToString());

            return Path.Combine(installDir, BinariesDir);
        }

        public Task StartAsync(CancellationToken cancellationToken) => Skip ? Task.CompletedTask : _steam.LoginAsync();

        public Task StopAsync(CancellationToken cancellationToken) => Skip ? Task.CompletedTask : _steam.LogoutAsync();
    }
}