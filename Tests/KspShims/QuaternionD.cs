using System;

namespace UnityEngine
{
    /// <summary>
    /// KSP's double-precision quaternion, limited to the members the frame maths uses.
    /// </summary>
    //Behaviour matches the game's, down to the guards and the order of operations, because
    //the mod's correctness argument is that it composes frames exactly the way KSP does.
    //Anything unused is deliberately absent: a test that reaches for it should fail to
    //compile rather than run against an implementation nobody checked.
    public struct QuaternionD
    {
        public double x;
        public double y;
        public double z;
        public double w;

        public QuaternionD(double x, double y, double z, double w)
        {
            this.x = x;
            this.y = y;
            this.z = z;
            this.w = w;
        }

        public static QuaternionD identity => new QuaternionD(0.0, 0.0, 0.0, 1.0);

        /// <summary>Celestial to Unity: swaps the last two axes, which also flips handedness.</summary>
        //Appendix C of Docs/TILT_MATHEMATICS.pdf derives why this is a conjugation and why it
        //preserves composition order.
        public QuaternionD swizzle => new QuaternionD(0.0 - x, 0.0 - z, 0.0 - y, w);

        /// <summary>Rotation of <paramref name="angle"/> degrees about <paramref name="axis"/>.</summary>
        public static QuaternionD AngleAxis(double angle, Vector3d axis)
        {
            double magnitude = axis.magnitude;

            //Stock's guard, kept: a degenerate axis gives the identity rather than NaN.
            if (magnitude <= 0.0001) return identity;

            double cos = Math.Cos(angle * (Math.PI / 180.0) / 2.0);
            double sin = Math.Sin(angle * (Math.PI / 180.0) / 2.0);

            return new QuaternionD(axis.x / magnitude * sin,
                                   axis.y / magnitude * sin,
                                   axis.z / magnitude * sin,
                                   cos);
        }

        /// <summary>The conjugate, which stock uses unnormalized.</summary>
        public static QuaternionD Inverse(QuaternionD q)
        {
            return new QuaternionD(0.0 - q.x, 0.0 - q.y, 0.0 - q.z, q.w);
        }

        /// <summary>The three axis vectors of the rotation this quaternion represents.</summary>
        public void FrameVectors(out Vector3d frameX, out Vector3d frameY, out Vector3d frameZ)
        {
            frameX = new Vector3d(1.0 - 2.0 * y * y - 2.0 * z * z, 2.0 * x * y + 2.0 * w * z, 2.0 * x * z - 2.0 * w * y);
            frameY = new Vector3d(2.0 * x * y - 2.0 * w * z, 1.0 - 2.0 * x * x - 2.0 * z * z, 2.0 * y * z + 2.0 * w * x);
            frameZ = new Vector3d(2.0 * x * z + 2.0 * w * y, 2.0 * y * z - 2.0 * w * x, 1.0 - 2.0 * x * x - 2.0 * y * y);
        }

        public static double Dot(QuaternionD a, QuaternionD b)
        {
            return a.x * b.x + a.y * b.y + a.z * b.z + a.w * b.w;
        }

        public static QuaternionD operator *(QuaternionD lhs, QuaternionD rhs)
        {
            return new QuaternionD(
                lhs.w * rhs.x + lhs.x * rhs.w + lhs.y * rhs.z - lhs.z * rhs.y,
                lhs.w * rhs.y + lhs.y * rhs.w + lhs.z * rhs.x - lhs.x * rhs.z,
                lhs.w * rhs.z + lhs.z * rhs.w + lhs.x * rhs.y - lhs.y * rhs.x,
                lhs.w * rhs.w - lhs.x * rhs.x - lhs.y * rhs.y - lhs.z * rhs.z);
        }

        /// <summary>Rotates a point, expanded to the matrix form stock uses.</summary>
        //Not the shorter cross-product form: that rounds differently in the last bits, and
        //these tests resolve differences far below that.
        public static Vector3d operator *(QuaternionD rotation, Vector3d point)
        {
            double x2 = rotation.x * 2.0;
            double y2 = rotation.y * 2.0;
            double z2 = rotation.z * 2.0;
            double xx = rotation.x * x2;
            double yy = rotation.y * y2;
            double zz = rotation.z * z2;
            double xy = rotation.x * y2;
            double xz = rotation.x * z2;
            double yz = rotation.y * z2;
            double wx = rotation.w * x2;
            double wy = rotation.w * y2;
            double wz = rotation.w * z2;

            Vector3d result = default(Vector3d);
            result.x = (1.0 - (yy + zz)) * point.x + (xy - wz) * point.y + (xz + wy) * point.z;
            result.y = (xy + wz) * point.x + (1.0 - (xx + zz)) * point.y + (yz - wx) * point.z;
            result.z = (xz - wy) * point.x + (yz + wx) * point.y + (1.0 - (xx + yy)) * point.z;

            return result;
        }

        public override string ToString() => $"({x:F1}, {y:F1}, {z:F1}, {w:F1})";
    }
}
