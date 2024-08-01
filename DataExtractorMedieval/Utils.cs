using Medieval.Definitions.Block;
using VRage.Definitions.Block;
using VRage.Game;
using VRage.Game.Definitions;
using VRage.ObjectBuilders.Definitions.Inventory;

namespace DataExtractorMedieval
{
    public static class Utils
    {
        public static bool IsTag(this MyDefinitionId id) => id.TypeId == typeof(MyObjectBuilder_ItemTagDefinition);

        public static bool IsIncluded(this MyVisualDefinitionBase def)
        {
            switch (def)
            {
                case MyGeneratedBlockDefinition _:
                    return false;
                case MyBuildableBlockDefinition block:
                    return block.Public && block.ComponentCount > 0;
                default:
                    return def.Public;
            }
        }
    }
}