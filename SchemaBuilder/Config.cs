using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Serialization;

namespace SchemaBuilder
{
    public class ConfigBase<T> : IInheritable<T> where T : ConfigBase<T>
    {
        [XmlElement("Include")]
        public List<string> Include = new List<string>();

        [XmlElement]
        public Game? GameOptional;

        [XmlElement]
        public Game Game
        {
            get => GameOptional ?? throw new Exception("Schema does not specify a game");
            set => GameOptional = value;
        }

        [XmlElement]
        public string SteamBranch;

        [XmlElement("Mod")]
        public readonly HashSet<ulong> Mods = new HashSet<ulong>();

        [XmlElement("ExcludeMod")]
        public readonly HashSet<ulong> ExcludeMods = new HashSet<ulong>();

        public static readonly XmlSerializer Serializer = new XmlSerializer(typeof(T));

        public virtual void InheritFrom(T other)
        {
            GameOptional ??= other.GameOptional;
            if (GameOptional != null && other.GameOptional != null && Game != other.Game)
                throw new Exception($"Attempting to inherit from schema for {other.Game} but this schema is for {Game}");
            SteamBranch ??= other.SteamBranch;
            foreach (var exclude in other.ExcludeMods)
                if (!Mods.Contains(exclude))
                    ExcludeMods.Add(exclude);
            foreach (var mod in other.Mods)
                if (!ExcludeMods.Contains(mod))
                    Mods.Add(mod);
        }

        public static T Read(string dir, string name)
        {
            var context = new Stack<string>();
            var loadedFiles = new HashSet<string>();
            var loaded = new List<T>();

            ReadRecursive(name);

            var final = loaded[0];
            for (var i = 1; i < loaded.Count; i++)
                final.InheritFrom(loaded[i]);

            if (final.GameOptional == null)
                throw new Exception($"{typeof(T).Name} tree {name} does not specify a game ({string.Join(", ", loadedFiles)})");
            if (final.SteamBranch == null)
                throw new Exception($"{typeof(T).Name} tree {name} does not specify a steam branch ({string.Join(", ", loadedFiles)})");
            return final;

            void ReadRecursive(string file)
            {
                if (file.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                    file = file.Substring(0, file.Length - 4);
                if (!loadedFiles.Add(file))
                    return;

                context.Push(Path.GetFileNameWithoutExtension(file));
                try
                {
                    T cfg;
                    try
                    {
                        using var stream = File.OpenRead(Path.Combine(dir, file + ".xml"));
                        cfg = (T)Serializer.Deserialize(stream);
                    }
                    catch (Exception err)
                    {
                        throw new Exception($"Failed to load config file {file} via {string.Join(", ", context)}", err);
                    }

                    loaded.Add(cfg);
                    foreach (var included in cfg.Include)
                        ReadRecursive(included);
                }
                finally
                {
                    context.Pop();
                }
            }
        }
    }

    public enum Game
    {
        [XmlEnum("medieval-engineers")]
        MedievalEngineers,

        [XmlEnum("space-engineers")]
        SpaceEngineers,
    }

    public interface IInheritable<in T>
    {
        /// <summary>
        /// Updates this instance with default values from the other instance.
        /// This instance takes priority.
        /// </summary>
        void InheritFrom(T other);
    }
}