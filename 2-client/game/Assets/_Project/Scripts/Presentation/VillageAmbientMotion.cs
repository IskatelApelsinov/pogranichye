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
