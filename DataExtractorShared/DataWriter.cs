using System;
using System.Collections.Generic;
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
                    sb.Append(pipe).Append(DataWriter.ToSafeString(pos)).Append(newLine);
            foreach (var named in Named)
                if (named.Value != null)
                    sb.Append(pipe).Append(named.Key).Append(" = ").Append(DataWriter.ToSafeString(named.Value)).Append(newLine);
            return sb.Append("}}").ToString();
        }
    }

    public sealed class DataWriter
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

        private readonly Action<string> _writeLine;
        public DataWriter(Action<string> writeLine) => _writeLine = writeLine;

        public void Write(object value) => _writeLine(ToSafeString(value));

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
                case string str:
                    var escaped = str;
                    foreach (var item in EscapeReplacements)
                        escaped = escaped.Replace(item.Key, item.Value);
                    return escaped;
                default:
                    throw new ArgumentException($"Value is not a supported type {value?.GetType()}");
            }
        }
    }
}