using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Xml.Serialization;

namespace SchemaService.SteamUtils
{
    [XmlRoot("FileCache")]
    public sealed class DistFileCache
    {
        public const string CacheDir = ".sdcache";

        public static XmlSerializer Serializer = new XmlSerializer(typeof(DistFileCache));

        [XmlIgnore]
        private readonly Dictionary<string, DistFileInfo> _files = new Dictionary<string, DistFileInfo>();

        public void Remove(string path) => _files.Remove(path);

        public void Add(DistFileInfo file) => _files[file.Path] = file;

        public bool TryGet(string path, out DistFileInfo info) => _files.TryGetValue(path, out info);

        [XmlIgnore]
        public ICollection<DistFileInfo> Files => _files.Values;

        [XmlElement("File")]
        public DistFileInfo[] FilesSerialized
        {
            get => _files.Values.ToArray();
            set
            {
                _files.Clear();
                foreach (var file in value)
                    _files[file.Path] = file;
            }
        }
    }

    public sealed class DistFileInfo
    {
        private static readonly ThreadLocal<SHA1> Sha1 = new ThreadLocal<SHA1>(SHA1.Create);

        [XmlAttribute("Path")]
        public string Path { get; set; }

        [XmlAttribute("Hash")]
        public string HashString
        {
            get => Convert.ToBase64String(Hash);
            set => Hash = Convert.FromBase64String(value);
        }

        [XmlIgnore]
        public byte[] Hash { get; set; }

        [XmlAttribute("Size")]
        public long Size { get; set; }

        public void RepairData(string installPath)
        {
            var realPath = System.IO.Path.Combine(installPath, Path);
            if (!File.Exists(realPath))
            {
                Size = 0;
                Hash = Array.Empty<byte>();
                return;
            }

            var fileLength = new FileInfo(realPath).Length;
            if (Size == fileLength)
                return;
            using (var stream = File.OpenRead(realPath))
            {
                Hash = Sha1.Value.ComputeHash(stream);
                Size = fileLength;
            }
        }
    }
}