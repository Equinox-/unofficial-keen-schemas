using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace DataExtractorShared
{
    public sealed class TemplateInvocation
    {
        private readonly string _name;

        public TemplateInvocation(string name) => _name = name;

        public readonly List<object> Unnamed = new List<object>();
        public readonly Dictionary<string, object> Named = new Dictionary<string, object>();

        public object this[string key]
        {
            get => Named[key];
            set => Named[key] = value;
        }

        public override string ToString()
        {
            var newLine = Unnamed.Count + Named.Count >= 4 ? Environment.NewLine : "";
            var pipe = newLine.Length == 0 ? " | " : "  | ";
            var sb = new StringBuilder("{{").Append(_name).Append(newLine);
            foreach (var pos in Unnamed)
                if (pos != null)
                    sb.Append(pipe).Append(PageWriter.ToSafeString(pos)).Append(newLine);
            foreach (var named in Named)
                if (named.Value != null)
                    sb.Append(pipe).Append(named.Key).Append(" = ").Append(PageWriter.ToSafeString(named.Value)).Append(newLine);
            return sb.Append("}}").ToString();
        }
    }

    public sealed class DataWriter
    {
        private readonly string _dir;
        private readonly HashSet<string> _pages = new HashSet<string>();

        public DataWriter(string dir)
        {
            _dir = dir;
        }

        private static string EscapeFileName(string name)
        {
            var dest = new StringBuilder((int)(name.Length * 1.5));
            var invalidChars = new string(Path.GetInvalidFileNameChars());
            foreach (var ch in name)
            {
                if (ch == '_' || invalidChars.IndexOf(ch) >= 0) dest.Append("_").Append(((ushort)ch).ToString("X4"));
                else dest.Append(ch);                
            }
            return dest.ToString();
        }
        
        public PageWriter CreatePage(string name)
        {
            if (!_pages.Add(name))
                throw new ArgumentException($"Page already created: {name}");
            return new PageWriter(Path.Combine(_dir, EscapeFileName(name)));
        }
    }

    public sealed class PageWriter : IDisposable
    {
        // https://www.mediawiki.org/wiki/Template:Escape_template_list
        private static readonly Dictionary<string, string> EscapeReplacements = new Dictionary<string, string>
        {
            ["|"] = "&#124;",
            ["="] = "&#61;",
            ["||"] = "&#124;&#124;",
            ["["] = "&#91;",
            ["]"] = "&#93;",
            ["{"] = "&#123;",
            ["}"] = "&#125;",
            ["<"] = "&lt;",
            [">"] = "&gt;"
        };

        private readonly TextWriter _writer;
        internal PageWriter(string path) => _writer = new StreamWriter(File.Open(path, FileMode.Create, FileAccess.Write));

        public bool? HideNewLines { get; set; }
        
        public void Write(object value)
        {
            var hideNewLines = HideNewLines ?? (HideNewLines = value is TemplateInvocation).Value;
            _writer.Write(ToSafeString(value));
            if (hideNewLines)
                _writer.Write("<!--");
            _writer.WriteLine();
            if (hideNewLines)
                _writer.Write("-->");
        }

        public void WriteComment(string value) => _writer.WriteLine("<!-- " + value + " -->");

        internal static string ToSafeString(object value)
        {
            switch (value)
            {
                case TemplateInvocation _:
                case float _:
                case double _:
                case byte _:
                case sbyte _:
                case ushort _:
                case short _:
                case uint _:
                case int _:
                case ulong _:
                case long _:
                case decimal _:
                    return value.ToString();
                case bool boolean:
                    return boolean ? "1" : "0";
                case string str:
                    var escaped = str;
                    foreach (var item in EscapeReplacements)
                        escaped = escaped.Replace(item.Key, item.Value);
                    return escaped;
                default:
                    throw new ArgumentException($"Value is not a supported type {value?.GetType()}");
            }
        }

        public void Dispose() => _writer.Dispose();
    }
}