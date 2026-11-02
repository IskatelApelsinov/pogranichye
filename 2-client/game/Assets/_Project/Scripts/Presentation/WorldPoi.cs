using System;
using UnityEngine;

namespace Pogranichye.Client.Presentation
{
    /// <summary>
    /// Clickable tutorial point. Selection requests an existing server goal; it does not
    /// perform the activity or mutate world state locally.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public sealed class WorldPoi : MonoBehaviour
    {
        public static event Action<WorldPoi> Selected;

        public string Title { get; private set; }
        public string Goal { get; private set; }

        public void Initialize(string title, string goal)
        {
            Title = title;
            Goal = goal;
        }

        private void OnMouseDown()
        {
            if (enabled && !string.IsNullOrEmpty(Goal))
                Selected?.Invoke(this);
        }
    }
}