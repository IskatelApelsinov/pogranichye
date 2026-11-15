using System;

namespace Pogranichye.Client.World
{
    /// <summary>Global world coordinate in metres. Never store this in a Unity Transform.</summary>
    public struct WorldOriginPosition : IEquatable<WorldOriginPosition>
    {
        public readonly int Frame;
        public readonly double East;
        public readonly double North;
        public readonly double Up;

        public WorldOriginPosition(int frame, double east, double north, double up)
        {
            if (frame < 0) throw new ArgumentOutOfRangeException("frame");
            if (!IsFinite(east)) throw new ArgumentOutOfRangeException("east");
            if (!IsFinite(north)) throw new ArgumentOutOfRangeException("north");
            if (!IsFinite(up)) throw new ArgumentOutOfRangeException("up");
            Frame = frame;
            East = east;
            North = north;
            Up = up;
        }

        public bool Equals(WorldOriginPosition other)
        {
            return Frame == other.Frame && East.Equals(other.East) &&
                North.Equals(other.North) && Up.Equals(other.Up);
        }

        public override bool Equals(object obj)
        {
            return obj is WorldOriginPosition && Equals((WorldOriginPosition)obj);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = Frame;
                hash = hash * 397 ^ East.GetHashCode();
                hash = hash * 397 ^ North.GetHashCode();
                hash = hash * 397 ^ Up.GetHashCode();
                return hash;
            }
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }

    /// <summary>Double-precision local offset. Convert to Unity float only at the render boundary.</summary>
    public struct WorldLocalPosition
    {
        public readonly double X;
        public readonly double Y;
        public readonly double Z;

        public WorldLocalPosition(double x, double y, double z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public bool IsFinite
        {
            get
            {
                return !double.IsNaN(X) && !double.IsInfinity(X) &&
                    !double.IsNaN(Y) && !double.IsInfinity(Y) &&
                    !double.IsNaN(Z) && !double.IsInfinity(Z);
            }
        }
    }

    public struct WorldOriginShift
    {
        public readonly WorldOriginPosition Previous;
        public readonly WorldOriginPosition Current;

        public WorldOriginShift(WorldOriginPosition previous, WorldOriginPosition current)
        {
            Previous = previous;
            Current = current;
        }

        public double EastDelta { get { return Current.East - Previous.East; } }
        public double NorthDelta { get { return Current.North - Previous.North; } }
        public double UpDelta { get { return Current.Up - Previous.Up; } }
    }

    /// <summary>
    /// Pure double-precision floating-origin state. It does not move Transforms itself:
    /// the runtime coordinator must apply the returned delta atomically to every presentation root.
    /// </summary>
    public sealed class WorldOriginState
    {
        public const double DefaultShiftThresholdMeters = 2048d;
        public const double DefaultHorizontalQuantumMeters = 256d;
        public const double DefaultVerticalQuantumMeters = 64d;

        private WorldOriginPosition _origin;
        private bool _initialized;
        private long _generation;

        public bool IsInitialized { get { return _initialized; } }
        public long Generation { get { return _generation; } }
        public WorldOriginPosition Origin
        {
            get
            {
                if (!_initialized) throw new InvalidOperationException("World origin is not initialized.");
                return _origin;
            }
        }

        public void Initialize(WorldOriginPosition origin)
        {
            _origin = origin;
            _initialized = true;
            _generation = checked(_generation + 1);
        }

        public bool TryToLocal(WorldOriginPosition world, out WorldLocalPosition local)
        {
            local = default(WorldLocalPosition);
            if (!_initialized || world.Frame != _origin.Frame) return false;
            local = new WorldLocalPosition(
                world.East - _origin.East,
                world.Up - _origin.Up,
                world.North - _origin.North);
            return local.IsFinite;
        }

        public bool TryToGlobal(WorldLocalPosition local, out WorldOriginPosition world)
        {
            world = default(WorldOriginPosition);
            if (!_initialized || !local.IsFinite) return false;
            var east = _origin.East + local.X;
            var north = _origin.North + local.Z;
            var up = _origin.Up + local.Y;
            if (!IsFinite(east) || !IsFinite(north) || !IsFinite(up)) return false;
            world = new WorldOriginPosition(_origin.Frame, east, north, up);
            return true;
        }

        public bool TryRebase(WorldOriginPosition player, out WorldOriginShift shift)
        {
            return TryRebase(player, DefaultShiftThresholdMeters,
                DefaultHorizontalQuantumMeters, DefaultVerticalQuantumMeters, out shift);
        }

        public bool TryRebase(WorldOriginPosition player, double thresholdMeters,
            double horizontalQuantumMeters, double verticalQuantumMeters, out WorldOriginShift shift)
        {
            shift = default(WorldOriginShift);
            if (!_initialized) throw new InvalidOperationException("Initialize the world origin before rebasing.");
            if (player.Frame != _origin.Frame) return false;
            if (!IsFinite(thresholdMeters) || thresholdMeters <= 0d)
                throw new ArgumentOutOfRangeException("thresholdMeters");
            if (!IsFinite(horizontalQuantumMeters) || horizontalQuantumMeters <= 0d)
                throw new ArgumentOutOfRangeException("horizontalQuantumMeters");
            if (!IsFinite(verticalQuantumMeters) || verticalQuantumMeters <= 0d)
                throw new ArgumentOutOfRangeException("verticalQuantumMeters");

            var dx = player.East - _origin.East;
            var dz = player.North - _origin.North;
            var dy = player.Up - _origin.Up;
            if (Math.Sqrt(dx * dx + dz * dz) <= thresholdMeters &&
                Math.Abs(dy) <= thresholdMeters)
                return false;

            var next = new WorldOriginPosition(player.Frame,
                Snap(player.East, horizontalQuantumMeters),
                Snap(player.North, horizontalQuantumMeters),
                Snap(player.Up, verticalQuantumMeters));
            if (next.Equals(_origin)) return false;

            var previous = _origin;
            _origin = next;
            _generation = checked(_generation + 1);
            shift = new WorldOriginShift(previous, next);
            return true;
        }

        private static double Snap(double value, double quantum)
        {
            return Math.Round(value / quantum, MidpointRounding.AwayFromZero) * quantum;
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }
}