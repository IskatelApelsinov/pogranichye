using UnityEngine;

namespace Pogranichye.Client.Presentation
{
    /// <summary>
    /// Presentation marker for a server-owned foe (beast or bandit) in the settlement scene.
    /// Colliders under this root are what the first-person aim ray hits; the controller reads
    /// the id and the body proportions to tell the server which body part was aimed at.
    /// </summary>
    public sealed class CoreFoeView : MonoBehaviour
    {
        public string FoeId;
        public string Kind;
        public string DisplayName;
        public bool Humanoid;
        public float Length = 1f;   // beast body length along local +Z
        public float LegHeight = 0.3f;
        public float Height = 1f;   // top of the body, for the floating label
        public bool Dead;
    }
}
