using System;

namespace Pogranichye.Client.Presentation
{
    /// <summary>
    /// Resolves visual treatment for server-authored space surfaces.
    /// Transparency is presentation-only and never changes collision.
    /// </summary>
    public static class WorldSpaceSurfacePresentationPolicy
    {
        public static bool UsesWindowGlass(string kind, bool transparent)
        {
            return IsWindow(kind) && transparent;
        }

        public static bool UsesOpaqueWindowFallback(string kind, bool transparent)
        {
            return IsWindow(kind) && !transparent;
        }

        public static bool CastsShadows(string kind, bool transparent)
        {
            if (UsesWindowGlass(kind, transparent)) return false;
            return !string.Equals(kind, "floor", StringComparison.OrdinalIgnoreCase) &&
                   !string.Equals(kind, "ceiling", StringComparison.OrdinalIgnoreCase);
        }

        public static bool ReceivesShadows(string kind, bool transparent)
        {
            return !UsesWindowGlass(kind, transparent);
        }

        private static bool IsWindow(string kind)
        {
            return string.Equals(kind, "window", StringComparison.OrdinalIgnoreCase);
        }
    }
}
