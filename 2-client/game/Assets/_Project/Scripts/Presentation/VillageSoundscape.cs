using System;
using UnityEngine;

namespace Pogranichye.Client.Presentation
{
    /// <summary>
    /// A small, deterministic set of procedural ambience loops for the local village.
    /// Keeps source count bounded: one 2D wind bed and five spatial ambience emitters.
    /// Replace generated clips with authored/licensed recordings when the audio pack is ready.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class VillageSoundscape : MonoBehaviour
    {
        private const int SampleRate = 22050;
        private AudioSource _wind;
        private AudioSource _birds;
        private AudioSource _water;
        private AudioSource _forge;
        private AudioSource _hearth;
        private AudioSource _tavern;
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
            if (_forge != null) _forge.volume = 0.18f * _ambienceVolume;
            if (_hearth != null) _hearth.volume = 0.24f * _ambienceVolume;
            if (_tavern != null) _tavern.volume = 0.12f * _ambienceVolume;
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

            // One spatial forge bed: distant low fire rumble with sparse hammer strikes.
            // Unity's 3D rolloff handles listener distance; no per-frame proximity polling.
            _forge = CreateSource("Ambience • blacksmith forge", 1f, 3f, 26f);
            _forge.transform.localPosition = new Vector3(27.9f, 1.15f, 20.65f);
            _forge.clip = CreateForgeLoop();
            _forge.volume = 0.18f * _ambienceVolume;
            _forge.Play();

            // Shared north hearth: one localized crackle loop, naturally distance-culled.
            _hearth = CreateSource("Ambience • village common hearth", 1f, 2f, 22f);
            _hearth.transform.localPosition = new Vector3(4f, 0.45f, 16f);
            _hearth.clip = CreateHearthLoop();
            _hearth.volume = 0.24f * _ambienceVolume;
            _hearth.Play();

            // A restrained, spatial room murmur makes the inn feel occupied without
            // adding voice actors, per-NPC emitters, or a constant village-wide noise bed.
            _tavern = CreateSource("Ambience • muffled tavern conversation", 1f, 4f, 24f);
            _tavern.transform.localPosition = new Vector3(-33f, 1.25f, 20f);
            _tavern.clip = CreateTavernMurmurLoop();
            _tavern.volume = 0.12f * _ambienceVolume;
            _tavern.Play();
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

        private static AudioClip CreateHearthLoop()
        {
            const int seconds = 9;
            var samples = new float[SampleRate * seconds];
            var random = new System.Random(16016);
            var filtered = 0f;
            for (var i = 0; i < samples.Length; i++)
            {
                var noise = (float)(random.NextDouble() * 2.0 - 1.0);
                filtered += (noise - filtered) * 0.11f;
                var slowBreath = 0.76f + 0.24f * Mathf.Sin(i / (float)SampleRate * 1.7f);
                samples[i] = (filtered * 0.20f + noise * 0.025f) * slowBreath;
            }

            // A small number of dry, short pops; no particle or emitter-per-ember objects.
            for (var pop = 0; pop < 15; pop++)
            {
                var start = random.Next(0, samples.Length - SampleRate / 20);
                var length = random.Next(SampleRate / 180, SampleRate / 55);
                var amplitude = 0.12f + (float)random.NextDouble() * 0.18f;
                for (var j = 0; j < length; j++)
                {
                    var envelope = 1f - j / (float)length;
                    samples[start + j] += ((float)random.NextDouble() * 2f - 1f)
                        * envelope * amplitude;
                }
            }

            return MakeLoop("Generated village hearth crackle", samples);
        }

        private static AudioClip CreateForgeLoop()
        {
            const int seconds = 10;
            var samples = new float[SampleRate * seconds];
            var random = new System.Random(28411);
            var low = 0f;
            for (var i = 0; i < samples.Length; i++)
            {
                var noise = (float)(random.NextDouble() * 2.0 - 1.0);
                low += (noise - low) * 0.018f;
                // Quiet, filtered forge/fire bed; transient strikes are layered below.
                samples[i] = low * 0.16f + noise * 0.012f;
            }

            // Sparse irregular hammer taps. Each short, damped metallic partial is
            // synthesized into the same loop, avoiding extra AudioSources or clips.
            var strikeTimes = new[] { 0.63f, 2.08f, 2.42f, 5.71f, 7.36f, 8.02f };
            for (var strike = 0; strike < strikeTimes.Length; strike++)
            {
                var start = Mathf.RoundToInt(strikeTimes[strike] * SampleRate);
                var length = Mathf.RoundToInt((0.11f + (float)random.NextDouble() * 0.055f) * SampleRate);
                var fundamental = 720f + (float)random.NextDouble() * 420f;
                var amplitude = 0.11f + (float)random.NextDouble() * 0.045f;
                for (var j = 0; j < length && start + j < samples.Length; j++)
                {
                    var t = j / (float)SampleRate;
                    var envelope = Mathf.Exp(-t * 25f);
                    var metallic = Mathf.Sin(2f * Mathf.PI * fundamental * t)
                        + 0.34f * Mathf.Sin(2f * Mathf.PI * fundamental * 2.73f * t);
                    samples[start + j] += metallic * envelope * amplitude;
                }
            }

            return MakeLoop("Generated distant forge and hammer", samples);
        }

        private static AudioClip CreateTavernMurmurLoop()
        {
            const int seconds = 12;
            var samples = new float[SampleRate * seconds];
            var random = new System.Random(28491);
            var voiceCount = 7;

            // Soft, overlapping speech-like tonal beds, deliberately indistinct:
            // this is ambience only and never conveys dialogue or gameplay information.
            for (var voice = 0; voice < voiceCount; voice++)
            {
                var start = random.Next(0, samples.Length / 3);
                var length = random.Next(SampleRate * 2, SampleRate * 5);
                var fundamental = 105f + (float)random.NextDouble() * 95f;
                var formant = 420f + (float)random.NextDouble() * 520f;
                var phase = (float)random.NextDouble() * Mathf.PI * 2f;
                var amplitude = 0.018f + (float)random.NextDouble() * 0.018f;
                for (var j = 0; j < length && start + j < samples.Length; j++)
                {
                    var t = j / (float)SampleRate;
                    var envelope = Mathf.Sin(Mathf.PI * j / Mathf.Max(1f, length - 1f));
                    var syllable = 0.72f + 0.28f * Mathf.Sin(t * (3.2f + voice * 0.31f) + phase);
                    var tone = Mathf.Sin(2f * Mathf.PI * fundamental * t + phase) * 0.55f
                        + Mathf.Sin(2f * Mathf.PI * formant * t + phase * 0.7f) * 0.18f;
                    var breath = (float)(random.NextDouble() * 2.0 - 1.0) * 0.12f;
                    samples[start + j] += (tone + breath) * envelope * syllable * amplitude;
                }
            }

            // A few distant tableware ticks add life without sounding like repeated SFX.
            for (var tick = 0; tick < 5; tick++)
            {
                var start = random.Next(0, samples.Length - SampleRate / 12);
                var length = random.Next(SampleRate / 90, SampleRate / 35);
                var frequency = 1450f + (float)random.NextDouble() * 900f;
                for (var j = 0; j < length; j++)
                {
                    var t = j / (float)SampleRate;
                    var envelope = Mathf.Exp(-t * 42f);
                    samples[start + j] += Mathf.Sin(2f * Mathf.PI * frequency * t)
                        * envelope * 0.012f;
                }
            }

            return MakeLoop("Generated distant tavern murmur", samples);
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
            DestroySource(_forge);
            DestroySource(_hearth);
            DestroySource(_tavern);
        }

        private static void DestroySource(AudioSource source)
        {
            if (source == null) return;
            var clip = source.clip;
            if (clip != null) Destroy(clip);
        }
    }
}
