namespace Pogranichye.Client.Presentation
{
    /// <summary>
    /// Keeps interaction queries aligned with world geometry that is intentionally placed on
    /// Unity's built-in Ignore Raycast layer for camera collision.
    /// </summary>
    public static class WorldInteractionRaycastMaskPolicy
    {
        public const int IgnoreRaycastLayer = 2;

        public static int IncludeIgnoreRaycastLayer(int raycastMask)
        {
            return raycastMask | (1 << IgnoreRaycastLayer);
        }
    }
}
