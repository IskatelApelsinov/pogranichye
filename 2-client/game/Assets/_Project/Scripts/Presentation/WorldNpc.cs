using System;
using UnityEngine;

namespace Pogranichye.Client.Presentation
{
    /// <summary>
    /// Selectable nearby NPC. Selection requests the existing generic socialize goal;
    /// it does not imply a server-side targeted dialogue contract.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public sealed class WorldNpc : MonoBehaviour
    {
        public static event Action<WorldNpc> Selected;

        public ulong EntityId { get; private set; }
        public string DisplayName { get; private set; }

        public void Initialize(ulong entityId, string displayName)
        {
            EntityId = entityId;
            DisplayName = displayName ?? "Житель";
        }

        private void OnMouseDown()
        {
            Selected?.Invoke(this);
        }
    }
}
