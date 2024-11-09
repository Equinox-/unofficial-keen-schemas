using System;
using System.Linq;
using DataExtractorShared;
using Medieval.Definitions.Inventory;
using Sandbox.Definitions.Inventory;
using VRage.Definitions.Inventory;
using VRage.Game;

namespace DataExtractorMedieval
{
    public class ItemDataExtractor : IPartitionedDataExtractor
    {
        public int Partitions => 8;
        public string PageName => "Items/Register";

        public void Run(Func<object, PageWriter> writers)
        {
            foreach (var def in MyDefinitionManager.GetOfType<MyInventoryItemDefinition>().OrderBy(x => x.Id.SubtypeName))
            {
                if (!def.IsIncluded()) continue;

                var writer = writers(def.Id.SubtypeName);

                writer.Write(new TemplateInvocation("Items/Register")
                {
                    ["subtype"] = def.Id.SubtypeName,
                    ["name"] = def.DisplayNameText,
                    ["desc"] = def.DescriptionText,
                    ["mass"] = def.Mass,
                    ["max_stack_amount"] = def.MaxStackAmount,

                    ["durability"] = (def as MyDurableItemDefinition)?.MaxDurability,
                    ["broken_item_subtype"] = (def as MyDurableItemDefinition)?.BrokenItem?.SubtypeName,
                });

                if (def is MyToolheadItemDefinition toolhead && toolhead.CraftingCategory != null)
                    foreach (var recipe in toolhead.CraftingCategory.Recipes.OrderBy(x => x.Id.SubtypeName))
                        if (recipe.IsIncluded())
                            writer.Write(new TemplateInvocation("Items/RegisterCraftingRecipe")
                            {
                                ["item"] = def.Id.SubtypeName,
                                ["recipe"] = recipe.Id.SubtypeName
                            });
            }
        }
    }
}