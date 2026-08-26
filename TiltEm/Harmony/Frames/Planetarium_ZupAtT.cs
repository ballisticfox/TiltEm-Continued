using HarmonyLib;

// ReSharper disable All

namespace TiltEm.Harmony
{
    /// <summary>
    /// Applies the tilt when the planetarium frame is asked for at an arbitrary time.
    /// Must agree with the static Zup that CBUpdate writes; see section 5 of
    /// Docs/TILT_MATHEMATICS.pdf.
    /// </summary>
    //GetOrbitalStateVectorsAtTrueAnomaly calls this, so UpdateFromUT and updateFromParameters
    //both reach it. Without the patch, orbits draw correctly but bodies sit in the wrong place.
    //Uses the ANCHOR's body, not the body passed in: there is one Zup and the rotating-frame
    //holder decides it, so evaluating at another time advances that body's rotation. See
    //TeleportChecks for the 839 km displacement when they differ.
    [HarmonyPatch(typeof(Planetarium))]
    [HarmonyPatch("ZupAtT")]
    internal class Planetarium_ZupAtT
    {
        [HarmonyPrefix]
        private static bool PrefixZupAtT(double UT, CelestialBody body, ref Planetarium.CelestialFrame tempZup)
        {
            if (body == null || !body.inverseRotation) return true;

            //Before the first latch there is no anchor to speak for, so the caller's own body is
            //the best guess - and it is what the anchor is about to be latched to.
            CelestialBody latched = PlanetariumAnchor.ZupAnchorBody;
            CelestialBody anchorBody = latched ?? body;

            //Untilted anchor body falls through to stock, keeping the path bit-identical rather
            //than merely equal.
            if (!TiltEm.TryGetTilt(anchorBody.bodyName, out BodyTilt tilt)) return true;

            Planetarium.CelestialFrame anchor = PlanetariumAnchor.ZupAnchor;
            double anchorRotationAngle = PlanetariumAnchor.ZupAnchorRotationAngle;

            if (latched == null)
            {
                //Without a latched anchor the stored frame and angle are stale and would
                //displace on-rails vessels by hundreds of km (PersistenceChecks). Build a
                //fresh anchor the way CBUpdate is about to; see section 5 of TILT_MATHEMATICS.pdf.
                anchor = TiltEmFrames.AnchorFor(tilt, body.rotationAngle, body.BodyFrame, Planetarium.Zup);
                anchorRotationAngle = body.rotationAngle;
            }

            double rotationAngle = PlanetariumAnchor.RotationAngleAt(anchorBody, UT);

            tempZup = TiltEmFrames.Zup(anchor, tilt, rotationAngle - anchorRotationAngle);
            return false;
        }
    }
}
