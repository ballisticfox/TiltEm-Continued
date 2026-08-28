using HarmonyLib;
using UnityEngine;

// ReSharper disable All

namespace TiltEm.Harmony
{
    /// <summary>Ends the outgoing body's rotating frame when the dominant body changes.</summary>
    //Stock never clears inverseRotation on the outgoing body. On most bodies the threshold
    //clears it before the SOI does, but a body whose inverseRotThresholdAltitude exceeds its
    //SOI carries the flag away for good. Three things then break: stale frame accelerations,
    //skipped velocity handover on re-entry, and two flagged bodies fighting over one Zup
    //(see MayHoldRotatingFrame). Safe to clear here because setDominantBody has already
    //rewritten every loaded part's velocity from its orbit. Kopernicus clears the same flag
    //in its own prefix, making this a no-op there.
    [HarmonyPatch(typeof(OrbitPhysicsManager))]
    [HarmonyPatch("setDominantBody")]
    internal class OrbitPhysicsManager_SetDominantBody
    {
        /// <summary>Captures the outgoing body before the original runs.</summary>
        //setDominantBody assigns dominantBody on its first line, and onDominantBodyChange
        //reports the new body as "from", so neither is usable afterwards.
        [HarmonyPrefix]
        private static void PrefixSetDominantBody(out CelestialBody __state)
        {
            __state = OrbitPhysicsManager.DominantBody;
        }

        [HarmonyPostfix]
        private static void PostfixSetDominantBody(CelestialBody body, CelestialBody __state)
        {
            using (TiltEmProfiler.SetDominantBody.Sample())
            {
                CelestialBody outgoing = __state;
                if (outgoing == null || ReferenceEquals(outgoing, body)) return;

                if (outgoing.inverseRotation)
                {
                    outgoing.inverseRotation = false;

                    Debug.Log("[TiltEm]: " + outgoing.bodyName + " still held the rotating frame at handover to "
                              + (body != null ? body.bodyName : "nothing") + "; cleared it");
                }

                //Unconditional, and outside the flag check: Kopernicus may have cleared the flag in
                //its own prefix already, and the anchor still has to follow.
                PlanetariumAnchor.ReleaseZupAnchor(outgoing);
            }
        }
    }
}
