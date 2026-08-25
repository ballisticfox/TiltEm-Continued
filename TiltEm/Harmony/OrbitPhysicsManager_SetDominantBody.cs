using HarmonyLib;
using UnityEngine;

// ReSharper disable All

namespace TiltEm.Harmony
{
    /// <summary>
    /// Ends the outgoing body's rotating frame when the dominant body changes.
    ///
    /// Stock never does this. setDominantBody assigns dominantBody, rebuilds every loaded
    /// vessel's part velocities from vessel.orbit.GetVel(), and leaves the body it just left
    /// still flagged inverseRotation; checkReferenceFrame afterwards only ever tests the body
    /// that is dominant now. For most bodies nothing shows, because the threshold is crossed on
    /// the way out and the flag is cleared before the sphere of influence changes. It shows on a
    /// body whose inverseRotThresholdAltitude reaches past its own SOI, Mimas being the usual
    /// example, where no altitude inside the SOI is above the threshold and so there is no point
    /// at which stock can clear it. Leaving carries the flag away still set, for good.
    ///
    /// Three things then go wrong, in rising order of severity.
    ///
    /// FlightGlobals.getCentrifugalAcc and getCoriolisAcc are both gated on the flag, so the
    /// stale body reports frame accelerations it has no business reporting to anything that
    /// asks about it.
    ///
    /// Coming back later, checkReferenceFrame sees the flag already true and skips
    /// setRotatingFrame entirely, and with it the rb.velocity handover that setRotatingFrame
    /// exists to perform - the craft keeps its inertial velocity inside a rotating frame.
    ///
    /// And for this mod, a second flagged body means two bodies driving one Planetarium.Zup and
    /// one anchor, which is a feedback loop rather than merely a wrong answer. TiltEm.
    /// MayHoldRotatingFrame refuses that in CBUpdate; clearing the flag here removes the cause
    /// instead of coping with it.
    ///
    /// Clearing is safe at exactly this point, and only here, because setDominantBody has just
    /// rewritten every loaded part's velocity from its orbit: there is no rotating-frame
    /// velocity term left owing, so skipping setRotatingFrame's handback costs nothing.
    /// Kopernicus clears the same flag in its own prefix on this method, which makes this a
    /// no-op when Kopernicus is installed and the same repair when it is not.
    /// </summary>
    [HarmonyPatch(typeof(OrbitPhysicsManager))]
    [HarmonyPatch("setDominantBody")]
    internal class OrbitPhysicsManager_SetDominantBody
    {
        /// <summary>
        /// The outgoing body has to be captured before the original runs: setDominantBody
        /// assigns dominantBody on its first line, and the onDominantBodyChange event it fires
        /// at the end reports the *new* body as the "from" value, so the event cannot be used
        /// for this either.
        /// </summary>
        [HarmonyPrefix]
        private static void PrefixSetDominantBody(out CelestialBody __state)
        {
            __state = OrbitPhysicsManager.DominantBody;
        }

        [HarmonyPostfix]
        private static void PostfixSetDominantBody(CelestialBody body, CelestialBody __state)
        {
            var outgoing = __state;
            if (outgoing == null || ReferenceEquals(outgoing, body)) return;

            if (outgoing.inverseRotation)
            {
                outgoing.inverseRotation = false;

                Debug.Log("[TiltEm]: " + outgoing.bodyName + " was still holding the rotating frame at handover to " +
                          (body != null ? body.bodyName : "nothing") + "; cleared it");
            }

            //Unconditional, and after the flag check rather than inside it: Kopernicus may have
            //already cleared the flag in its own prefix, and the anchor still has to follow.
            TiltEm.ReleaseZupAnchor(outgoing);
        }
    }
}
