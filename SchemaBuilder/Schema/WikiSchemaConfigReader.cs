using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using WikiClientLibrary.Client;
using WikiClientLibrary.Generators;

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
                    using var itr = new TranscludedInGenerator(site, pageConfig.RegexFromTemplate)
                        {
                            NamespaceIds = new[] { 0 }
                        }
                        .EnumItemsAsync()
                        .GetEnumerator();
                    while (await itr.MoveNext())
                    {
                        var page = itr.Current.Title;
                        var match = sourceRegex.Match(page);
                        if (!match.Success) continue;
                        var type = match.Result(pageConfig.Type);
                        var cleanType = InvalidTypeCharacters.Replace(type, "");
                        await ParseDocPage(page, cleanType);
                    }
                }

                return;

                async Task ParseDocPage(string source, string type)
                {
                    var types = new HashSet<string>();
                    try
                    {
                        const string prop = "text";
                        var body = await site.InvokeMediaWikiApiAsync(new MediaWikiFormRequestMessage(new Dictionary<string, string>
                        {
                            ["action"] = "parse",
                            ["format"] = "json",
                            ["page"] = source,
                            ["prop"] = prop
                        }), default);
                        var xml = new XmlDocument();
                        xml.LoadXml(body.Value<JToken>("parse").Value<JToken>(prop).Value<string>("*")!);
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
                if (typeCfg.Name == "MyObjectBuilder_AmmoDefinition") Debugger.Break();
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
                if (docs != null && CleanDocs(docs))
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

                bool CleanDocs(XmlNode node)
                {
                    if (node is XmlText text)
                    {
                        if (string.IsNullOrEmpty(text.Value)) return false;
                        var str = text.Value.Trim();
                        return !str.Equals("This type hosts other elements:", StringComparison.OrdinalIgnoreCase) &&
                               !str.Equals("Unused or obsolete elements", StringComparison.OrdinalIgnoreCase);
                    }

                    var anyValid = false;
                    foreach (var child in node.OfType<XmlNode>().ToList())
                    {
                        if (CleanDocs(child))
                            anyValid = true;
                        else if (child is XmlElement element && element.GetAttribute("class") == "w")
                            ; // Don't remove whitespace spans.
                        else
                            node.RemoveChild(child);
                    }

                    return anyValid;
                }
            }
        }
    }
}