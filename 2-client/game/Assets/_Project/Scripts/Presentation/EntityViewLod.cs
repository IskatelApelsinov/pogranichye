using UnityEngine;
using UnityEngine.Rendering;

namespace Pogranichye.Client.Presentation
{
    /// <summary>
    /// Lightweight distance-based presentation LOD for replicated entities.
    /// It never changes replication, identity, interaction range, or gameplay state.
    /// </summary>
    public sealed class EntityViewLod : MonoBehaviour
    {
        private const float HideLabelDistance = 24f;
        private const float ShowLabelDistance = 21f;
        private const float DisableShadowsDistance = 18f;
        private const float EnableShadowsDistance = 15f;

        private GameObject _label;
        private Renderer[] _renderers;
        private bool[] _rendererInitiallyEnabled;
        private bool _silhouetteEnabled;
        private EntityMotion _motion;
        private int _animationTier;
        private bool _labelVisible = true;
        private bool _shadowsEnabled = true;

        public void Initialize(GameObject label, Renderer[] renderers)
        {
            _label = label;
            _renderers = renderers;
            _rendererInitiallyEnabled = new bool[renderers != null ? renderers.Length : 0];
            for (var i = 0; i < _rendererInitiallyEnabled.Length; i++)
                _rendererInitiallyEnabled[i] = renderers[i] != null && renderers[i].enabled;
            _motion = GetComponent<EntityMotion>();
        }

        public void ApplyDistance(float distance)
        {
            UpdateAnimationTier(distance);

            if (_label != null)
            {
                if (_labelVisible && distance >= HideLabelDistance)
                {
                    _labelVisible = false;
                    _label.SetActive(false);
                }
                else if (!_labelVisible && distance <= ShowLabelDistance)
                {
                    _labelVisible = true;
                    _label.SetActive(true);
                }
            }

            if (_shadowsEnabled && distance >= DisableShadowsDistance)
            {
                _shadowsEnabled = false;
                SetShadowMode(false);
            }
            else if (!_shadowsEnabled && distance <= EnableShadowsDistance)
            {
                _shadowsEnabled = true;
                SetShadowMode(true);
            }
        }

        private void UpdateAnimationTier(float distance)
        {
            // 0 = full rate, 1 = 20 Hz, 2 = approximately 8 Hz.
            // Separate enter/exit thresholds prevent rapid switching near a boundary.
            var previousTier = _animationTier;
            if (_animationTier == 0)
            {
                if (distance >= 42f) _animationTier = 2;
                else if (distance >= 16f) _animationTier = 1;
            }
            else if (_animationTier == 1)
            {
                if (distance >= 42f) _animationTier = 2;
                else if (distance <= 12f) _animationTier = 0;
            }
            else
            {
                if (distance <= 12f) _animationTier = 0;
                else if (distance <= 38f) _animationTier = 1;
            }

            if (previousTier != _animationTier)
                SetSilhouette(_animationTier == 2);

            if (_motion != null)
                _motion.SetPresentationUpdateInterval(_animationTier == 0 ? 0f :
                    (_animationTier == 1 ? 0.05f : 0.12f));
        }

        private void SetSilhouette(bool enabled)
        {
            if (_renderers == null) return;
            for (var i = 0; i < _renderers.Length; i++)
            {
                var renderer = _renderers[i];
                if (renderer == null) continue;

                // The far tier keeps only the torso and head meshes. Limbs, hands,
                // clothing details and held props return when the entity comes closer.
                var isCoreSilhouettePart = renderer.transform.name == "Body" ||
                    renderer.transform.name == "Head";
                renderer.enabled = _rendererInitiallyEnabled[i] &&
                    (!enabled || isCoreSilhouettePart);
            }
        }

        private void SetShadowMode(bool enabled)
        {
            if (_renderers == null) return;
            for (var i = 0; i < _renderers.Length; i++)
            {
                var renderer = _renderers[i];
                if (renderer == null) continue;
                renderer.shadowCastingMode = enabled
                    ? ShadowCastingMode.On
                    : ShadowCastingMode.Off;
                renderer.receiveShadows = enabled;
            }
        }
    }
}
