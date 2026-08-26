using UnityEngine;

namespace TiltEm
{
    /// <summary>
    /// The frame Planetarium.Zup rotates away from while a body holds the rotating frame, plus
    /// the rule for which body may hold it. Latched on entry, dropped on exit.
    ///
    /// See section 5 of Docs/TILT_MATHEMATICS.pdf.
    /// </summary>
    public static class PlanetariumAnchor
    {
        /// <summary>
        /// The planetarium frame captured when the current body took the rotating frame.
        /// </summary>
        //Identity, not default(CelestialFrame): that is a zero matrix, and it would poison every
        //frame built from it before the first latch.
        public static Planetarium.CelestialFrame ZupAnchor { get; private set; } = TiltEmFrames.Identity;

        /// <summary>The anchoring body's spin angle when it took the rotating frame.</summary>
        public static double ZupAnchorRotationAngle { get; private set; }

        /// <summary>The body holding the rotating frame, or null while none does.</summary>
        public static CelestialBody ZupAnchorBody { get; private set; }

        /// <summary>The body's absolute spin phase at <paramref name="ut"/>.</summary>
        //Uses rotationPeriod rather than rotPeriodRecip so it holds before the first CBUpdate.
        public static double RotationAngleAt(CelestialBody body, double ut)
        {
            if (body.rotationPeriod == 0) return body.initialRotation % 360;
            return (body.initialRotation + 360 * (1 / body.rotationPeriod) * ut) % 360;
        }

        /// <summary>Latches the anchor to the body entering its rotating frame. Idempotent.</summary>
        //Kopernicus sets inverseRotation without going through setRotatingFrame, so the prefix
        //does not always fire - both prefix and CBUpdate call this.
        public static void EnsureZupAnchor(CelestialBody body, BodyTilt tilt)
        {
            if (ReferenceEquals(ZupAnchorBody, body)) return;

            //body.rotationAngle, not the angle at the current UT. The anchor has to describe the
            //frame the body is in, and BodyFrame was last written at that angle; in CBUpdate the
            //two differ by a tick, and pairing the new angle with the old frame is what holds the
            //body still while the sky takes up the tick.
            ZupAnchor = TiltEmFrames.AnchorFor(tilt, body.rotationAngle, body.BodyFrame, Planetarium.Zup);
            ZupAnchorRotationAngle = body.rotationAngle;
            ZupAnchorBody = body;
        }

        /// <summary>Drops the anchor when a body leaves its rotating frame.</summary>
        //A stale anchor measures elapsed rotation from the old latch, which grows by the body's
        //whole inertial arc. Re-entry snaps Zup forward by that amount in one tick (section 7.4).
        //CBUpdate calls this (not a setRotatingFrame postfix) because it is the one universal path.
        public static void ReleaseZupAnchor(CelestialBody body)
        {
            if (!ReferenceEquals(ZupAnchorBody, body)) return;

            ZupAnchorBody = null;
        }

        /// <summary>Whether <paramref name="body"/> may hold the rotating frame this tick.</summary>
        //Stock does not enforce one holder: setDominantBody leaves the outgoing body's
        //inverseRotation set, and it sticks on bodies whose threshold reaches past their SOI.
        //Two flagged bodies both write Zup and re-latch from each other's BodyFrame - a feedback
        //loop that drags everything positioned through Zup.
        public static bool MayHoldRotatingFrame(CelestialBody body)
        {
            if (body == null) return false;

            //The rotating frame exists for the active vessel's physics, which is referenced to
            //the dominant body and nothing else, so that body decides whenever there is one.
            CelestialBody dominant = OrbitPhysicsManager.DominantBody;
            if (dominant != null) return ReferenceEquals(body, dominant);

            //No physics manager yet - system construction, or PSystemSetup.SetSpaceCentre. With
            //nothing to arbitrate, the first claimant keeps the frame, which is what preserves
            //the space centre's frame at load.
            return ZupAnchorBody == null || ReferenceEquals(ZupAnchorBody, body);
        }

        /// <summary>Drops the anchor so the next rotating body re-latches.</summary>
        //Planetarium.Awake rebuilds Zup, so an anchor from the old frame is invalid.
        public static void ResetZupAnchor()
        {
            ZupAnchorBody = null;
            ZupAnchorRotationAngle = 0;
            ZupAnchor = TiltEmFrames.OrIdentity(Planetarium.Zup);
        }
    }
}
