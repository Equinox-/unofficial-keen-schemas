using DataExtractorShared;
using Medieval.Definitions.Block;
using VRage.Definitions.Block;
using VRage.Game;

namespace DataExtractorMedieval
{
    public class BlockDataExtractor : IDataExtractor
    {
        public void Run(DataWriter writer)
        {
            foreach (var def in MyDefinitionManager.GetOfType<MyBlockVariantsDefinition>())
            {
                writer.Write(new TemplateInvocation("BlockVariant")
                {
                    ["Subtype"] = def.Id.SubtypeName,
                    ["Name"] = def.DisplayNameText
                });
                foreach (var block in def.Blocks)
                    if (!(block is MyGeneratedBlockDefinition))
                        writer.Write(new TemplateInvocation("BlockVariant")
                        {
                            ["VariantSubtype"] = def.Id.SubtypeName,
                            ["BlockSubtype"] = block.Id.SubtypeName
                        });
            }

            foreach (var def in MyDefinitionManager.GetOfType<MyBlockDefinition>())
                if (!(def is MyGeneratedBlockDefinition))
                    writer.Write(new TemplateInvocation("Block")
                    {
                        ["Subtype"] = def.Id.SubtypeName,
                        ["Name"] = def.DisplayNameText,
                        ["Mass"] = def.Mass
                    });
        }
    }
}