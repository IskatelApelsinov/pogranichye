using System.Collections.Generic;
using UnityEngine;

namespace Pogranichye.Client.Presentation
{
    /// <summary>
    /// Code-driven animation for primitive creatures and the mount: no clips, no rig. The animator only reads
    /// how fast the root actually moves (server-confirmed positions, interpolated) and a few flags set from
    /// confirmed state, and turns that into a walk/trot leg cycle, body bob, breathing, tail and head motion,
    /// an attack lunge and a collapse. It never moves the root itself and decides nothing about gameplay.
    /// </summary>
    public sealed class ProceduralGait : MonoBehaviour
    {
        public readonly List<Transform> Legs = new List<Transform>();   // order: front-left, front-right, hind-left, hind-right (or arms for humanoids)
        public Transform Body;
        public Transform Head;
        public Transform Tail;
        public float LegLength = 0.4f;
        public float StrideDegrees = 28f;
        public bool Humanoid;
        public bool Dead;
        public bool Chasing;   // confirmed "chase" state: lower head, faster cadence
        public bool Eating;    // confirmed "eat" state: head to the ground

        private readonly List<Vector3> _legRest = new List<Vector3>();
        private Vector3 _bodyRest;
        private Vector3 _headRest;
        private Quaternion _headRestRotation;
        private Vector3 _lastPosition;
        private float _speed;
        private float _phase;
        private float _lunge;
        private bool _captured;
        private float _motionSeed;

        /// <summary>Plays a short forward lunge (call when a confirmed attack event arrives).</summary>
        public void Lunge()
        {
            _lunge = 1f;
        }

        private void Capture()
        {
            _captured = true;
            _legRest.Clear();
            foreach (var leg in Legs) _legRest.Add(leg != null ? leg.localPosition : Vector3.zero);
            if (Body != null) _bodyRest = Body.localPosition;
            if (Head != null)
            {
                _headRest = Head.localPosition;
                _headRestRotation = Head.localRotation;
            }
            _lastPosition = transform.position;
            // GetInstanceID is obsolete in current Unity 6. Derive a stable per-view phase instead.
            unchecked
            {
                var hash = 17;
                var objectName = gameObject.name;
                for (var i = 0; i < objectName.Length; i++) hash = hash * 31 + objectName[i];
                _motionSeed = hash * 0.0137f + transform.position.x * 1.71f + transform.position.z * 2.37f;
            }
        }

        private void LateUpdate()
        {
            if (!_captured) Capture();
            var dt = Mathf.Max(Time.deltaTime, 0.0001f);
            var moved = transform.position - _lastPosition;
            moved.y = 0f;
            _lastPosition = transform.position;
            _speed = Mathf.Lerp(_speed, moved.magnitude / dt, 1f - Mathf.Exp(-8f * dt));
            if (Dead)
            {
                return; // the owner lays the body on its side; limbs stay where they were
            }

            // Cadence follows ground speed so feet do not slide: one cycle per ~2.2 leg lengths travelled.
            var stride = Mathf.Max(0.2f, LegLength * 2.2f);
            _phase += _speed / stride * Mathf.PI * 2f * dt;
            var moving = Mathf.Clamp01(_speed / 0.6f);
            var swing = StrideDegrees * moving * (Chasing ? 1.25f : 1f);

            for (var i = 0; i < Legs.Count; i++)
            {
                var leg = Legs[i];
                if (leg == null) continue;
                // diagonal pairs move together (trot); for humanoids the two arms swing in opposition
                var offset = Humanoid ? (i % 2 == 0 ? 0f : Mathf.PI) : ((i == 0 || i == 3) ? 0f : Mathf.PI);
                var angle = Mathf.Sin(_phase + offset) * swing;
                var lift = Mathf.Max(0f, Mathf.Cos(_phase + offset)) * 0.12f * LegLength * moving;
                leg.localRotation = Quaternion.Euler(angle, 0f, 0f);
                // rotate about the hip: shift the box so its top stays put
                var rest = _legRest[i];
                var half = LegLength * 0.5f;
                leg.localPosition = rest + new Vector3(0f, lift + half * (1f - Mathf.Cos(angle * Mathf.Deg2Rad)), half * Mathf.Sin(angle * Mathf.Deg2Rad));
            }

            _lunge = Mathf.Max(0f, _lunge - dt * 3.5f);
            var lunge = Mathf.Sin(_lunge * Mathf.PI) * 0.35f;
            var breathe = Mathf.Sin(Time.time * 1.6f + _motionSeed) * 0.012f;
            var bob = Mathf.Abs(Mathf.Sin(_phase)) * 0.05f * moving * LegLength * 2f;
            if (Body != null) Body.localPosition = _bodyRest + new Vector3(0f, bob + breathe, lunge);

            if (Head != null)
            {
                var nod = Mathf.Sin(_phase * 2f) * 3f * moving;
                var drop = Eating ? 55f : Chasing ? 14f : 0f;
                var look = moving < 0.1f && !Eating ? Mathf.Sin(Time.time * 0.5f + _motionSeed) * 12f : 0f;
                Head.localRotation = _headRestRotation * Quaternion.Euler(drop + nod, look, 0f);
                Head.localPosition = _headRest + new Vector3(0f, bob - (Eating ? LegLength * 0.6f : 0f), lunge);
            }

            if (Tail != null)
            {
                var wag = Mathf.Sin(Time.time * (Chasing ? 9f : 3f) + _motionSeed) * (Chasing ? 8f : 16f);
                Tail.localRotation = Quaternion.Euler(Chasing ? -20f : 12f, wag, 0f);
            }
        }
    }
}
