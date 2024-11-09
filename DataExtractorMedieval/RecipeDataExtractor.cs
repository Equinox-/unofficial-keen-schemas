using System;
using System.Linq;
using DataExtractorShared;
using Medieval.Definitions.Crafting;
using VRage.Game;

namespace DataExtractorMedieval
{
    public class RecipeDataExtractor : IPartitionedDataExtractor
    {
        public int Partitions => 16;
        public string PageName => "Recipes/Register";
        public void Run(Func<object, PageWriter> writers)
        {
            foreach (var def in MyDefinitionManager.GetOfType<MyCraftingRecipeDefinition>().OrderBy(x => x.Id.SubtypeName))
            {
                if (!def.IsIncluded()) continue;
                var writer = writers(def.Id.SubtypeName);

                writer.Write(new TemplateInvocation("Recipes/Register")
                {
                    ["subtype"] = def.Id.SubtypeName,
                    ["name"] = def.DisplayNameText,
                    ["desc"] = def.DescriptionText,
                    ["time_sec"] = def.CraftingTime.TotalSeconds,
                });

                foreach (var group in def.Prerequisites.GroupBy(item => item.Id).OrderBy(x => x.Key.SubtypeName))
                    WriteRecipeItem(false, group.Key, group.Sum(item => item.Amount));
                foreach (var group in def.Results.GroupBy(item => item.Id).OrderBy(x => x.Key.SubtypeName))
                    WriteRecipeItem(true, group.Key, group.Sum(item => item.Amount));
                continue;

                void WriteRecipeItem(bool result, MyDefinitionId id, int amount) => writer.Write(new TemplateInvocation("Recipes/RegisterItem")
                {
                    ["recipe_subtype"] = def.Id.SubtypeName,
                    ["is_result"] = result,
                    ["item_is_tag"] = id.IsTag(),
                    ["item_subtype"] = id.SubtypeName,
                    ["amount"] = amount,
                });
            }
        }
    }
}