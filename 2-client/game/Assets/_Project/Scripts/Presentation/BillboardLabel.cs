using UnityEngine;

namespace Pogranichye.Client.Presentation
{
    /// <summary>
    /// Keeps a world-space TextMesh readable from the active scene camera.
    /// </summary>
    public sealed class BillboardLabel : MonoBehaviour
    {
        private Camera _camera;

        private void LateUpdate()
        {
            if (_camera == null) _camera = Camera.main;
            if (_camera == null) return;

            // TextMesh's visible face is opposite the default back-facing local
            // orientation used by our generated entity hierarchy. Rotate the billboard
            // half a turn after aiming its forward axis at the camera so labels are never mirrored.
            var towardCamera = _camera.transform.position - transform.position;
            if (towardCamera.sqrMagnitude > 0.0001f)
                transform.rotation = Quaternion.LookRotation(towardCamera, Vector3.up) *
                    Quaternion.Euler(0f, 180f, 0f);
        }
    }
}
