using UnityEngine;

namespace TiltEm
{
    /// <summary>
    /// Shared number formatting for the debug tabs.
    /// </summary>
    //One place, so a column in the bodies table and the same value in a row read identically.
    internal static class DebugFormat
    {
        /// <summary>Euler angles of a rotation, as three fixed-width degree values.</summary>
        public static string Euler(QuaternionD rotation)
        {
            return Euler((Quaternion)rotation);
        }

        public static string Euler(Quaternion rotation)
        {
            return Vector(rotation.eulerAngles);
        }

        /// <summary>Euler angles with no decimals or spaces, for a narrow table column.</summary>
        public static string EulerCompact(QuaternionD rotation)
        {
            return EulerCompact((Quaternion)rotation);
        }

        public static string EulerCompact(Quaternion rotation)
        {
            Vector3 v = rotation.eulerAngles;

            return v.x.ToString("F0") + "," + v.y.ToString("F0") + "," + v.z.ToString("F0");
        }

        public static string Vector(Vector3 v)
        {
            return v.x.ToString("F1") + ", " + v.y.ToString("F1") + ", " + v.z.ToString("F1");
        }

        public static string Vector(Vector3d v)
        {
            return v.x.ToString("F1") + ", " + v.y.ToString("F1") + ", " + v.z.ToString("F1");
        }
    }
}
