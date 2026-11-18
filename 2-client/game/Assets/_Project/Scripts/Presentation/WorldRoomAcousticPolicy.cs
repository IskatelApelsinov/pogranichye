using System;

namespace Pogranichye.Client.Presentation
{
    /// <summary>Exact room-kind to acoustic-preset mapping for server-authored spaces.</summary>
    public static class WorldRoomAcousticPolicy
    {
        public static bool UsesCaveReverb(string roomKind)
        {
            return string.Equals(roomKind, "cave", StringComparison.Ordinal) ||
                string.Equals(roomKind, "tunnel", StringComparison.Ordinal);
        }
    }
}
