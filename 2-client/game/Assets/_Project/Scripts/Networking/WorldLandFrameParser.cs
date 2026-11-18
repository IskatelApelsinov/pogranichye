using System;
using Pogranichye.Client.Networking.Protocol;

namespace Pogranichye.Client.Networking
{
    public static class WorldLandFrameParser
    {
        public static void Validate(WorldLandFrame land)
        {
            // Optional for pre-v1.33 layouts; grid cities require it in HttpAuthClient.
            if (land == null) return;
            // Older hand-authored layouts (notably the starter settlement) serialize
            // a zero-valued LandStyle object. Empty fields mean "metadata not supplied",
            // not a malformed layout; keep using the legacy river/zones data in that case.
            if (!string.IsNullOrEmpty(land.Water) &&
                land.Water != "river" && land.Water != "pond" && land.Water != "well")
                throw new FormatException("layout.land.water имеет неизвестное значение.");
            if (!string.IsNullOrEmpty(land.Fields) &&
                land.Fields != "strip" && land.Fields != "yards" && land.Fields != "none")
                throw new FormatException("layout.land.fields имеет неизвестное значение.");
            if (!string.IsNullOrEmpty(land.Forest) &&
                land.Forest != "side" && land.Forest != "ring" &&
                land.Forest != "copse" && land.Forest != "none")
                throw new FormatException("layout.land.forest имеет неизвестное значение.");
        }

        public static bool HasFishableWater(WorldLandFrame land, bool riverPresent, bool hasPondZone)
        {
            if (land != null && !string.IsNullOrEmpty(land.Water)) return land.Water == "river" || land.Water == "pond";
            return riverPresent || hasPondZone;
        }
    }
}
