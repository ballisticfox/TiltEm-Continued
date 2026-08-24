using HarmonyLib;
using UnityEngine;

// ReSharper disable All

namespace TiltEm.Harmony
{
    /// <summary>
    /// Reads the debug menu's Set Orbit elements against the parent's equator instead of the
    /// celestial one.
    ///
    /// This is the input half of the same problem the maneuver node editor has on the output
    /// side. The Set Orbit screen never shows the vessel's current elements - every field starts
    /// blank or at a default - so the player is typing what they want, not editing what is
    /// there. What they want, essentially always, is expressed relative to the planet: "put me in
    /// an equatorial orbit" is inclination zero. Interpreting that in the celestial frame instead
    /// drops the craft into an orbit inclined by the planet's whole obliquity, which around a
    /// body like Eve is 35 degrees of surprise.
    ///
    /// Patching FlightGlobals.SetShipOrbit rather than the SetOrbit screen catches both stock
    /// entry points at once - the screen and the debug console's setorbit command run through
    /// here - and keeps the conversion in one place. It also means the two halves of this feature
    /// cannot disagree: an orbit entered as inclination zero reads back as inclination zero in
    /// the maneuver node editor, because both sides pivot on the same parent tilt.
    ///
    /// A prefix with ref parameters rather than a replacement: stock's own clamping, the SOI
    /// limit and the error checks all still run, on elements that are by then in the frame the
    /// rest of the method expects.
    ///
    /// Note this deliberately does not touch semi-major axis, eccentricity, mean anomaly or ObT.
    /// None of them describe the plane the orbit lies in, so a change of reference frame leaves
    /// them alone - see TiltEmFrames.OrbitElements.
    /// </summary>
    [HarmonyPatch(typeof(FlightGlobals))]
    [HarmonyPatch("SetShipOrbit")]
    internal class FlightGlobals_SetShipOrbit
    {
        [HarmonyPrefix]
        private static void PrefixSetShipOrbit(int selBodyIndex, ref double inc, ref double LAN, ref double argPe)
        {
            var bodies = FlightGlobals.Bodies;

            if (bodies == null || selBodyIndex < 0 || selBodyIndex >= bodies.Count) return;

            var body = bodies[selBodyIndex];

            TiltEmFrames.OrbitElements local;
            local.Inclination = inc;
            local.LongitudeOfAscendingNode = LAN;
            local.ArgumentOfPeriapsis = argPe;

            //False for an untilted body, which leaves the elements bit-identical rather than
            //round-tripped through a decomposition - so a stock system behaves exactly as before.
            if (!ParentRelativeOrbit.TryGetCelestialElements(body, local, out var celestial)) return;

            inc = celestial.Inclination;
            LAN = celestial.LongitudeOfAscendingNode;
            argPe = celestial.ArgumentOfPeriapsis;

            Debug.Log("[TiltEm]: Set Orbit elements read against " + body.bodyName + "'s equator: inc "
                      + local.Inclination.ToString("F3") + " -> " + celestial.Inclination.ToString("F3")
                      + ", LAN " + local.LongitudeOfAscendingNode.ToString("F3") + " -> "
                      + celestial.LongitudeOfAscendingNode.ToString("F3") + ", argPe "
                      + local.ArgumentOfPeriapsis.ToString("F3") + " -> "
                      + celestial.ArgumentOfPeriapsis.ToString("F3"));
        }
    }
}
