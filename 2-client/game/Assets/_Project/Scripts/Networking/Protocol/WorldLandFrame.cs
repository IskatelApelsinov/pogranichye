using System;
using Newtonsoft.Json;

namespace Pogranichye.Client.Networking.Protocol
{
    [Serializable]
    public sealed class WorldLandFrame
    {
        [JsonProperty("water")] public string Water { get; set; }
        [JsonProperty("fields")] public string Fields { get; set; }
        [JsonProperty("forest")] public string Forest { get; set; }
    }

    public static class WorldLandFrameParser
    {
        public static void Validate(WorldLandFrame land)
        {
            // Optional for old Layout v2 servers; v1.33 generated layouts always include it.
            if (land == null) return;
            if (land.Water != "river" && land.Water != "pond" && land.Water != "well")
                throw new FormatException("layout.land.water имеет неизвестное значение.");
            if (land.Fields != "strip" && land.Fields != "yards" && land.Fields != "none")
                throw new FormatException("layout.land.fields имеет неизвестное значение.");
            if (land.Forest != "side" && land.Forest != "ring" &&
                land.Forest != "copse" && land.Forest != "none")
                throw new FormatException("layout.land.forest имеет неизвестное значение.");
        }
    }
}
