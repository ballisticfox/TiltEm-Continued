using HarmonyLib;
using System;
using UnityEngine;

// ReSharper disable All

namespace TiltEm.Harmony
{
    /// <summary>
    /// Gives the in-flight cameras the focused body's pole instead of the celestial +Z axis.
    ///
    /// Two of FlightGlobals.getFoR's cases are anchored to a global axis, and both are wrong for
    /// a tilted body in the same way: stock can write Vector3.up wherever it means "the body's
    /// spin axis", because for every stock body those are the same direction.
    ///
    /// FoRModes.OBT_ABS - the Orbital camera - has no case in the switch at all and falls through
    /// to "default: return Quaternion.identity". FlightCamera uses that as
    ///
    ///     pivot.rotation = frameOfReference * AngleAxis(camHdg, Vector3.up) * AngleAxis(pitch, Vector3.right)
    ///
    /// so the camera's up is Unity +Y and camHdg orbits about it. Left-multiplying
    /// FromToRotation(Vector3.up, north) re-expresses the frame about the pole instead, which is
    /// the substitution stock would have made if it had a pole to work with. It composes onto
    /// __result rather than replacing it, so if a KSP update ever gives OBT_ABS a real frame that
    /// frame is kept and simply re-based.
    ///
    /// FoRModes.SRF_NORTH - the Free and Auto cameras - builds
    ///
    ///     LookRotation(AngleAxis(90, cross(Vector3.up, FoRupAxis)) * -FoRupAxis, FoRupAxis)
    ///
    /// where FoRupAxis is the local vertical. That cross product is the local east, and rotating
    /// straight down by 90 degrees about east gives the north tangent - but only if the axis
    /// crossed against the vertical really is the spin axis. On a tilted body it is not, so the
    /// camera's "north" leans off true north and the horizon rolls as the vessel moves, which is
    /// the flipping. The whole expression is rebuilt here rather than composed onto, because the
    /// forward vector itself is what has to change; the up axis is the local vertical either way
    /// and is already pole-independent.
    ///
    /// GetFoR is called only from camera code (FlightCamera and SpaceCenterCamera2), so patching
    /// it here rather than at FlightCamera.GetCameraFoR costs nothing in blast radius and picks
    /// up the one path that bypasses GetCameraFoR: FlightCamera.setTarget seeds lastFoR straight
    /// from FlightGlobals.GetFoR, and if that disagreed with the live frame the camera would lerp
    /// in from a tilted start every time the target changed.
    ///
    /// Note this is the single-argument overload, which is the one every FlightCamera path uses.
    /// SpaceCenterCamera2 calls the two-argument overload, which passes no body at all - it is
    /// not covered here.
    /// </summary>
    [HarmonyPatch(typeof(FlightGlobals))]
    [HarmonyPatch("GetFoR")]
    [HarmonyPatch(new Type[] { typeof(FoRModes) })]
    internal class FlightGlobals_GetFoR
    {
        /// <summary>
        /// getFoR assigns this from the reference position before it reaches the switch, so a
        /// postfix reads exactly the vertical stock used - no need to re-derive which body and
        /// position it was taken against.
        /// </summary>
        private static readonly AccessTools.FieldRef<FlightGlobals, Vector3> FoRupAxis =
            AccessTools.FieldRefAccess<FlightGlobals, Vector3>("FoRupAxis");

        [HarmonyPostfix]
        private static void PostfixGetFoR(FoRModes mode, ref Quaternion __result)
        {
            //Every other mode builds its frame from the local vertical or the orbit, both of
            //which are already pole-independent.
            if (mode != FoRModes.OBT_ABS && mode != FoRModes.SRF_NORTH) return;

            var vessel = FlightGlobals.ActiveVessel;
            if (vessel == null) return;

            var north = TiltEm.WorldNorth(vessel.mainBody);

            //Exactly Vector3.up on an untilted install, where both branches below would reproduce
            //stock's own arithmetic - but returning early keeps the result bit-identical rather
            //than merely indistinguishable, so nothing downstream can accumulate rounding drift.
            if ((north - Vector3.up).sqrMagnitude < 1e-12f) return;

            if (mode == FoRModes.OBT_ABS)
            {
                __result = Quaternion.FromToRotation(Vector3.up, north) * __result;
                return;
            }

            var up = FoRupAxis(FlightGlobals.fetch);
            var east = Vector3.Cross(north, up);

            //Directly over the pole, where north is undefined and the cross collapses. Stock is
            //singular at its own pole in exactly the same way; leaving its answer in place is
            //what stock would have produced and avoids handing LookRotation a zero vector.
            if (east.sqrMagnitude < 1e-12f) return;

            __result = Quaternion.LookRotation(Quaternion.AngleAxis(90f, east) * -up, up);
        }
    }
}
