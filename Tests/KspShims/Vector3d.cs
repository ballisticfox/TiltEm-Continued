using System;

/// <summary>
/// KSP's double-precision vector, as much of it as the tests touch.
/// </summary>
//Global namespace on purpose: KSP declares Vector3d in Assembly-CSharp-firstpass with no
//namespace, and the tests have to compile unchanged against either this or the real thing.
public struct Vector3d
{
    public double x;
    public double y;
    public double z;

    public Vector3d(double x, double y, double z)
    {
        this.x = x;
        this.y = y;
        this.z = z;
    }

    public static Vector3d zero => new Vector3d(0.0, 0.0, 0.0);
    public static Vector3d one => new Vector3d(1.0, 1.0, 1.0);
    public static Vector3d right => new Vector3d(1.0, 0.0, 0.0);
    public static Vector3d left => new Vector3d(-1.0, 0.0, 0.0);
    public static Vector3d up => new Vector3d(0.0, 1.0, 0.0);
    public static Vector3d down => new Vector3d(0.0, -1.0, 0.0);
    public static Vector3d forward => new Vector3d(0.0, 0.0, 1.0);
    public static Vector3d back => new Vector3d(0.0, 0.0, -1.0);

    public double sqrMagnitude => x * x + y * y + z * z;
    public double magnitude => Math.Sqrt(sqrMagnitude);

    /// <summary>Stock's guard, kept: a zero vector normalizes to zero rather than NaN.</summary>
    public Vector3d normalized
    {
        get
        {
            double m = magnitude;
            return m > 0.0 ? this / m : zero;
        }
    }

    /// <summary>Swaps y and z, converting between KSP's celestial frame and Unity's world axes.</summary>
    public Vector3d xzy => new Vector3d(x, z, y);

    public static Vector3d operator +(Vector3d a, Vector3d b) => new Vector3d(a.x + b.x, a.y + b.y, a.z + b.z);
    public static Vector3d operator -(Vector3d a, Vector3d b) => new Vector3d(a.x - b.x, a.y - b.y, a.z - b.z);
    public static Vector3d operator -(Vector3d a) => new Vector3d(-a.x, -a.y, -a.z);
    public static Vector3d operator *(Vector3d a, double d) => new Vector3d(a.x * d, a.y * d, a.z * d);
    public static Vector3d operator *(double d, Vector3d a) => new Vector3d(a.x * d, a.y * d, a.z * d);
    public static Vector3d operator /(Vector3d a, double d) => new Vector3d(a.x / d, a.y / d, a.z / d);

    public static double Dot(Vector3d a, Vector3d b) => a.x * b.x + a.y * b.y + a.z * b.z;

    public static Vector3d Cross(Vector3d a, Vector3d b)
    {
        return new Vector3d(a.y * b.z - a.z * b.y,
                            a.z * b.x - a.x * b.z,
                            a.x * b.y - a.y * b.x);
    }

    public override string ToString() => "[" + x + ", " + y + ", " + z + "]";
}
