using HarmonyLib;
using System;
using UnityEngine;

// ReSharper disable All

namespace TiltEm.Harmony
{
    /// <summary>
    /// Gives the in-flight cameras the focused body's pole instead of the celestial +Z axis.
    /// Covers the single-argument overload; the two-argument one (SpaceCenterCamera2) is not patched.
    /// </summary>
    //Stock writes Vector3.up wherever it means the spin axis, which is wrong for tilted bodies.
    //Patched at getFoR rather than GetCameraFoR because setTarget seeds lastFoR straight from
    //here, bypassing GetCameraFoR.
    [HarmonyPatch(typeof(FlightGlobals))]
    [HarmonyPatch("GetFoR")]
    [HarmonyPatch(new Type[] { typeof(FoRModes) })]
    internal class FlightGlobals_GetFoR
    {
        /// <summary>The local vertical, as getFoR assigned it before reaching the switch.</summary>
        private static readonly AccessTools.FieldRef<FlightGlobals, Vector3> FoRupAxis =
            AccessTools.FieldRefAccess<FlightGlobals, Vector3>("FoRupAxis");

        [HarmonyPostfix]
        private static void PostfixGetFoR(FoRModes mode, ref Quaternion __result)
        {
            //Every other mode builds its frame from the local vertical or the orbit, both of which
            //are already pole-independent.
            if (mode != FoRModes.OBT_ABS && mode != FoRModes.SRF_NORTH) return;

            Vessel vessel = FlightGlobals.ActiveVessel;
            if (vessel == null) return;

            //The System camera mode replaces the body's pole with the normal of the plane its
            //system orbits in, and only for the orbital frame: the surface frame is about which
            //way is north on the ground, which the system plane has nothing to say about.
            Vector3 north = mode == FoRModes.OBT_ABS && FlightCameraFrame.SystemUp
                ? BodyAxes.WorldSystemNorth(vessel.mainBody)
                : BodyAxes.WorldNorth(vessel.mainBody);

            //Exactly Vector3.up on untilted bodies. Early return keeps the result bit-identical
            //rather than accumulating rounding drift.
            if ((north - Vector3.up).sqrMagnitude < 1e-12f) return;

            __result = mode == FoRModes.OBT_ABS
                ? RebaseAboutPole(north, __result)
                : SurfaceNorthFrame(north, __result);
        }

        /// <summary>Re-expresses the orbital camera frame about the body's pole.</summary>
        //Stock has no case for OBT_ABS and falls through to the identity. Composed onto the
        //stock result so any future KSP frame for this mode survives and is simply re-based.
        private static Quaternion RebaseAboutPole(Vector3 north, Quaternion stock)
        {
            return Quaternion.FromToRotation(Vector3.up, north) * stock;
        }

        /// <summary>Rebuilds the surface-north camera frame using the body's pole.</summary>
        //Stock derives east by crossing the vertical against the celestial axis, not the spin
        //axis, so the camera's north leans off and the horizon rolls. Rebuilt rather than
        //composed because the forward vector is what changes; up is already pole-independent.
        private static Quaternion SurfaceNorthFrame(Vector3 north, Quaternion stock)
        {
            Vector3 up = FoRupAxis(FlightGlobals.fetch);
            Vector3 east = Vector3.Cross(north, up);

            //Directly over the pole the cross collapses. Stock is singular the same way, so
            //leaving its answer alone matches stock and keeps a zero vector out of LookRotation.
            if (east.sqrMagnitude < 1e-12f) return stock;

            return Quaternion.LookRotation(Quaternion.AngleAxis(90f, east) * -up, up);
        }
    }
}
