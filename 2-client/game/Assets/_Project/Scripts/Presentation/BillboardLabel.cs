using UnityEngine;

namespace Pogranichye.Client.Presentation
{
    /// <summary>
    /// Keeps a world-space TextMesh readable from the active scene camera.
    /// The generated TextMesh front faces local -Z. Its local forward must point
    /// away from the camera so the readable face points toward the viewer.
    /// </summary>
    public sealed class BillboardLabel : MonoBehaviour
    {
        private Camera _camera;

        private void OnEnable()
        {
            _camera = Camera.main;
        }

        private void LateUpdate()
        {
            if (_camera == null) _camera = Camera.main;
            if (_camera == null) return;

            // Keep nameplates upright: only billboard around world up. Projecting the
            // direction also avoids edge-on labels when the camera is looking steeply down.
            var towardCamera = Vector3.ProjectOnPlane(
                _camera.transform.position - transform.position, Vector3.up);
            if (towardCamera.sqrMagnitude < 0.0001f)
                towardCamera = Vector3.ProjectOnPlane(_camera.transform.forward, Vector3.up);
            if (towardCamera.sqrMagnitude < 0.0001f)
                towardCamera = Vector3.forward;

            // Unity TextMesh's front normal is local -Z (unlike a conventional +Z billboard).
            // Point local +Z away from the viewer so the readable face faces the camera.
            transform.rotation = Quaternion.LookRotation(-towardCamera.normalized, Vector3.up);
        }
    }
}