using System;

/// <summary>
/// Stands in for KSP's Planetarium, carrying only the nested CelestialFrame the frame maths
/// is built on.
/// </summary>
public class Planetarium
{
    /// <summary>
    /// A rotation matrix held as its three rows. Section 2 of Docs/TILT_MATHEMATICS.pdf
    /// records the stock implementation this reproduces.
    /// </summary>
    public struct CelestialFrame
    {
        public Vector3d X;
        public Vector3d Y;
        public Vector3d Z;

        /// <summary>Multiplication by the transpose of M: out of the frame's basis, into it.</summary>
        //The names are stock's and read backwards: "World" is the basis the rows are written
        //in, "Local" is this frame's own axes.
        public Vector3d WorldToLocal(Vector3d r)
        {
            double x = Vector3d.Dot(r, X);
            double y = Vector3d.Dot(r, Y);
            double z = Vector3d.Dot(r, Z);

            return new Vector3d(x, y, z);
        }

        /// <summary>Multiplication by M.</summary>
        public Vector3d LocalToWorld(Vector3d r)
        {
            return r.x * X + r.y * Y + r.z * Z;
        }

        /// <summary>The generator both public entry points reduce to. Angles in radians.</summary>
        public static void SetFrame(double A, double B, double C, ref CelestialFrame cf)
        {
            double cosA = Math.Cos(A);
            double sinA = Math.Sin(A);
            double cosB = Math.Cos(B);
            double sinB = Math.Sin(B);
            double cosC = Math.Cos(C);
            double sinC = Math.Sin(C);

            cf.X = new Vector3d(cosA * cosC - sinA * cosB * sinC, sinA * cosC + cosA * cosB * sinC, sinB * sinC);
            cf.Y = new Vector3d((0.0 - cosA) * sinC - sinA * cosB * cosC, (0.0 - sinA) * sinC + cosA * cosB * cosC, sinB * cosC);
            cf.Z = new Vector3d(sinA * sinB, (0.0 - cosA) * sinB, cosB);
        }

        /// <summary>A frame named by its orbital elements, in degrees.</summary>
        public static void OrbitalFrame(double LAN, double Inc, double ArgPe, ref CelestialFrame cf)
        {
            LAN *= Math.PI / 180.0;
            Inc *= Math.PI / 180.0;
            ArgPe *= Math.PI / 180.0;

            SetFrame(LAN, Inc, ArgPe, ref cf);
        }

        /// <summary>A frame named by where its third axis points: the pole's right ascension
        /// and declination, in degrees, plus a spin about it.</summary>
        public static void PlanetaryFrame(double ra, double dec, double rot, ref CelestialFrame cf)
        {
            ra = (ra - 90.0) * Math.PI / 180.0;
            dec = (dec - 90.0) * Math.PI / 180.0;
            rot = (rot + 90.0) * Math.PI / 180.0;

            SetFrame(ra, dec, rot, ref cf);
        }
    }
}
