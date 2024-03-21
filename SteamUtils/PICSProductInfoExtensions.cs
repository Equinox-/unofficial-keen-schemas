using System;
using SteamKit2;
using static SteamKit2.SteamApps.PICSProductInfoCallback;

namespace SchemaService.SteamUtils
{
    public static class PICSProductInfoExtensions
    {
        public static ulong GetManifestId(this PICSProductInfo info, uint depotId, string branch)
        {
            return info.GetSection(EAppInfoSection.Depots)[depotId.ToString()]["manifests"][branch]["gid"].AsUnsignedLong();
        }

        public static byte[] GetEncryptedManifestId(this PICSProductInfo info, uint depotId, string branch)
        {
            var hex =  info.GetSection(EAppInfoSection.Depots)[depotId.ToString()]["encryptedmanifests"][branch]["gid"].AsString();
            if (string.IsNullOrEmpty(hex))
                return null;
            var bytes = new byte[hex.Length >> 1];
            for (int i = 0, j = 0; i < bytes.Length; i++, j += 2)
                bytes[i] = (byte)((Hex(hex[j]) << 4) | Hex(hex[j + 1]));
            return bytes;

            int Hex(char chr)
            {
                var val = (int)chr;
                if (val < 'A')
                    return val - '0';
                if (val < 'a')
                    return 10 + val - 'A';
                return 10 + val - 'a';
            }
        }

        public static uint GetWorkshopDepot(this PICSProductInfo info)
        {
            return info.GetSection(EAppInfoSection.Depots)["workshopdepot"].AsUnsignedInteger();
        }

        private static KeyValue GetSection(this PICSProductInfo info, EAppInfoSection section)
        {
            // ReSharper disable once SwitchStatementHandlesSomeKnownEnumValuesWithDefault
            switch (section)
            {
                case EAppInfoSection.Depots:
                    return info.KeyValues["depots"];
                default:
                    throw new NotSupportedException(section.ToString("G"));
            }
        }
    }
}