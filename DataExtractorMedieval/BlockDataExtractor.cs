using System;
using System.Collections.Generic;
using System.Linq;
using DataExtractorShared;
using Medieval.Definitions.Block;
using Medieval.Definitions.Inventory;
using Medieval.Entities.Components.Crafting.Recipes;
using Sandbox.Definitions.Components;
using VRage.Game;

namespace DataExtractorMedieval
{
    public class BlockDataExtractor : IPartitionedDataExtractor
    {
        public string PageName => "Blocks/Register";
        public int Partitions => 16;

        public void Run(Func<object, PageWriter> writers)
        {
            foreach (var def in MyDefinitionManager.GetOfType<MyBuildableBlockDefinition>().OrderBy(x => x.Id.SubtypeName))
            {
                if (!def.IsIncluded())
                    continue;

                var writer = writers(def.Id.SubtypeName);

                var components = MyDefinitionManager.Get<MyContainerDefinition>(def.Id)?
                    .ComponentIndex
                    .Values
                    .Select(x => x.Definition)
                    .ToArray() ?? Array.Empty<MyEntityComponentDefinition>();

                writer.Write(new TemplateInvocation("Blocks/Register")
                {
                    ["subtype"] = def.Id.SubtypeName,
                    ["name"] = def.DisplayNameText,
                    ["desc"] = def.DescriptionText,
                    ["mass"] = def.Mass,
                    ["grid_subtype"] = def.GridDataDefinitionId.SubtypeName,
                    ["volume"] = def.Positions.Count,
                    ["max_integrity"] = def.MaxIntegrity
                });

                for (var i = 0; i < def.Components.Length; i++)
                {
                    var component = def.Components[i];
                    writer.Write(new TemplateInvocation("Blocks/RegisterComponent")
                    {
                        ["block_subtype"] = def.Id.SubtypeName,
                        ["index"] = i,
                        ["required_is_tag"] = component.Id.IsTag(),
                        ["required_subtype"] = component.Id.SubtypeName,
                        ["returned_is_tag"] = component.ReturnedItem?.IsTag(),
                        ["returned_subtype"] = component.ReturnedItem?.SubtypeName,
                        ["amount"] = component.Count,
                    });
                }

                foreach (var toolhead in CraftingToolHeads().OrderBy(x => x.Id.SubtypeName))
                    if (toolhead.IsIncluded())
                        writer.Write(new TemplateInvocation("Blocks/RegisterCraftingToolhead")
                        {
                            ["block"] = def.Id.SubtypeName,
                            ["toolhead"] = toolhead.Id.SubtypeName
                        });

                foreach (var recipe in GetComponents<MyConstantRecipeProviderComponentDefinition>()
                             .SelectMany(x => x.Recipes).Distinct()
                             .OrderBy(x => x.Id.SubtypeName))
                    if (recipe.IsIncluded())
                        writer.Write(new TemplateInvocation("Blocks/RegisterCraftingRecipe")
                        {
                            ["block"] = def.Id.SubtypeName,
                            ["recipe"] = recipe.Id.SubtypeName
                        });

                continue;

                T GetComponent<T>() where T : MyEntityComponentDefinition => GetComponents<T>().FirstOrDefault();

                bool TryGetComponent<T>(out T component) where T : MyEntityComponentDefinition
                {
                    component = GetComponent<T>();
                    return component != null;
                }

                IEnumerable<T> GetComponents<T>() where T : MyEntityComponentDefinition => components.OfType<T>();


                IEnumerable<MyToolheadItemDefinition> CraftingToolHeads() => MyDefinitionManager.GetOfType<MyToolheadItemDefinition>()
                    .Where(x => x.IsIncluded() && x.CraftingCategory != null)
                    .Where(x =>
                    {
                        foreach (var toolBasedProvider in GetComponents<MyToolBasedRecipeProviderComponentDefinition>())
                            if (toolBasedProvider.ToolheadConstraint.Check(x.Id))
                                return true;
                        return false;
                    });
            }
        }
    }
}