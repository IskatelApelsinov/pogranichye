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
            if (land.Water != "river" && land.Water != "pond" && land.Water != "well")
                throw new FormatException("layout.land.water имеет неизвестное значение.");
            if (land.Fields != "strip" && land.Fields != "yards" && land.Fields != "none")
                throw new FormatException("layout.land.fields имеет неизвестное значение.");
            if (land.Forest != "side" && land.Forest != "ring" &&
                land.Forest != "copse" && land.Forest != "none")
                throw new FormatException("layout.land.forest имеет неизвестное значение.");
        }

        public static bool HasFishableWater(WorldLandFrame land, bool riverPresent, bool hasPondZone)
        {
            if (land != null) return land.Water == "river" || land.Water == "pond";
            return riverPresent || hasPondZone;
        }
    }
}
