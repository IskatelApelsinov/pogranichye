using System;
using UnityEngine;

namespace Pogranichye.Client.Presentation
{
    /// <summary>
    /// A small, deterministic set of procedural ambience loops for the local village.
    /// Keeps source count bounded: one 2D wind bed and two spatial ambience emitters.
    /// Replace generated clips with authored/licensed recordings when the audio pack is ready.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class VillageSoundscape : MonoBehaviour
    {
        private const int SampleRate = 22050;
        private AudioSource _wind;
        private AudioSource _birds;
        private AudioSource _water;
        private float _ambienceVolume = 0.75f;
        private float _windAtmosphereFactor = 1f;
        private float _birdDaylightFactor = 1f;
        private float _nextAtmosphereMixAt;

        public void SetAmbienceVolume(float volume)
        {
            _ambienceVolume = Mathf.Clamp01(volume);
            ApplyVolumes();
        }

        private void Update()
        {
            // Three bounded sources do not need per-frame atmosphere polling.
            if (Time.time < _nextAtmosphereMixAt) return;
            _nextAtmosphereMixAt = Time.time + 0.25f;

            var wind01 = Mathf.InverseLerp(0.68f, 1.32f, WorldAtmosphereCycle.WindStrength);
            var targetWind = Mathf.Lerp(0.78f, 1.16f, wind01);
            var targetBirds = Mathf.Lerp(1f, 0.16f, WorldAtmosphereCycle.NightAmount);
            _windAtmosphereFactor = Mathf.Lerp(_windAtmosphereFactor, targetWind, 0.42f);
            _birdDaylightFactor = Mathf.Lerp(_birdDaylightFactor, targetBirds, 0.42f);
            ApplyVolumes();
        }

        private void ApplyVolumes()
        {
            if (_wind != null) _wind.volume = 0.16f * _ambienceVolume * _windAtmosphereFactor;
            if (_birds != null) _birds.volume = 0.20f * _ambienceVolume * _birdDaylightFactor;
            if (_water != null) _water.volume = 0.22f * _ambienceVolume;
        }

        public void Initialize()
        {
            if (_wind != null) return;

            _wind = CreateSource("Ambience • soft meadow wind", 0f, 0f, 1f);
            _wind.clip = CreateWindLoop();
            _wind.volume = 0.16f * _ambienceVolume;
            _wind.Play();

            _birds = CreateSource("Ambience • distant birds", 0.78f, 8f, 72f);
            _birds.transform.localPosition = new Vector3(12f, 5f, 18f);
            _birds.clip = CreateBirdLoop();
            _birds.volume = 0.20f * _ambienceVolume;
            _birds.Play();

            _water = CreateSource("Ambience • creek and reeds", 0.86f, 5f, 58f);
            _water.transform.localPosition = new Vector3(0f, 1.2f, -108f);
            _water.clip = CreateWaterLoop();
            _water.volume = 0.22f * _ambienceVolume;
            _water.Play();
        }

        private AudioSource CreateSource(string sourceName, float blend, float minDistance, float maxDistance)
        {
            var child = new GameObject(sourceName);
            child.transform.SetParent(transform, false);
            var source = child.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = true;
            source.spatialBlend = blend;
            source.dopplerLevel = 0f;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.minDistance = minDistance;
            source.maxDistance = maxDistance;
            source.priority = 180;
            source.reverbZoneMix = 0.25f;
            return source;
        }

        private static AudioClip CreateWindLoop()
        {
            const int seconds = 8;
            var samples = new float[SampleRate * seconds];
            var random = new System.Random(191);
            var low = 0f;
            for (var i = 0; i < samples.Length; i++)
            {
                var noise = (float)(random.NextDouble() * 2.0 - 1.0);
                low += (noise - low) * 0.012f;
                var swell = 0.68f + 0.32f * Mathf.Sin(i / (float)SampleRate * Mathf.PI * 2f / 5.5f);
                samples[i] = Mathf.Clamp(low * 0.72f + noise * 0.035f, -0.9f, 0.9f) * swell;
            }
            return MakeLoop("Generated meadow wind", samples);
        }

        private static AudioClip CreateBirdLoop()
        {
            const int seconds = 12;
            var samples = new float[SampleRate * seconds];
            // Sparse, soft tonal chirps. A single loop replaces many constantly active emitters.
            var random = new System.Random(7331);
            for (var chirp = 0; chirp < 18; chirp++)
            {
                var start = random.Next(0, samples.Length - SampleRate / 3);
                var length = random.Next(SampleRate / 18, SampleRate / 8);
                var frequency = 1700f + (float)random.NextDouble() * 1500f;
                var sweep = 220f + (float)random.NextDouble() * 420f;
                var amplitude = 0.12f + (float)random.NextDouble() * 0.12f;
                for (var j = 0; j < length; j++)
                {
                    var t = j / (float)SampleRate;
                    var phase = 2f * Mathf.PI * (frequency * t + sweep * t * t * 0.5f);
                    var envelope = Mathf.Sin(Mathf.PI * j / Mathf.Max(1f, length - 1f));
                    var value = Mathf.Sin(phase) * envelope * amplitude;
                    var index = start + j;
                    samples[index] = Mathf.Clamp(samples[index] + value, -0.8f, 0.8f);
                }
            }
            return MakeLoop("Generated distant bird calls", samples);
        }

        private static AudioClip CreateWaterLoop()
        {
            const int seconds = 8;
            var samples = new float[SampleRate * seconds];
            var random = new System.Random(991);
            var filtered = 0f;
            for (var i = 0; i < samples.Length; i++)
            {
                var noise = (float)(random.NextDouble() * 2.0 - 1.0);
                filtered += (noise - filtered) * 0.075f;
                var ripple = 0.72f + 0.20f * Mathf.Sin(i * 0.0017f) + 0.08f * Mathf.Sin(i * 0.00043f);
                samples[i] = filtered * ripple * 0.48f;
            }
            return MakeLoop("Generated creek water", samples);
        }

        private static AudioClip MakeLoop(string clipName, float[] samples)
        {
            // Fade the loop boundaries to avoid a hard discontinuity/click on repeat.
            var fadeSamples = Mathf.Min(SampleRate / 6, samples.Length / 4);
            for (var i = 0; i < fadeSamples; i++)
            {
                var gain = i / (float)fadeSamples;
                samples[i] *= gain;
                samples[samples.Length - 1 - i] *= gain;
            }
            var clip = AudioClip.Create(clipName, samples.Length, 1, SampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        private void OnDestroy()
        {
            DestroySource(_wind);
            DestroySource(_birds);
            DestroySource(_water);
        }

        private static void DestroySource(AudioSource source)
        {
            if (source == null) return;
            var clip = source.clip;
            if (clip != null) Destroy(clip);
        }
    }
}
