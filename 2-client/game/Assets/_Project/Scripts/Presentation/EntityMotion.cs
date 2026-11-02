using UnityEngine;

namespace Pogranichye.Client.Presentation
{
    /// <summary>Lightweight procedural prototype animation; no Animator Controller asset required.</summary>
    public sealed class EntityMotion : MonoBehaviour
    {
        private Transform _body;
        private Transform _leftArm;
        private Transform _rightArm;
        private Transform _leftLeg;
        private Transform _rightLeg;
        private float _phase;
        private bool _isPlayer;

        public void Initialize(Transform body, Transform leftArm, Transform rightArm,
            Transform leftLeg, Transform rightLeg, bool isPlayer)
        {
            _body = body;
            _leftArm = leftArm;
            _rightArm = rightArm;
            _leftLeg = leftLeg;
            _rightLeg = rightLeg;
            _isPlayer = isPlayer;
            _phase = Random.value * Mathf.PI * 2f;
        }

        private void Update()
        {
            if (_body == null) return;
            _phase += Time.deltaTime * (_isPlayer ? 2.2f : 1.7f);
            var idle = Mathf.Sin(_phase) * 0.025f;
            _body.localPosition = new Vector3(0, 1.05f + idle, 0);

            var swing = Mathf.Sin(_phase * 1.4f) * 0.045f;
            if (_leftArm != null) _leftArm.localRotation = Quaternion.Euler(swing * 90f, 0, -7f);
            if (_rightArm != null) _rightArm.localRotation = Quaternion.Euler(-swing * 90f, 0, 7f);
            if (_leftLeg != null) _leftLeg.localRotation = Quaternion.Euler(-swing * 35f, 0, 0);
            if (_rightLeg != null) _rightLeg.localRotation = Quaternion.Euler(swing * 35f, 0, 0);
        }
    }
}