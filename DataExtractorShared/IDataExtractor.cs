using System;
using System.Linq;

namespace DataExtractorShared
{
    public interface IDataExtractor
    {
        string PageName { get; }
        void Run(PageWriter writer);
    }

    public interface IPartitionedDataExtractor
    {
        int Partitions { get; }
        string PageName { get; }
        void Run(Func<object, PageWriter> writers);
    }


    public static class DataExtractor
    {
        public static void RunAll(DataWriter writer)
        {
            var pagePrefix = Environment.GetEnvironmentVariable("PAGE_PREFIX") ?? "";
            var types = typeof(DataExtractor).Assembly.GetTypes();
            foreach (var type in types.Where(type => typeof(IDataExtractor).IsAssignableFrom(type) && !type.IsAbstract))
            {
                var instance = (IDataExtractor)Activator.CreateInstance(type);
                using (var page = writer.CreatePage($"{pagePrefix}{instance.PageName}"))
                {
                    instance.Run(page);
                }
            }

            if (!double.TryParse(Environment.GetEnvironmentVariable("PARTITION_MULTIPLIER"), out var partitionMultiplier))
                partitionMultiplier = 1;
            foreach (var type in types.Where(type => typeof(IPartitionedDataExtractor).IsAssignableFrom(type) && !type.IsAbstract))
            {
                var instance = (IPartitionedDataExtractor)Activator.CreateInstance(type);
                var partitions = (int) (instance.Partitions * partitionMultiplier);
                var pages = new PageWriter[partitions];
                try
                {
                    for (var i = 0; i < partitions; i++)
                        pages[i] = writer.CreatePage($"{pagePrefix}{instance.PageName}/Partition{i + 1}");
                    instance.Run(key => pages[(key.GetHashCode() % partitions + partitions) % partitions]);
                }
                finally
                {
                    foreach (var page in pages)
                        page?.Dispose();
                }
            }
        }
    }
}