using System;
using System.Linq;
using DataExtractorShared;
using VRage.Definitions.Block;
using VRage.Game;

namespace DataExtractorMedieval
{
    public class BlockVariantDataExtractor : IPartitionedDataExtractor
    {
        public int Partitions => 4;
        public string PageName => "BlockVariants/Register";
        public void Run(Func<object, PageWriter> writers)
        {
            foreach (var def in MyDefinitionManager.GetOfType<MyBlockVariantsDefinition>().OrderBy(x => x.Id.SubtypeName))
            {
                if (!def.IsIncluded()) continue;
                var writer = writers(def.Id.SubtypeName);

                writer.Write(new TemplateInvocation("BlockVariants/Register")
                {
                    ["subtype"] = def.Id.SubtypeName,
                    ["name"] = def.DisplayNameText,
                    ["desc"] = def.DescriptionText,
                });
                foreach (var block in def.Blocks.OrderBy(x => x.Id.SubtypeName))
                    if (block.IsIncluded())
                        writer.Write(new TemplateInvocation("BlockVariants/RegisterBlock")
                        {
                            ["variant_subtype"] = def.Id.SubtypeName,
                            ["block_subtype"] = block.Id.SubtypeName
                        });
            }
        }
    }
}