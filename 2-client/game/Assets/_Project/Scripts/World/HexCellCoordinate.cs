using System;

namespace Pogranichye.Client.World
{
    /// <summary>
    /// Stable logical hex-cell coordinates for a single WorldPos frame.
    /// This is an indexing layer only; terrain remains addressed by square 256 m chunks.
    /// Pointy-top axial layout, with E as +X and N as +Z.
    /// </summary>
    public struct HexCellCoordinate : IEquatable<HexCellCoordinate>
    {
        private static readonly long[,] NeighborOffsets =
        {
            { 1, 0 }, { 1, -1 }, { 0, -1 },
            { -1, 0 }, { -1, 1 }, { 0, 1 }
        };

        public readonly int Frame;
        public readonly long Q;
        public readonly long R;

        public HexCellCoordinate(int frame, long q, long r)
        {
            if (frame < 0) throw new ArgumentOutOfRangeException("frame");
            Frame = frame;
            Q = q;
            R = r;
        }

        public string Id { get { return Frame + ":" + Q + ":" + R; } }

        public HexCellCoordinate Neighbor(int direction)
        {
            if (direction < 0 || direction >= 6)
                throw new ArgumentOutOfRangeException("direction");
            return new HexCellCoordinate(Frame,
                checked(Q + NeighborOffsets[direction, 0]),
                checked(R + NeighborOffsets[direction, 1]));
        }

        public long DistanceTo(HexCellCoordinate other)
        {
            if (Frame != other.Frame)
                throw new ArgumentException("Hex cells from different world frames cannot be compared.", "other");
            var dq = checked(Q - other.Q);
            var dr = checked(R - other.R);
            var ds = checked(dq + dr);
            return checked((Abs(dq) + Abs(dr) + Abs(ds)) / 2);
        }

        public static HexCellCoordinate FromWorld(int frame, double east, double north, double radiusMeters)
        {
            if (frame < 0) throw new ArgumentOutOfRangeException("frame");
            if (!IsFinite(east)) throw new ArgumentOutOfRangeException("east");
            if (!IsFinite(north)) throw new ArgumentOutOfRangeException("north");
            if (!IsFinite(radiusMeters) || radiusMeters <= 0d)
                throw new ArgumentOutOfRangeException("radiusMeters");

            // Inverse pointy-top axial transform.
            var q = (Math.Sqrt(3d) / 3d * east - north / 3d) / radiusMeters;
            var r = (2d / 3d * north) / radiusMeters;
            var x = q;
            var z = r;
            var y = -x - z;

            var rx = Math.Round(x, MidpointRounding.AwayFromZero);
            var ry = Math.Round(y, MidpointRounding.AwayFromZero);
            var rz = Math.Round(z, MidpointRounding.AwayFromZero);
            var dx = Math.Abs(rx - x);
            var dy = Math.Abs(ry - y);
            var dz = Math.Abs(rz - z);

            // Deterministic tie order: repair X first, then Y, then Z.
            if (dx >= dy && dx >= dz) rx = -ry - rz;
            else if (dy >= dz) ry = -rx - rz;
            else rz = -rx - ry;

            // Keep a conservative exact-integer range; beyond this, double cannot
            // represent every adjacent axial coordinate reliably.
            const double ExactIntegerLimit = 9007199254740991d; // 2^53 - 1
            if (Math.Abs(rx) > ExactIntegerLimit || Math.Abs(rz) > ExactIntegerLimit)
                throw new ArgumentOutOfRangeException("east", "World position exceeds exact axial coordinate range.");

            return new HexCellCoordinate(frame, checked((long)rx), checked((long)rz));
        }

        public void GetCenter(double radiusMeters, out double east, out double north)
        {
            if (!IsFinite(radiusMeters) || radiusMeters <= 0d)
                throw new ArgumentOutOfRangeException("radiusMeters");
            east = radiusMeters * Math.Sqrt(3d) * (Q + R * 0.5d);
            north = radiusMeters * 1.5d * R;
        }

        public bool Equals(HexCellCoordinate other)
        {
            return Frame == other.Frame && Q == other.Q && R == other.R;
        }

        public override bool Equals(object obj)
        {
            return obj is HexCellCoordinate && Equals((HexCellCoordinate)obj);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = Frame;
                hash = hash * 397 ^ Q.GetHashCode();
                hash = hash * 397 ^ R.GetHashCode();
                return hash;
            }
        }

        public override string ToString() { return Id; }

        public static bool operator ==(HexCellCoordinate left, HexCellCoordinate right) { return left.Equals(right); }
        public static bool operator !=(HexCellCoordinate left, HexCellCoordinate right) { return !left.Equals(right); }

        private static long Abs(long value)
        {
            if (value == long.MinValue) throw new OverflowException();
            return Math.Abs(value);
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }
}
