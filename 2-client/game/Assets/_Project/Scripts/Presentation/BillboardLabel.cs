using UnityEngine;

namespace Pogranichye.Client.Presentation
{
    /// <summary>
    /// Keeps a world-space TextMesh readable from the active scene camera.
    /// The generated TextMesh front is local +Z, so its forward axis must point
    /// directly toward the camera. A 180-degree correction mirrors the glyphs.
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

            // TextMesh's readable face points along local +Z. Do not add a 180-degree
            // yaw here: that turns the back of the text toward the camera (mirrored text).
            transform.rotation = Quaternion.LookRotation(towardCamera.normalized, Vector3.up);
        }
    }
}