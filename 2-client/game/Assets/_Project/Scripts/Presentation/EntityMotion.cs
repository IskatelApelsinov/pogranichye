using UnityEngine;

namespace Pogranichye.Client.Presentation
{
    /// <summary>Lightweight role/activity animation for the first scene; no Animator asset required.</summary>
    public sealed class EntityMotion : MonoBehaviour
    {
        private Transform _body;
        private Transform _leftArm;
        private Transform _rightArm;
        private Transform _leftLeg;
        private Transform _rightLeg;
        private float _phase;
        private bool _isPlayer;
        private string _activity = "";

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

        public void SetActivity(string activity)
        {
            _activity = (activity ?? string.Empty).ToLowerInvariant();
        }

        private void Update()
        {
            if (_body == null) return;
            _phase += Time.deltaTime * (_isPlayer ? 2.2f : 1.7f);

            var resting = _activity.Contains("rest") || _activity.Contains("отдых");
            var working = _activity.Contains("work") || _activity.Contains("работ");
            var social = _activity.Contains("social") || _activity.Contains("общ") || _activity.Contains("говор");
            var eating = _activity.Contains("eat") || _activity.Contains("ест") || _activity.Contains("ед");

            var bob = resting ? 0.008f : 0.025f;
            _body.localPosition = new Vector3(0, 1.05f + Mathf.Sin(_phase) * bob, 0);

            var swing = Mathf.Sin(_phase * 1.4f);
            var leftArmAngle = working ? swing * 32f : social ? Mathf.Sin(_phase * 2f) * 24f : 0f;
            var rightArmAngle = working ? -swing * 38f : social ? Mathf.Cos(_phase * 2f) * 26f : 0f;
            if (eating) rightArmAngle = Mathf.Sin(_phase * 1.8f) * 34f - 28f;
            if (resting) { leftArmAngle = 8f; rightArmAngle = -8f; }

            if (_leftArm != null) _leftArm.localRotation = Quaternion.Euler(leftArmAngle, 0, -7f);
            if (_rightArm != null) _rightArm.localRotation = Quaternion.Euler(rightArmAngle, 0, 7f);
            if (_leftLeg != null) _leftLeg.localRotation = Quaternion.Euler(swing * 2f, 0, 0);
            if (_rightLeg != null) _rightLeg.localRotation = Quaternion.Euler(-swing * 2f, 0, 0);
        }
    }
}