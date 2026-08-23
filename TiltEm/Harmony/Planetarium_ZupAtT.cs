using HarmonyLib;

// ReSharper disable All

namespace TiltEm.Harmony
{
    /// <summary>
    /// Applies the tilt when the planetarium frame is asked for at an arbitrary time.
    ///
    /// This is called by Orbit.GetOrbitalStateVectorsAtTrueAnomaly, which Orbit.UpdateFromUT
    /// and therefore OrbitDriver.updateFromParameters go through. Without it, orbits of
    /// unloaded vessels and planets draw correctly but the bodies themselves sit in the wrong
    /// place on the map.
    ///
    /// Critically, it must agree with the static Planetarium.Zup that CBUpdate writes. Those
    /// two used to disagree by the whole tilt for the duration of a transition tick - Zup was
    /// still untilted while this returned a tilted frame - so position code that read one and
    /// orbit code that read the other landed a vessel in two different places. Both now come
    /// from the same TiltEmFrames.Zup call over the same anchor, so they cannot drift apart.
    /// </summary>
    [HarmonyPatch(typeof(Planetarium))]
    [HarmonyPatch("ZupAtT")]
    internal class Planetarium_ZupAtT
    {
        [HarmonyPrefix]
        private static bool PrefixZupAtT(double UT, CelestialBody body, ref Planetarium.CelestialFrame tempZup)
        {

            if (body == null || !body.inverseRotation || !TiltEm.TryGetTilt(body.bodyName, out var tilt))
            {
                return true;
            }

            var rotationAngle = TiltEm.RotationAngleAt(body, UT);

            tempZup = TiltEmFrames.Zup(TiltEm.ZupAnchor, tilt, rotationAngle - TiltEm.ZupAnchorRotationAngle);
            return false;
        }
    }
}
