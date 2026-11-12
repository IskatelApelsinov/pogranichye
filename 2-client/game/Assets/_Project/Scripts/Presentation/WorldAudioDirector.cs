using System;
using UnityEngine;

namespace Pogranichye.Client.Presentation
{
    /// <summary>Bounded procedural placeholder SFX for the vertical slice. Replace with licensed recordings later.</summary>
    [DisallowMultipleComponent]
    public sealed class WorldAudioDirector : MonoBehaviour
    {
        private const int Rate = 22050;
        private const float NearbyVoiceMaxDistance = 24f;
        private AudioSource _oneShot;
        private AudioSource _fire;
        private AudioClip _step, _run, _combat, _magic, _beer, _fireLoop;
        private AudioClip _npcWoodcut, _npcForge, _npcFishing, _npcFieldwork, _npcMining, _npcHearth, _npcHerding;
        private readonly AudioSource[] _nearbyVoices = new AudioSource[6];
        private int _nearbyVoiceCursor;
        private float _nextStepAt;
        private float _sfxVolume = 0.9f;
        private float _ambienceVolume = 0.75f;

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
            _oneShot.volume = _sfxVolume;
            _step = MakeNoise("Soft footsteps on earth", 0.12f, 0.085f, 0.12f);
            _run = MakeNoise("Running footsteps", 0.20f, 0.065f, 0.17f);
            _combat = MakeNoise("Steel combat impact", 0.32f, 0.11f, 0.24f);
            _magic = MakeTone("Magic cast shimmer", 0.38f, 0.42f, 0.19f);
            _beer = MakeNoise("Beer sip and mug", 0.15f, 0.24f, 0.13f);
            _fireLoop = MakeNoise("Village campfire crackle", 0.055f, 0.18f, 0.08f, 4);
            _npcWoodcut = MakeNoise("Nearby NPC axe and wood", 0.22f, 0.13f, 0.055f);
            _npcForge = MakeMetalTap("Nearby NPC forge hammer", 690f, 0.18f);
            _npcFishing = MakeNoise("Nearby NPC fishing splash", 0.10f, 0.16f, 0.07f);
            _npcFieldwork = MakeNoise("Nearby NPC hoe in soil", 0.12f, 0.12f, 0.06f);
            _npcMining = MakeMetalTap("Nearby NPC mining pick", 430f, 0.15f);
            _npcHearth = MakeNoise("Nearby NPC hearth tending", 0.075f, 0.12f, 0.055f);
            _npcHerding = MakeNoise("Nearby NPC livestock handling", 0.065f, 0.15f, 0.08f);
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
            _fire.volume = 0.42f * _ambienceVolume;
            _fire.Play();
            for (var i = 0; i < _nearbyVoices.Length; i++)
            {
                var voiceObject = new GameObject("Nearby entity SFX voice " + (i + 1));
                voiceObject.transform.SetParent(transform, false);
                var voice = voiceObject.AddComponent<AudioSource>();
                voice.playOnAwake = false;
                voice.spatialBlend = 1f;
                voice.minDistance = 2f;
                voice.maxDistance = NearbyVoiceMaxDistance;
                voice.dopplerLevel = 0f;
                voice.rolloffMode = AudioRolloffMode.Linear;
                voice.priority = 160;
                voice.volume = _sfxVolume;
                _nearbyVoices[i] = voice;
            }
            EntityMotion.NearbyFootstep += OnNearbyFootstep;
            EntityMotion.NearbyMagicCast += OnNearbyMagicCast;
            EntityMotion.NearbyWorkAction += OnNearbyWorkAction;
        }

        public void SetVolumes(float sfx, float ambience)
        {
            _sfxVolume = Mathf.Clamp01(sfx);
            _ambienceVolume = Mathf.Clamp01(ambience);
            if (_oneShot != null) _oneShot.volume = _sfxVolume;
            if (_fire != null) _fire.volume = 0.42f * _ambienceVolume;
            for (var i = 0; i < _nearbyVoices.Length; i++)
                if (_nearbyVoices[i] != null) _nearbyVoices[i].volume = _sfxVolume;
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

        private void OnNearbyFootstep(Vector3 position, bool running)
        {
            PlayNearby(position, running ? _run : _step, running ? 0.62f : 0.48f);
        }

        private void OnNearbyMagicCast(Vector3 position)
        {
            PlayNearby(position, _magic, 0.64f);
        }

        private void OnNearbyWorkAction(Vector3 position, NpcActivitySoundKind kind)
        {
            AudioClip clip = null;
            var volume = 0.5f;
            switch (kind)
            {
                case NpcActivitySoundKind.Woodcut:
                    clip = _npcWoodcut;
                    volume = 0.66f;
                    break;
                case NpcActivitySoundKind.Forge:
                    clip = _npcForge;
                    volume = 0.62f;
                    break;
                case NpcActivitySoundKind.Fishing:
                    clip = _npcFishing;
                    volume = 0.48f;
                    break;
                case NpcActivitySoundKind.Fieldwork:
                    clip = _npcFieldwork;
                    volume = 0.46f;
                    break;
                case NpcActivitySoundKind.Mining:
                    clip = _npcMining;
                    volume = 0.58f;
                    break;
                case NpcActivitySoundKind.Hearth:
                    clip = _npcHearth;
                    volume = 0.42f;
                    break;
                case NpcActivitySoundKind.Drinking:
                    clip = _beer;
                    volume = 0.56f;
                    break;
                case NpcActivitySoundKind.Herding:
                    clip = _npcHerding;
                    volume = 0.36f;
                    break;
            }

            PlayNearby(position, clip, volume);
        }

        private void PlayNearby(Vector3 position, AudioClip clip, float volume)
        {
            if (_oneShot == null || clip == null || _nearbyVoices.Length == 0) return;
            // Remote entities are replicated as a bounded nearest-neighbor set, but some may
            // still be outside hearing range. Cull before taking a voice so distant activity
            // cannot steal one of the six audible slots from a nearby character.
            var listenerPosition = _oneShot.transform.position;
            if ((position - listenerPosition).sqrMagnitude >
                NearbyVoiceMaxDistance * NearbyVoiceMaxDistance) return;

            var voice = _nearbyVoices[_nearbyVoiceCursor++ % _nearbyVoices.Length];
            voice.transform.position = position;
            voice.Stop();
            voice.PlayOneShot(clip, volume);
        }

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

        private static AudioClip MakeMetalTap(string name, float fundamental, float amplitude)
        {
            const float duration = 0.22f;
            var length = Mathf.CeilToInt(Rate * duration);
            var data = new float[length];
            var random = new System.Random(name.GetHashCode());
            for (var i = 0; i < length; i++)
            {
                var t = i / (float)Rate;
                var decay = Mathf.Exp(-t * 23f);
                var partials = Mathf.Sin(2f * Mathf.PI * fundamental * t)
                    + 0.38f * Mathf.Sin(2f * Mathf.PI * fundamental * 2.17f * t)
                    + 0.16f * Mathf.Sin(2f * Mathf.PI * fundamental * 3.41f * t);
                var transient = ((float)random.NextDouble() * 2f - 1f) * Mathf.Exp(-t * 105f) * 0.22f;
                data[i] = (partials * 0.62f + transient) * decay * amplitude;
            }

            var clip = AudioClip.Create(name, data.Length, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        private void OnDestroy()
        {
            EntityMotion.NearbyFootstep -= OnNearbyFootstep;
            EntityMotion.NearbyMagicCast -= OnNearbyMagicCast;
            EntityMotion.NearbyWorkAction -= OnNearbyWorkAction;
            DestroyClip(_step); DestroyClip(_run); DestroyClip(_combat);
            DestroyClip(_magic); DestroyClip(_beer); DestroyClip(_fireLoop);
            DestroyClip(_npcWoodcut); DestroyClip(_npcForge); DestroyClip(_npcFishing);
            DestroyClip(_npcFieldwork); DestroyClip(_npcMining); DestroyClip(_npcHearth);
            DestroyClip(_npcHerding);
        }

        private static void DestroyClip(AudioClip clip) { if (clip != null) Destroy(clip); }
    }
}
