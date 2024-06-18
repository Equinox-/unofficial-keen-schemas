using DataExtractorShared;
using Sandbox.Definitions;

namespace DataExtractorSpace
{
    public class BlockDataExtractor : IDataExtractor
    {
        public void Run(DataWriter writer)
        {
            foreach (var def in MyDefinitionManager.Static.GetBlockVariantGroupDefinitions().Values)
            {
                writer.Write(new TemplateInvocation("BlockVariant")
                {
                    ["Subtype"] = def.Id.SubtypeName,
                    ["Name"] = def.DisplayNameText
                });
                foreach (var block in def.Blocks)
                    writer.Write(new TemplateInvocation("BlockBlockVariant")
                    {
                        ["VariantSubtype"] = def.Id.SubtypeName,
                        ["BlockSubtype"] = block.Id.SubtypeName
                    });
            }

            foreach (var def in MyDefinitionManager.Static.GetDefinitionsOfType<MyCubeBlockDefinition>())
                writer.Write(new TemplateInvocation("Block")
                {
                    ["Subtype"] = def.Id.SubtypeName,
                    ["Name"] = def.DisplayNameText,
                    ["Mass"] = def.Mass
                });
        }
    }
}