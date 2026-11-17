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
}
