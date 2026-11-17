using Newtonsoft.Json;

namespace Pogranichye.Client.Networking.Protocol
{
    [System.Serializable]
    public sealed class ActionResultFrame
    {
        [JsonProperty("reqid")] public string RequestId { get; set; }
        [JsonProperty("ok")] public bool Ok { get; set; }
        [JsonProperty("reason")] public string Reason { get; set; }
    }
}
