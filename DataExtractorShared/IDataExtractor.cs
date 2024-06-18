using System;
using System.Linq;

namespace DataExtractorShared
{
    public interface IDataExtractor
    {
        void Run(DataWriter writer);
    }

    public static class DataExtractor
    {
        public static void RunAll(Action<string> writeLine)
        {
            var writer = new DataWriter(writeLine);
            foreach (var type in typeof(DataExtractor).Assembly.GetTypes().Where(type => typeof(IDataExtractor).IsAssignableFrom(type) && !type.IsAbstract))
                ((IDataExtractor)Activator.CreateInstance(type)).Run(writer);
        }
    }
}