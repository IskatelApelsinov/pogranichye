using System;
using UnityEngine;

namespace Pogranichye.Client.Presentation
{
    /// <summary>Bounded procedural placeholder SFX for the vertical slice. Replace with licensed recordings later.</summary>
    [DisallowMultipleComponent]
    public sealed class WorldAudioDirector : MonoBehaviour
    {
        private const int Rate = 22050;
        private AudioSource _oneShot;
        private AudioSource _fire;
        private AudioClip _step, _run, _combat, _magic, _beer, _fireLoop;
        private float _nextStepAt;

        public void Initialize()
        {
            if (_oneShot != null) return;
            var playerAudioObject = new GameObject("Player • spatial action sounds");
            playerAudioObject.transform.SetParent(transform, false);
            _oneShot = playerAudioObject.AddComponent<AudioSource>();
            _oneShot.playOnAwake = false;
            _oneShot.spatialBlend = 0.82f;
            _oneShot.minDistance = 2f;
            _oneShot.maxDistance = 26f;
            _oneShot.dopplerLevel = 0f;
            _oneShot.rolloffMode = AudioRolloffMode.Linear;
            _oneShot.priority = 64;
            _step = MakeNoise("Soft footsteps on earth", 0.12f, 0.085f, 0.12f);
            _run = MakeNoise("Running footsteps", 0.20f, 0.065f, 0.17f);
            _combat = MakeNoise("Steel combat impact", 0.32f, 0.11f, 0.24f);
            _magic = MakeTone("Magic cast shimmer", 0.38f, 0.42f, 0.19f);
            _beer = MakeNoise("Beer sip and mug", 0.15f, 0.24f, 0.13f);
            _fireLoop = MakeNoise("Village campfire crackle", 0.055f, 0.18f, 0.08f, 4);
            var fireObject = new GameObject("Ambience • campfire crackle");
            fireObject.transform.SetParent(transform, false);
            fireObject.transform.localPosition = new Vector3(-3.5f, 0.8f, 4.5f);
            _fire = fireObject.AddComponent<AudioSource>();
            _fire.clip = _fireLoop;
            _fire.loop = true;
            _fire.playOnAwake = false;
            _fire.spatialBlend = 1f;
            _fire.minDistance = 2f;
            _fire.maxDistance = 18f;
            _fire.dopplerLevel = 0f;
            _fire.volume = 0.42f;
            _fire.Play();
        }

        public void SetPlayerPosition(Vector3 position)
        {
            if (_oneShot != null) _oneShot.transform.position = position;
        }

        public void Footsteps(bool running)
        {
            if (Time.unscaledTime < _nextStepAt || _oneShot == null) return;
            _nextStepAt = Time.unscaledTime + (running ? 0.31f : 0.48f);
            _oneShot.PlayOneShot(running ? _run : _step, running ? 0.82f : 0.62f);
        }

        public void Combat() { if (_oneShot != null) _oneShot.PlayOneShot(_combat, 0.72f); }
        public void Magic() { if (_oneShot != null) _oneShot.PlayOneShot(_magic, 0.78f); }
        public void DrinkBeer() { if (_oneShot != null) _oneShot.PlayOneShot(_beer, 0.9f); }

        private static AudioClip MakeNoise(string name, float amplitude, float duration, float decay, int seconds = 1)
        {
            var length = seconds > 1 ? Rate * seconds : Mathf.Max(1, Mathf.CeilToInt(Rate * duration));
            var data = new float[length];
            var random = new System.Random(name.GetHashCode());
            for (var i = 0; i < length; i++)
            {
                var t = i / (float)Rate;
                var randomValue = (float)random.NextDouble();
                var envelope = seconds > 1 ? 0.18f + randomValue * 0.82f : Mathf.Exp(-t / Mathf.Max(0.02f, decay));
                var noise = (float)(random.NextDouble() * 2.0 - 1.0);
                var low = Mathf.Sin(t * 2f * Mathf.PI * 92f) * 0.24f;
                data[i] = (noise * 0.72f + low) * amplitude * envelope;
            }
            var clip = AudioClip.Create(name, data.Length, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        private static AudioClip MakeTone(string name, float frequency, float duration, float amplitude)
        {
            var length = Mathf.CeilToInt(Rate * duration);
            var data = new float[length];
            for (var i = 0; i < length; i++)
            {
                var t = i / (float)Rate;
                var envelope = Mathf.Sin(Mathf.PI * i / Mathf.Max(1f, length - 1f));
                var sweep = frequency * (1f + t * 2.2f);
                data[i] = Mathf.Sin(2f * Mathf.PI * sweep * t) * envelope * amplitude * 0.35f;
            }
            var clip = AudioClip.Create(name, data.Length, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        private void OnDestroy()
        {
            DestroyClip(_step); DestroyClip(_run); DestroyClip(_combat);
            DestroyClip(_magic); DestroyClip(_beer); DestroyClip(_fireLoop);
        }

        private static void DestroyClip(AudioClip clip) { if (clip != null) Destroy(clip); }
    }
}
