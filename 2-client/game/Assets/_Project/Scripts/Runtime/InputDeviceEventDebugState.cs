using System;
using System.Globalization;

namespace Pogranichye.Client
{
    /// <summary>
    /// Bounded, allocation-light summary of raw Input System event traffic for the F3 overlay.
    /// It records only while diagnostics are enabled and keeps the latest processed frame.
    /// </summary>
    public sealed class InputDeviceEventDebugState
    {
        private const int MaxTrackedDevices = 6;

        private struct DeviceStats
        {
            public bool Used;
            public int Id;
            public string Name;
            public long Bytes;
            public int Events;
            public int LargestEventBytes;
        }

        private readonly DeviceStats[] _devices = new DeviceStats[MaxTrackedDevices];
        private int _frame = -1;
        private long _totalBytes;
        private int _totalEvents;
        private int _largestEventBytes;
        private long _otherBytes;
        private int _otherEvents;

        public void Record(int deviceId, string deviceName, int eventBytes, int frame)
        {
            if (deviceId < 0 || frame < 0) return;
            if (_frame > frame) return;
            if (_frame != frame) ResetFrame(frame);

            if (eventBytes < 0) eventBytes = 0;
            _totalBytes += eventBytes;
            if (_totalEvents < int.MaxValue) _totalEvents++;
            if (eventBytes > _largestEventBytes) _largestEventBytes = eventBytes;

            var slot = FindDevice(deviceId);
            if (slot < 0)
            {
                _otherBytes += eventBytes;
                if (_otherEvents < int.MaxValue) _otherEvents++;
                return;
            }

            var stats = _devices[slot];
            stats.Bytes += eventBytes;
            if (stats.Events < int.MaxValue) stats.Events++;
            if (eventBytes > stats.LargestEventBytes) stats.LargestEventBytes = eventBytes;
            _devices[slot] = stats;
        }

        public string Describe(int currentFrame, int budgetBytes)
        {
            var budget = budgetBytes > 0 ? FormatBytes(budgetBytes) : "без ограничения";
            if (_frame < 0 || _totalEvents == 0)
                return "события: нет · лимит " + budget;

            var age = Math.Max(0, currentFrame - _frame);
            var ageText = age == 0 ? "сейчас" : age + " кадр. назад";
            var summary = string.Format(CultureInfo.InvariantCulture,
                "кадр {0}: {1} / {2} событий · пик {3} · лимит {4}",
                ageText, FormatBytes(_totalBytes), _totalEvents, FormatBytes(_largestEventBytes), budget);

            var first = -1;
            var second = -1;
            for (var i = 0; i < _devices.Length; i++)
            {
                if (!_devices[i].Used) continue;
                if (first < 0 || _devices[i].Bytes > _devices[first].Bytes)
                {
                    second = first;
                    first = i;
                }
                else if (second < 0 || _devices[i].Bytes > _devices[second].Bytes)
                {
                    second = i;
                }
            }

            var deviceLine = DescribeDevice(first);
            var secondLine = DescribeDevice(second);
            if (!string.IsNullOrEmpty(secondLine))
                deviceLine = string.IsNullOrEmpty(deviceLine) ? secondLine : deviceLine + " · " + secondLine;
            if (_otherEvents > 0)
            {
                var other = "прочие " + FormatBytes(_otherBytes) + "/" + _otherEvents;
                deviceLine = string.IsNullOrEmpty(deviceLine) ? other : deviceLine + " · " + other;
            }

            return string.IsNullOrEmpty(deviceLine) ? summary : summary + "\n" + deviceLine;
        }

        public void Reset()
        {
            Array.Clear(_devices, 0, _devices.Length);
            _frame = -1;
            _totalBytes = 0;
            _totalEvents = 0;
            _largestEventBytes = 0;
            _otherBytes = 0;
            _otherEvents = 0;
        }

        private int FindDevice(int deviceId)
        {
            var empty = -1;
            for (var i = 0; i < _devices.Length; i++)
            {
                if (_devices[i].Used && _devices[i].Id == deviceId) return i;
                if (!_devices[i].Used && empty < 0) empty = i;
            }

            if (empty < 0) return -1;
            _devices[empty] = new DeviceStats
            {
                Used = true,
                Id = deviceId,
                Name = NormalizeName(null)
            };
            return empty;
        }

        private string DescribeDevice(int index)
        {
            if (index < 0) return string.Empty;
            var stats = _devices[index];
            return stats.Name + " " + FormatBytes(stats.Bytes) + "/" + stats.Events;
        }

        private void ResetFrame(int frame)
        {
            Array.Clear(_devices, 0, _devices.Length);
            _frame = frame;
            _totalBytes = 0;
            _totalEvents = 0;
            _largestEventBytes = 0;
            _otherBytes = 0;
            _otherEvents = 0;
        }

        private static string NormalizeName(string name)
        {
            var value = string.IsNullOrWhiteSpace(name) ? "устройство" : name.Trim();
            return value.Length <= 24 ? value : value.Substring(0, 23) + "…";
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes < 1024) return bytes.ToString(CultureInfo.InvariantCulture) + " B";
            if (bytes < 1024L * 1024L)
                return (bytes / 1024d).ToString("0.0", CultureInfo.InvariantCulture) + " KiB";
            return (bytes / (1024d * 1024d)).ToString("0.0", CultureInfo.InvariantCulture) + " MiB";
        }
    }
}
