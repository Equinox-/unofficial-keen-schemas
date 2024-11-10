using System;
using System.Collections.Generic;
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
                    _log.LogInformation($"Parsing page {source} for inclusion as {type}");
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
                        var elementDocs = xml.SelectNodes("//*[@data-xml-element]")!;
                        var attributeDocs = xml.SelectNodes("//*[@data-xml-attribute]")!;
                        if (elementDocs.Count == 0 && attributeDocs.Count == 0) return;

                        var typeInfo = configOut.TypePatch(type, true);
                        foreach (var element in elementDocs.OfType<XmlElement>())
                        {
                            var tag = element.GetAttribute("data-xml-element");
                            if (string.IsNullOrEmpty(tag)) continue;
                            var elementInfo = typeInfo.ElementPatch(tag, true);
                            BindDocs(elementInfo, element);
                        }

                        foreach (var attribute in attributeDocs.OfType<XmlElement>())
                        {
                            var tag = attribute.GetAttribute("data-xml-attribute");
                            if (string.IsNullOrEmpty(tag)) continue;
                            var attributeInfo = typeInfo.AttributePatch(tag, true);
                            BindDocs(attributeInfo, attribute);
                        }
                    }
                    catch (Exception err)
                    {
                        _log.LogWarning(err, $"Failed to parse page {source}");
                    }
                }
            });
            return configOut;

            void BindDocs(MemberPatch member, XmlElement root)
            {
                var content = root.SelectSingleNode("//*[@class = 'xml-doc-content']")?.InnerXml;
                if (string.IsNullOrEmpty(content)) return;
                member.Documentation = content.Replace("href=\"/", $"href=\"{wikiRoot}");
            }
        }
    }
}