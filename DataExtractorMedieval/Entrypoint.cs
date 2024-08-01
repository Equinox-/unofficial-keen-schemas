using System;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using DataExtractorShared;
using Havok;
using Sandbox.Definitions;
using Sandbox.Game.Localization;
using VRage.FileSystem;
using VRage.Game;
using VRage.Input;
using VRage.Logging;
using VRage.Meta;
using VRage.ParallelWorkers;
using VRage.ParallelWorkers.Work;
using VRage.Systems;
using VRageRender;

namespace DataExtractorMedieval
{
    public static class Entrypoint
    {
        public static void Main(string[] args)
        {
            Run(args[0], args[1], Array.Empty<Tuple<ulong, string>>());
        }

        // ReSharper disable once MemberCanBePrivate.Global
        public static void Run(string outputDirectory, string gameContentDir, Tuple<ulong, string>[] mods)
        {
            using (var log = new MyLog())
            {
                MyLog.Default = log;
                MyFileSystem.Init(gameContentDir, "temp");
                MyLanguage.Init();
                MyRenderProxy.Initialize(new MyNullRender());
                MyLog.Default.Init("temp/data-extractor-medieval.log", new StringBuilder());
                Workers.Init(new WorkerConfigurationFactory()
                    .AddGroup(new WorkerConfigurationFactory.Group
                    {
                        Id = WorkerGroup.Background,
                        Min = 1,
                        Priority = ThreadPriority.BelowNormal,
                        Ratio = .1f
                    })
                    .AddGroup(new WorkerConfigurationFactory.Group
                    {
                        Id = WorkerGroup.Logic,
                        Min = 1,
                        Priority = ThreadPriority.Normal,
                        Ratio = .7f
                    })
                    .AddGroup(new WorkerConfigurationFactory.Group
                    {
                        Id = WorkerGroup.Render,
                        Min = 1,
                        Priority = ThreadPriority.AboveNormal,
                        Ratio = .2f
                    })
                    .SetDefault(WorkerGroup.Logic)
                    .Bake(32));

                MyMetadataSystem.LoadAssemblies(new[]
                {
                    "VRage",
                    "VRage.Game",
                    "Sandbox.Graphics",
                    "Sandbox.Game",
                    "MedievalEngineers.ObjectBuilders",
                    "MedievalEngineers.Game"
                }.Select(Assembly.Load));

                HkBaseSystem.Init(MyLog.DefaultLogger);
                var workId = Workers.Manager.EnqueueWorkAll(ActionWork.Get(() => HkBaseSystem.InitThread(Thread.CurrentThread.Name)));
                Workers.Manager.WaitOn(workId);

                MyFileSystem.SetAdditionalContentPaths(mods.Select(x => x.Item2).ToList());
                MyGameInput.Init(true);

                var definitions = MyDefinitionManagerSandbox.Static;
                definitions.LoadData(mods
                    .Select(mod => new MyModContext(
                        mod.Item1.ToString(),
                        mod.Item1.ToString(),
                        mod.Item2))
                    .ToList());
                definitions.InitDefinitions();

                DataExtractor.RunAll(new DataWriter(outputDirectory));
            }
        }
    }
}