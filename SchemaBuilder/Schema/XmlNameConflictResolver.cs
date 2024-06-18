using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml.Serialization;
using Microsoft.Extensions.Logging;

namespace SchemaBuilder.Schema
{
    public static class XmlNameConflictResolver
    {
        public static void ResolveConflicts(ILogger log, XmlInfo resolver)
        {
            var nameConflicts = new Dictionary<string, List<XmlTypeInfo>>();
            foreach (var type in resolver.AllTypes)
            {
                var baseTypeName = type.OriginalXmlName;
                if (!nameConflicts.TryGetValue(baseTypeName, out var conflicts))
                    nameConflicts.Add(baseTypeName, conflicts = new List<XmlTypeInfo>());
                conflicts.Add(type);
            }

            foreach (var conflict in nameConflicts)
            {
                if (conflict.Value.Count <= 1) continue;
                var groups = new Dictionary<PathSegment, List<(PathSegment path, XmlTypeInfo type)>>();
                foreach (var type in conflict.Value)
                {
                    var path = new PathSegment(TypePath(type));
                    groups.GetCollection(path.LastN(2)).Add((path, type));
                }

                var resolution = new StringBuilder();
                while (groups.Count > 0)
                {
                    var copy = groups.ToArray();
                    groups.Clear();
                    foreach (var entry in copy)
                    {
                        if (entry.Value.Count == 1)
                        {
                            var type = entry.Value[0].type;
                            var attrs = type.Attributes;
                            attrs.XmlType ??= new XmlTypeAttribute();
                            attrs.XmlType.TypeName = entry.Key.Join("_");
                            resolution.Append("\n        ").Append(attrs.XmlType.TypeName).Append(" -> ").Append(type.Type.FullName);
                            continue;
                        }

                        foreach (var type in entry.Value)
                            groups.GetCollection(type.path.LastN(entry.Key.Count + 1)).Add(type);
                    }
                }

                log.LogInformation($"Resolving conflict of name {conflict.Key} as:{resolution}");
            }
        }

        private static List<TV> GetCollection<TK, TV>(this Dictionary<TK, List<TV>> dict, TK key)
        {
            if (dict.TryGetValue(key, out var val))
                return val;
            dict.Add(key, val = new List<TV>());
            return val;
        }

        private static string[] TypePath(XmlTypeInfo xmlType)
        {
            var path = new List<string> { xmlType.OriginalXmlName };
            var type = xmlType.Type;
            while (type.DeclaringType != null)
            {
                type = type.DeclaringType;
                path.Add(type.Name);
            }

            path.Reverse();
            if (type.Namespace != null)
                path.InsertRange(0, type.Namespace.Split('.'));
            path.Insert(0, type.Assembly.GetName().Name);
            return path.ToArray();
        }

        private readonly struct PathSegment : IEquatable<PathSegment>
        {
            public readonly string[] Path;
            public readonly int Offset;
            public readonly int Count;

            public PathSegment(string[] path)
            {
                Path = path;
                Offset = 0;
                Count = path.Length;
            }

            public PathSegment(string[] path, int offset, int count)
            {
                Path = path;
                Offset = offset;
                Count = count;
            }

            public string Join(string sep)
            {
                var sb = new StringBuilder();
                sb.Append(Path[Offset]);
                for (var i = 1; i < Count; i++)
                    sb.Append(sep).Append(Path[Offset + i]);
                return sb.ToString();
            }

            public PathSegment LastN(int n) => new PathSegment(Path, Offset + Count - n, n);

            private Span<string> Span => Path.AsSpan(Offset, Count);

            public bool Equals(PathSegment other) => Span.SequenceEqual(other.Span);

            public override bool Equals(object obj) => obj is PathSegment other && Equals(other);

            public override int GetHashCode()
            {
                var hashCode = 0;
                foreach (var item in Span)
                    hashCode = (hashCode * 397) ^ item.GetHashCode();
                return hashCode;
            }
        }
    }
}