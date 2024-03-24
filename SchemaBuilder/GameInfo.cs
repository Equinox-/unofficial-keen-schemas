using System.Collections.Generic;

namespace SchemaBuilder
{
    public sealed class GameInfo
    {
        public string SteamBranch;
        public uint SteamGameAppId;
        public uint SteamDedicatedAppId;
        public uint SteamDedicatedDepotId;

        public string RootType;

        public readonly HashSet<string> PolymorphicBaseTypes = new HashSet<string>();
        public readonly HashSet<string> PolymorphicSubtypeAttribute = new HashSet<string>();
        public readonly List<string> BinaryPrefixes = new List<string>();

        private GameInfo()
        {
        }

        public static readonly IReadOnlyDictionary<Game, GameInfo> Games = new Dictionary<Game, GameInfo>
        {
            [Game.MedievalEngineers] = new GameInfo
            {
                SteamBranch = "communityedition",
                SteamGameAppId = 333950,
                SteamDedicatedAppId = 367970,
                SteamDedicatedDepotId = 367971,
                RootType = "VRage.ObjectBuilders.Definitions.MyObjectBuilder_Definitions, VRage.Game",
                PolymorphicBaseTypes =
                {
                    "VRage.Game.MyObjectBuilder_DefinitionBase, VRage.Game",
                    "ObjectBuilders.GUI.MyObjectBuilder_ContextMenuAction, MedievalEngineers.ObjectBuilders",
                    "ObjectBuilders.GUI.MyObjectBuilder_ContextMenuCondition, MedievalEngineers.ObjectBuilders",
                    "ObjectBuilders.GUI.MyObjectBuilder_ContextMenuCondition, MedievalEngineers.ObjectBuilders",
                },
                PolymorphicSubtypeAttribute =
                {
                    "VRage.ObjectBuilders.MyObjectBuilderDefinitionAttribute",
                    "VRage.Serialization.Xml.MyXmlSerializableAttribute",
                },
                BinaryPrefixes =
                {
                    "MedievalEngineers",
                    "Sandbox",
                    "VRage"
                }
            },
            [Game.SpaceEngineers] = new GameInfo
            {
                SteamBranch = "public",
                SteamGameAppId = 244850,
                SteamDedicatedAppId = 298740,
                SteamDedicatedDepotId = 298741,
                RootType = "VRage.Game.MyObjectBuilder_Definitions, VRage.Game",
                PolymorphicBaseTypes =
                {
                    "VRage.Game.MyObjectBuilder_DefinitionBase, VRage.Game",
                },
                PolymorphicSubtypeAttribute =
                {
                    "VRage.ObjectBuilders.MyObjectBuilderDefinitionAttribute",
                    "VRage.Serialization.Xml.MyXmlSerializableAttribute",
                },
                BinaryPrefixes =
                {
                    "SpaceEngineers",
                    "Sandbox",
                    "VRage"
                }
            }
        };
    }
}