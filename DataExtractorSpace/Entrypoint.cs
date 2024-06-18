using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using System.Text;
using System.Threading;
using DataExtractorShared;
using Havok;
using ParallelTasks;
using Sandbox;
using Sandbox.Definitions;
using Sandbox.Engine.Utils;
using Sandbox.Game;
using SpaceEngineers.Game;
using VRage;
using VRage.FileSystem;
using VRage.Game;
using VRage.Game.Localization;
using VRage.Game.ObjectBuilder;
using VRage.GameServices;
using VRage.Platform.Windows;
using VRage.Plugins;
using VRage.Utils;
using VRageRender;
using Game = Sandbox.Engine.Platform.Game;

namespace DataExtractorSpace
{
    public static class Entrypoint
    {
        public static void Main(string[] args)
        {
            Run(Console.WriteLine, args[0], Array.Empty<Tuple<ulong, string>>());
        }

        // ReSharper disable once MemberCanBePrivate.Global
        public static void Run(Action<string> writeLine, string gameContentDir, Tuple<ulong, string>[] mods)
        {
            var log = new MyLog();
            try
            {
                SpaceEngineersGame.SetupBasicGameInfo();
                SpaceEngineersGame.SetupPerGameSettings();
                MyLog.Default = log;
                MyFileSystem.Init(gameContentDir, "temp");
                MyFileSystem.ExePath = Path.Combine(gameContentDir, "..", "DedicatedServer64");
                MyLog.Default.Init("temp/data-extractor-space.log", new StringBuilder());
                MyVRageWindows.Init("DataExtractorSpace", log, "./", false);
                Parallel.Scheduler = new FixedPriorityScheduler(Environment.ProcessorCount, ThreadPriority.Normal);
                MyLanguage.Instance.Init();
                MyRenderProxy.Initialize(new MyNullRender());
                Game.IsDedicated = true;
                MySandboxGame.Static = (MySandboxGame)FormatterServices.GetUninitializedObject(typeof(MySandboxGame));

                HkBaseSystem.Init(
                    line => MyLog.Default.WriteLine(line), false,
                    MyVRage.Platform.System.CreateSharedCriticalSection(false));
                MyPlugins.RegisterGameAssemblyFile(MyPerGameSettings.GameModAssembly);
                MyPlugins.RegisterGameObjectBuildersAssemblyFile(MyPerGameSettings.GameModObjBuildersAssembly);
                MyPlugins.RegisterSandboxAssemblyFile(MyPerGameSettings.SandboxAssembly);
                MyPlugins.RegisterSandboxGameAssemblyFile(MyPerGameSettings.SandboxGameAssembly);
                MyPlugins.Load();
                MyGlobalTypeMetadata.Static.Init(true);

                var definitions = MyDefinitionManager.Static;
                var modItems = mods.Select(mod =>
                {
                    var modItem = new MyObjectBuilder_Checkpoint.ModItem(
                        mod.Item1,
                        mod.Item1.ToString(),
                        false);
                    modItem.SetModData(new HelperWorkshopItem(mod.Item1, mod.Item2));
                    return modItem;
                }).ToList();

                definitions.LoadData(modItems);

                DataExtractor.RunAll(writeLine);
            }
            finally
            {
                log.Flush();
            }
        }

        private sealed class HelperWorkshopItem : MyWorkshopItem
        {
            public HelperWorkshopItem(ulong id, string path)
            {
                Metadata = new MyModMetadata();
                Id = id;
                State = MyWorkshopItemState.Installed;
                Title = id.ToString();
                Visibility = MyPublishedFileVisibility.Private;
                Folder = path;
            }
        }
    }
}