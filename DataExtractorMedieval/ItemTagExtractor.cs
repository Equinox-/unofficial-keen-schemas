using System;
using System.Linq;
using DataExtractorShared;
using VRage.Definitions.Inventory;
using VRage.Game;

namespace DataExtractorMedieval
{
    public class ItemTagDataExtractor : IPartitionedDataExtractor
    {
        public int Partitions => 4;
        public string PageName => "ItemTags/Register";

        public void Run(Func<object, PageWriter> writers)
        {
            foreach (var def in MyDefinitionManager.GetOfType<MyItemTagDefinition>().OrderBy(x => x.Id.SubtypeName))
            {
                if (!def.IsIncluded())
                    continue;
                var writer = writers(def.Id.SubtypeName);
                writer.Write(new TemplateInvocation("ItemTags/Register")
                {
                    ["subtype"] = def.Id.SubtypeName,
                    ["name"] = def.DisplayNameText,
                    ["desc"] = def.DescriptionText,
                });
                foreach (var item in def.Items.OrderBy(x => x.Id.SubtypeName))
                    if (item.IsIncluded())
                        writer.Write(new TemplateInvocation("ItemTags/RegisterItem")
                        {
                            ["tag_subtype"] = def.Id.SubtypeName,
                            ["item_subtype"] = item.Id.SubtypeName
                        });
            }
        }
    }
}