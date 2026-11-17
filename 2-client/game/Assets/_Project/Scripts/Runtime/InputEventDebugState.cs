using System;
using System.Globalization;

namespace Pogranichye.Client
{
    /// <summary>
    /// Allocation-light state for the most recent Input System action event.
    /// Kept separate from Unity so its formatting and time handling can be regression-tested.
    /// </summary>
    public sealed class InputEventDebugState
    {
        private string _action;
        private string _control;
        private string _phase;
        private float _recordedAt;
        private bool _hasEvent;

        public void Record(string action, string control, string phase, float unscaledTime)
        {
            _action = Normalize(action);
            _control = Normalize(control);
            _phase = Normalize(phase);
            _recordedAt = unscaledTime;
            _hasEvent = true;
        }

        public string Describe(float unscaledTime)
        {
            if (!_hasEvent) return "Последнее: нет";

            var age = Math.Max(0f, unscaledTime - _recordedAt);
            return string.Format(CultureInfo.InvariantCulture,
                "Последнее: {0}/{1}/{2} · {3:0.0}с назад",
                _action, _control, _phase, age);
        }

        private static string Normalize(string value)
        {
            return string.IsNullOrEmpty(value) ? "?" : value;
        }
    }
}
