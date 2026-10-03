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
            //Most calls hand straight back to stock, so they skip the marker, and a plain reference
            //test replaces Unity's destroyed-object check; stock reads these fields regardless.
            if (ReferenceEquals(body, null) || !body.inverseRotation) return true;

            using (TiltEmProfiler.ZupAtT.Sample())
            {
                //Before the first latch there is no anchor to speak for, so the caller's own body is
                //the best guess - and it is what the anchor is about to be latched to.
                CelestialBody latched = PlanetariumAnchor.ZupAnchorBody;
                CelestialBody anchorBody = latched ?? body;
                double rotationAngle = PlanetariumAnchor.RotationAngleAt(anchorBody, UT);

                if (latched == null)
                {
                    tempZup = UnlatchedZup(body, rotationAngle);
                    return false;
                }

                //The closest-approach solver lands here twice per iteration, so this reads the
                //anchor's cached basis rather than rebuilding it.
                tempZup = PlanetariumAnchor.ZupAt(rotationAngle - PlanetariumAnchor.ZupAnchorRotationAngle);
                return false;
            }
        }

        /// <summary>Zup with no anchor latched yet, from the anchor CBUpdate is about to latch.</summary>
        //The stored frame and angle are stale here and would displace on-rails vessels by hundreds
        //of km (PersistenceChecks). See section 5 of TILT_MATHEMATICS.pdf.
        private static Planetarium.CelestialFrame UnlatchedZup(CelestialBody body, double rotationAngle)
        {
            //Untilted rather than stock: stock's Rz spin assumes +Z, which is wrong once CBUpdate
            //has tilted the anchor.
            if (!TiltEm.TryGetTilt(body.bodyName, out BodyTilt tilt))
            {
                tilt = TiltEmFrames.Untilted;
            }

            Planetarium.CelestialFrame anchor = TiltEmFrames.AnchorFor(tilt, body.rotationAngle, body.BodyFrame, Planetarium.Zup);
            double anchorRotationAngle = body.rotationAngle;

            return TiltEmFrames.Zup(anchor, tilt, rotationAngle - anchorRotationAngle);
        }
    }
}
