using System.Collections.Generic;

namespace SchemaBuilder
{
    public sealed class GameInfo
    {
        public string SteamBranch;
        public uint SteamDedicatedAppId;
        public uint SteamDedicatedDepotId;

        public string RootType;

        public readonly HashSet<string> PolymorphicBaseTypes = new HashSet<string>();
        public readonly HashSet<string> PolymorphicSubtypeAttribute = new HashSet<string>();

        private GameInfo()
        {
        }

        public static readonly IReadOnlyDictionary<Game, GameInfo> Games = new Dictionary<Game, GameInfo>
        {
            [Game.ME] = new GameInfo
            {
                SteamBranch = "communityedition",
                SteamDedicatedAppId = 367970,
                SteamDedicatedDepotId = 367971,
                RootType = "VRage.ObjectBuilders.Definitions.MyObjectBuilder_Definitions, VRage.Game",
                PolymorphicBaseTypes =
                {
                    "VRage.Game.MyObjectBuilder_DefinitionBase, VRage.Game",
                },
                PolymorphicSubtypeAttribute =
                {
                    "VRage.ObjectBuilders.MyObjectBuilderDefinitionAttribute"
                },
            },
            [Game.SE] = new GameInfo
            {
                SteamBranch = "public",
                SteamDedicatedAppId = 298740,
                SteamDedicatedDepotId = 298741,
                RootType = "VRage.Game.MyObjectBuilder_Definitions, VRage.Game",
                PolymorphicBaseTypes =
                {
                    "VRage.Game.MyObjectBuilder_DefinitionBase, VRage.Game",
                },
                PolymorphicSubtypeAttribute =
                {
                    "VRage.ObjectBuilders.MyObjectBuilderDefinitionAttribute"
                },
            }
        };
    }
}