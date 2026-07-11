using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using WikiClientLibrary.Client;
using WikiClientLibrary.Generators.Primitive;
using WikiClientLibrary.Infrastructures;
using WikiClientLibrary.Pages;
using WikiClientLibrary.Sites;

namespace SchemaBuilder.Schema
{
    public class WikiSchemaConfigReader
    {
        private static readonly Regex InvalidTypeCharacters = new Regex("[^a-zA-Z0-9_]+");

        private readonly WikiClientFactory _clientFactory;
        private readonly ILogger<WikiSchemaConfigReader> _log;

        public WikiSchemaConfigReader(ILogger<WikiSchemaConfigReader> log, WikiClientFactory clientFactory)
        {
            _clientFactory = clientFactory;
            _log = log;
        }

        private const string AttrXmlType = "data-xml-type";
        private static readonly string XPathTypeTable = $".//*[@{AttrXmlType}]";
        private const string AttrXmlElement = "data-xml-element";
        private const string AttrXmlAttribute = "data-xml-attribute";
        private const string ClassXmlDocs = "xml-doc-content";

        public async Task<SchemaConfig> Read(SchemaConfigFromWiki cfg)
        {
            var configOut = new SchemaConfig();
            var wikiRoot = new Uri(new Uri(cfg.Api), "/");
            var cacheDir = Path.Combine(Path.GetFullPath("."), "wiki", new Uri(cfg.Api).Host);
            Directory.CreateDirectory(cacheDir);
            await _clientFactory.WithClient(cfg.Api, async site =>
            {
                foreach (var pageConfig in cfg.Pages)
                {
                    if (string.IsNullOrEmpty(pageConfig.RegexFromTemplate))
                    {
                        await ParseDocPage(pageConfig.Source, pageConfig.Type);
                        continue;
                    }

                    var sourceRegex = new Regex(pageConfig.Source);
                    var pages = await new TranscludedWithRevisionGenerator(site)
                        {
                            TargetTitle = pageConfig.RegexFromTemplate,
                            NamespaceIds = new[] { 0 },
                            PaginationSize = 100,
                        }
                        .EnumItemsAsync()
                        .ToList();
                    for (var i = 0; i < pages.Count; i++)
                    {
                        var page = pages[i];
                        var match = sourceRegex.Match(page.Stub.Title);
                        if (!match.Success) continue;
                        var type = match.Result(pageConfig.Type);
                        var cleanType = InvalidTypeCharacters.Replace(type, "");
                        await ParseDocPage(page.Stub.Title, cleanType, page.Stub.Id, page.Revision);
                        _log.LogInformation($"Parsed page {i + 1}/{pages.Count}");
                    }
                }

                return;

                async Task<(JToken, bool)> MaybeCachedLoad(string page, int id, string timestamp)
                {
                    if (id == 0 || timestamp == null)
                        return (await Load(), false);
                    var timeFile = Path.Combine(cacheDir, id + ".time");
                    var contentFile = Path.Combine(cacheDir, id + ".json");
                    if (File.Exists(timeFile) && File.Exists(contentFile))
                    {
                        try
                        {
                            if (File.ReadAllText(timeFile) == timestamp)
                                return (JToken.Parse(File.ReadAllText(contentFile)), true);
                        }
                        catch (Exception err)
                        {
                            _log.LogInformation($"Failed to read cache files for page {page}: {err}");
                        }
                    }

                    var result = await Load();
                    File.WriteAllText(contentFile, result.ToString());
                    File.WriteAllText(timeFile, timestamp);
                    return (result, false);

                    async Task<JToken> Load()
                    {
                        await Task.Delay(TimeSpan.FromSeconds(5));
                        return await site.InvokeMediaWikiApiAsync(new MediaWikiFormRequestMessage(new Dictionary<string, string>
                        {
                            ["action"] = "parse",
                            ["format"] = "json",
                            ["page"] = page,
                            ["prop"] = "text",
                        }), CancellationToken.None);
                    }
                }

                async Task ParseDocPage(string source, string type, int id = 0, string timestamp = null)
                {
                    var types = new HashSet<string>();
                    try
                    {
                        var (body, cached) = await MaybeCachedLoad(source, id, timestamp);
                        if (cached)
                            _log.LogInformation($"Cache hit for page {source} timestamp {timestamp}");
                        var xml = new XmlDocument { PreserveWhitespace = true };
                        xml.LoadXml(body.Value<JToken>("parse").Value<JToken>("text").Value<string>("*")!);
                        var typeTables = xml.SelectNodes(XPathTypeTable)!;
                        foreach (var typeTable in typeTables.OfType<XmlElement>())
                        {
                            var typeName = typeTable.GetAttribute(AttrXmlType);
                            if (string.IsNullOrEmpty(typeName))
                                typeName = type;
                            types.Add(typeName);
                            BindType(configOut.TypePatch(typeName, true), typeTable);
                        }

                        _log.LogInformation($"Parsed page {source}, found types {string.Join(", ", types)}");
                    }
                    catch (Exception err)
                    {
                        _log.LogWarning(err, $"Failed to parse page {source}");
                    }
                }
            });
            return configOut;

            void BindType(TypePatch typeCfg, XmlElement typeXml)
            {
                Walk(typeXml);
                return;

                void Walk(XmlElement xml)
                {
                    var elementName = xml.GetAttribute(AttrXmlElement);
                    if (!string.IsNullOrEmpty(elementName))
                    {
                        var elementInfo = typeCfg.ElementPatch(elementName, true);
                        BindDocs(elementInfo, xml);
                        return;
                    }

                    var attributeName = xml.GetAttribute(AttrXmlAttribute);
                    if (!string.IsNullOrEmpty(attributeName))
                    {
                        var attributeInfo = typeCfg.AttributePatch(attributeName, true);
                        BindDocs(attributeInfo, xml);
                        return;
                    }

                    foreach (var child in xml.OfType<XmlElement>())
                        Walk(child);
                }
            }

            void BindDocs(MemberPatch member, XmlElement root)
            {
                root = (XmlElement)root.CloneNode(true);

                // Remove nested type tables, they are handled intrinsically in the schema.
                foreach (var nested in root.SelectNodes(XPathTypeTable)!.OfType<XmlNode>().ToList())
                    nested.ParentNode!.RemoveChild(nested);

                var docs = root.SelectSingleNode($".//*[contains(@class, '{ClassXmlDocs}')]");
                if (docs != null && CleanDocs(docs) == CleanWhitespaceResult.HasContent)
                    member.Documentation = InlineCss(docs).InnerXml
                        .Replace("href=\"/", $"href=\"{wikiRoot}")
                        .Trim();

                return;

                XmlNode InlineCss(XmlNode node)
                {
                    foreach (var rule in cfg.CssInlines)
                    foreach (var match in node.SelectNodes(rule.XPath)!.OfType<XmlElement>())
                    {
                        var style = match.GetAttribute("style");
                        match.SetAttribute("style", style + rule.Style);
                    }

                    return node;
                }

                CleanWhitespaceResult CleanDocs(XmlNode node)
                {
                    switch (node)
                    {
                        case XmlText text when string.IsNullOrEmpty(text.Value):
                            return CleanWhitespaceResult.None;
                        case XmlText text:
                        {
                            var str = text.Value.Trim();
                            if (str == "")
                                return CleanWhitespaceResult.HasSignificantWhitespace;
                            if (str.Equals("This type hosts other elements:", StringComparison.OrdinalIgnoreCase))
                                return CleanWhitespaceResult.None;
                            // ReSharper disable once ConvertIfStatementToReturnStatement
                            if (str.Equals("Unused or obsolete elements", StringComparison.OrdinalIgnoreCase))
                                return CleanWhitespaceResult.None;
                            return CleanWhitespaceResult.HasContent;
                        }
                        case XmlWhitespace ws when ws.Value.Length > 0:
                        case XmlSignificantWhitespace sws when sws.Value.Length > 0:
                            return CleanWhitespaceResult.HasSignificantWhitespace;
                    }

                    var mostSignificant = CleanWhitespaceResult.None;
                    foreach (var child in node.OfType<XmlNode>().ToList())
                    {
                        var childResult = CleanDocs(child);
                        if (childResult == CleanWhitespaceResult.None) node.RemoveChild(child);
                        if (childResult > mostSignificant) mostSignificant = childResult;
                    }

                    if (mostSignificant == CleanWhitespaceResult.None && node is XmlElement element &&
                        (element.Name == "p" || element.Name == "br" || element.GetAttribute("class") == "w"))
                        mostSignificant = CleanWhitespaceResult.HasSignificantWhitespace;

                    return mostSignificant;
                }
            }
        }

        private struct WikiPageWithRevision
        {
            public WikiPageStub Stub;
            public string Revision;
        }

        private sealed class TranscludedWithRevisionGenerator : WikiPageGenerator<WikiPageWithRevision>
        {
            public TranscludedWithRevisionGenerator(WikiSite site) : base(site)
            {
            }

            public string TargetTitle { get; set; }
            public IEnumerable<int> NamespaceIds { get; set; }

            public override IEnumerable<KeyValuePair<string, object>> EnumListParameters() => new Dictionary<string, object>
            {
                { "prop", "revisions" },
                { "generator", "embeddedin" },
                { "rvprop", "timestamp" },
                { "geititle", TargetTitle },
                { "geinamespace", NamespaceIds == null ? null : MediaWikiHelper.JoinValues(NamespaceIds) },
                { "geilimit", PaginationSize },
            };

            protected override WikiPageWithRevision ItemFromJson(JToken json)
            {
                var data = json.First;
                return new WikiPageWithRevision
                {
                    Stub = new WikiPageStub((int)data["pageid"], (string)data["title"], (int)data["ns"]),
                    Revision = (string)data["revisions"]?[0]?["timestamp"],
                };
            }

            public override string ListName => "pages";
        }

        enum CleanWhitespaceResult
        {
            None,
            HasSignificantWhitespace,
            HasContent,
        }
    }
}