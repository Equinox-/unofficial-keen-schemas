using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using Microsoft.Extensions.Logging;

namespace SchemaBuilder
{
    public static class TaskAvoidance
    {
        public static void MaybeRun(
            ILogger log,
            string fingerprintFile,
            string taskName,
            Action action,
            string inputFile = null,
            string[] inputFiles = null,
            string outputFile = null,
            string[] outputFiles = null)
        {
            var previous = new Fingerprints(fingerprintFile).Read();
            var current = new Fingerprints(fingerprintFile);
            if (inputFile != null)
                current.UpdateFromDisk(inputFile, previous);
            if (inputFiles != null)
                foreach (var input in inputFiles)
                    current.UpdateFromDisk(input, previous);
            if (outputFile != null)
                current.UpdateFromDisk(outputFile, previous);
            if (outputFiles != null)
                foreach (var output in outputFiles)
                    current.UpdateFromDisk(output, previous);
            var differences = previous.Differences(current);
            if (differences.Count == 0)
            {
                log.LogInformation($"[Skip] {taskName}");
                return;
            }

            const int showCauses = 5;
            log.LogInformation(
                $"[Start] {taskName}{string.Join("", differences.Take(showCauses).Select(x => "\n      " + x))}{(differences.Count > showCauses ? $"\n      ... and {differences.Count - showCauses} more" : "")}");
            var time = Stopwatch.GetTimestamp();
            action();
            if (outputFile != null)
                current.UpdateFromDisk(outputFile);
            if (outputFiles != null)
                foreach (var output in outputFiles)
                    current.UpdateFromDisk(output);
            current.Write();
            log.LogInformation($"[Done] {taskName} in {(Stopwatch.GetTimestamp() - time) / (double)Stopwatch.Frequency:F4} s");
        }

        private sealed class Fingerprints
        {
            private readonly string _fingerprintParent;
            private readonly string _fingerprintFile;
            private readonly Dictionary<string, Fingerprint> _state;

            public Fingerprints(string file)
            {
                _fingerprintFile = Path.GetFullPath(file);
                _fingerprintParent = Path.GetDirectoryName(_fingerprintFile);
                _state = new Dictionary<string, Fingerprint>();
            }

            private string RelativeKey(string path) => MakeRelativePath(_fingerprintParent, Path.GetFullPath(path));

            public Fingerprint this[string path]
            {
                get => _state[RelativeKey(path)];
                set => _state[RelativeKey(path)] = value;
            }

            public void UpdateFromDisk(string path, Fingerprints hintFrom = null)
            {
                (hintFrom ?? this)._state.TryGetValue(RelativeKey(path), out var hint);
                this[path] = CachedFingerprint.Compute(path, hint);
            }

            public Fingerprints Read()
            {
                _state.Clear();
                if (!File.Exists(_fingerprintFile))
                    return this;
                foreach (var line in File.ReadAllLines(_fingerprintFile))
                {
                    var chunks = line.Split(new[] { ' ' }, 4);
                    if (chunks.Length != 4) continue;
                    if (long.TryParse(chunks[1], out var size) && long.TryParse(chunks[2], out var time))
                        _state[chunks[3]] = new Fingerprint(size, time, chunks[0]);
                }

                return this;
            }

            public List<string> Differences(Fingerprints other)
            {
                var changes = new List<string>();
                foreach (var kv in other._state)
                    if (!_state.TryGetValue(kv.Key, out var fingerprint) || !fingerprint.Equals(kv.Value))
                        changes.Add(Path.GetFullPath(Path.Combine(_fingerprintParent, kv.Key)));
                foreach (var key in _state.Keys)
                    if (!other._state.ContainsKey(key))
                        changes.Add(Path.GetFullPath(Path.Combine(_fingerprintParent, key)));
                return changes;
            }

            public void Write()
            {
                File.WriteAllLines(_fingerprintFile, _state.Select(x => $"{x.Value.Hash} {x.Value.Size} {x.Value.Time} {x.Key}"));
            }
        }

        private static readonly ThreadLocal<SHA1> Hasher = new ThreadLocal<SHA1>(SHA1.Create);


        private struct CachedFingerprint
        {
            private static readonly ConcurrentDictionary<string, CachedFingerprint> Cache = new ConcurrentDictionary<string, CachedFingerprint>();

            public static Fingerprint Compute(string path, Fingerprint hint) => Cache.AddOrUpdate(Path.GetFullPath(path), p =>
            {
                var value = new CachedFingerprint
                {
                    _fingerprint = hint
                };
                value.Update(p);
                return value;
            }, (p, value) =>
            {
                value.Update(p);
                return value;
            })._fingerprint;

            private Fingerprint _fingerprint;

            private void Update(string path)
            {
                var info = new FileInfo(path);
                if (!info.Exists)
                {
                    _fingerprint = new Fingerprint(0, 0, "missing");
                    return;
                }

                if (_fingerprint.Hash != null && _fingerprint.Size == info.Length && _fingerprint.Time == info.LastWriteTime.Ticks)
                    return;
                using var stream = File.OpenRead(path);
                _fingerprint = new Fingerprint(info.Length, info.LastWriteTime.Ticks, Convert.ToBase64String(Hasher.Value.ComputeHash(stream)));
            }
        }

        private readonly struct Fingerprint : IEquatable<Fingerprint>
        {
            public readonly long Size;
            public readonly long Time;
            public readonly string Hash;

            public Fingerprint(long size, long time, string hash)
            {
                Size = size;
                Time = time;
                Hash = hash;
            }

            public bool Equals(Fingerprint other) => Size == other.Size && Time == other.Time && Hash == other.Hash;

            public override bool Equals(object obj) => obj is Fingerprint other && Equals(other);

            public override int GetHashCode()
            {
                var hashCode = Size.GetHashCode();
                hashCode = (hashCode * 397) ^ Time.GetHashCode();
                hashCode = (hashCode * 397) ^ (Hash != null ? Hash.GetHashCode() : 0);
                return hashCode;
            }
        }

        /// <summary>
        /// Creates a relative path from one file or folder to another.
        /// </summary>
        /// <param name="fromPath">Contains the directory that defines the start of the relative path.</param>
        /// <param name="toPath">Contains the path that defines the endpoint of the relative path.</param>
        /// <returns>The relative path from the start directory to the end path or <c>toPath</c> if the paths are not related.</returns>
        /// <exception cref="ArgumentNullException"></exception>
        /// <exception cref="UriFormatException"></exception>
        /// <exception cref="InvalidOperationException"></exception>
        // https://stackoverflow.com/questions/275689/how-to-get-relative-path-from-absolute-path
        public static string MakeRelativePath(string fromPath, string toPath)
        {
            var fromUri = new Uri(fromPath);
            var toUri = new Uri(toPath);

            if (fromUri.Scheme != toUri.Scheme) return toPath;

            var relativeUri = fromUri.MakeRelativeUri(toUri);
            var relativePath = Uri.UnescapeDataString(relativeUri.ToString());

            if (toUri.Scheme.Equals("file", StringComparison.InvariantCultureIgnoreCase))
                relativePath = relativePath.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
            return relativePath;
        }
    }
}