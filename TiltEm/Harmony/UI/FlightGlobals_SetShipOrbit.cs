using HarmonyLib;
using System.Collections.Generic;
using OrbitElements = TiltEm.TiltEmFrames.OrbitElements;

// ReSharper disable All

namespace TiltEm.Harmony
{
    /// <summary>
    /// Reads the debug menu's Set Orbit elements against the parent's equator instead of the
    /// celestial one, so inclination zero means an equatorial orbit around a tilted planet.
    ///
    /// Only the three orientation elements change. Semi-major axis, eccentricity and the
    /// anomalies do not describe the plane, so a change of frame leaves them alone. See section
    /// 8.2 of Docs/TILT_MATHEMATICS.pdf.
    /// </summary>
    [HarmonyPatch(typeof(FlightGlobals))]
    [HarmonyPatch("SetShipOrbit")]
    internal class FlightGlobals_SetShipOrbit
    {
        [HarmonyPrefix]
        private static void PrefixSetShipOrbit(int selBodyIndex, ref double inc, ref double LAN, ref double argPe)
        {
            List<CelestialBody> bodies = FlightGlobals.Bodies;

            if (bodies == null || selBodyIndex < 0 || selBodyIndex >= bodies.Count) return;

            CelestialBody body = bodies[selBodyIndex];
            var local = new OrbitElements(inc, LAN, argPe);

            //False for an untilted body, which leaves the elements bit-identical.
            if (!ParentRelativeOrbit.TryGetCelestialElements(body, local, out OrbitElements celestial))
                return;

            inc = celestial.Inclination;
            LAN = celestial.LongitudeOfAscendingNode;
            argPe = celestial.ArgumentOfPeriapsis;
        }
    }
}
