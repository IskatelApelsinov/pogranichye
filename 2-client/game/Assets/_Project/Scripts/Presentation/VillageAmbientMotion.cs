using System.Collections.Generic;
using UnityEngine;

namespace Pogranichye.Client.Presentation
{
    /// <summary>
    /// Small presentation-only ambient motion for the generated village.
    /// It never changes gameplay state or networked entity positions.
    /// </summary>
    public sealed class VillageAmbientMotion : MonoBehaviour
    {
        private struct LightPulse
        {
            public Light Light;
            public float BaseIntensity;
            public float Amplitude;
            public float Speed;
            public float Phase;
        }

        private struct Sway
        {
            public Transform Target;
            public Quaternion RestRotation;
            public float Degrees;
            public float Speed;
            public float Phase;
        }

        private readonly List<LightPulse> _lights = new List<LightPulse>();
        private readonly List<Sway> _sways = new List<Sway>();

        public void RegisterLight(Light light, float amplitude = 0.12f, float speed = 2.4f)
        {
            if (light == null) return;
            _lights.Add(new LightPulse
            {
                Light = light,
                BaseIntensity = light.intensity,
                Amplitude = Mathf.Max(0f, amplitude),
                Speed = Mathf.Max(0.1f, speed),
                Phase = _lights.Count * 1.71f
            });
        }

        public void RegisterSway(Transform target, float degrees = 3f, float speed = 1.2f)
        {
            if (target == null) return;
            _sways.Add(new Sway
            {
                Target = target,
                RestRotation = target.localRotation,
                Degrees = Mathf.Max(0f, degrees),
                Speed = Mathf.Max(0.1f, speed),
                Phase = _sways.Count * 2.13f
            });
        }

        private void Update()
        {
            var time = Time.time;
            for (var i = _lights.Count - 1; i >= 0; i--)
            {
                var pulse = _lights[i];
                if (pulse.Light == null) { _lights.RemoveAt(i); continue; }
                var wave = Mathf.Sin(time * pulse.Speed + pulse.Phase) * 0.65f
                         + Mathf.Sin(time * pulse.Speed * 2.17f + pulse.Phase) * 0.35f;
                pulse.Light.intensity = pulse.BaseIntensity * (1f + wave * pulse.Amplitude);
            }

            for (var i = _sways.Count - 1; i >= 0; i--)
            {
                var sway = _sways[i];
                if (sway.Target == null) { _sways.RemoveAt(i); continue; }
                var angle = Mathf.Sin(time * sway.Speed + sway.Phase) * sway.Degrees;
                sway.Target.localRotation = sway.RestRotation * Quaternion.Euler(0f, 0f, angle);
            }
        }
    }

    /// <summary>
    /// Small decorative yard bird. Its movement is local presentation only and is not a networked animal entity.
    /// </summary>
    public sealed class VillageChickenMotion : MonoBehaviour
    {
        private System.Random _random;
        private Transform _body;
        private Transform _head;
        private Transform _leftLeg;
        private Transform _rightLeg;
        private Vector3 _bodyRest;
        private Quaternion _headRest;
        private Vector3 _target;
        private Vector3 _homePosition;
        private float _wanderRadius;
        private float _speed;
        private float _phase;
        private float _pause;

        public void Initialize(Transform body, Transform head, Transform leftLeg, Transform rightLeg,
            int seed, float wanderRadius = 0.45f)
        {
            _random = new System.Random(seed);
            _homePosition = transform.localPosition;
            _wanderRadius = Mathf.Max(0.1f, wanderRadius);
            _body = body;
            _head = head;
            _leftLeg = leftLeg;
            _rightLeg = rightLeg;
            _bodyRest = body != null ? body.localPosition : Vector3.zero;
            _headRest = head != null ? head.localRotation : Quaternion.identity;
            _phase = (float)_random.NextDouble() * Mathf.PI * 2f;
            PickTarget(wanderRadius);
        }

        private void PickTarget(float radius)
        {
            if (_random == null) return;
            var angle = _random.NextDouble() * Mathf.PI * 2.0;
            var distance = 0.15 + _random.NextDouble() * Mathf.Max(0.1f, radius);
            _target = _homePosition + new Vector3((float)System.Math.Cos(angle) * (float)distance, 0f,
                (float)System.Math.Sin(angle) * (float)distance);
            _speed = 0.38f + (float)_random.NextDouble() * 0.28f;
            _pause = 0.45f + (float)_random.NextDouble() * 1.7f;
        }

        private void Update()
        {
            if (_random == null) return;
            var position = transform.localPosition;
            var delta = _target - position;
            if (delta.sqrMagnitude > 0.025f)
            {
                var direction = delta.normalized;
                transform.localPosition = Vector3.MoveTowards(position, _target, _speed * Time.deltaTime);
                var look = Quaternion.LookRotation(direction, Vector3.up);
                transform.localRotation = Quaternion.Slerp(transform.localRotation, look,
                    Mathf.Clamp01(Time.deltaTime * 5f));
            }
            else if (_pause > 0f)
            {
                _pause -= Time.deltaTime;
            }
            else
            {
                PickTarget(_wanderRadius);
            }

            var moving = delta.sqrMagnitude > 0.025f;
            var gait = Mathf.Sin(Time.time * 13f + _phase);
            if (_body != null)
                _body.localPosition = _bodyRest + Vector3.up * (moving ? Mathf.Abs(gait) * 0.025f : 0.008f * Mathf.Sin(Time.time * 2f + _phase));
            if (_head != null)
            {
                var peck = moving ? 2.5f : Mathf.Max(0f, Mathf.Sin(Time.time * 2.1f + _phase)) * 17f;
                _head.localRotation = _headRest * Quaternion.Euler(peck, 0f, 0f);
            }
            if (_leftLeg != null) _leftLeg.localRotation = Quaternion.Euler(0f, 0f, moving ? gait * 22f : 0f);
            if (_rightLeg != null) _rightLeg.localRotation = Quaternion.Euler(0f, 0f, moving ? -gait * 22f : 0f);
        }
    }

    /// <summary>Very slow UV drift gives the decorative creek a readable current without moving gameplay objects.</summary>
    public sealed class CreekWaterDrift : MonoBehaviour
    {
        private Material _material;
        private float _speed;
        private Vector2 _origin;

        public void Initialize(Material material, float speed)
        {
            _material = material;
            _speed = speed;
            if (_material != null) _origin = _material.mainTextureOffset;
        }

        private void Update()
        {
            if (_material == null) return;
            _material.mainTextureOffset = _origin + Vector2.up * (Time.time * _speed);
        }
    }

}
