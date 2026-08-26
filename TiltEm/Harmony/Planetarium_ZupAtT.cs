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
    ///
    /// Which is why the frame is built from the ANCHOR's body rather than from the body passed
    /// in. There is only one Planetarium.Zup, and what it is doing is decided entirely by
    /// whichever body currently holds the rotating frame - so evaluating it at some other time
    /// means advancing THAT body's rotation, not this one's. The two are normally the same body
    /// and the distinction never arises; when they are not, using the caller's rotation against
    /// another body's anchor produces a frame with no relation to the live Zup, and every vessel
    /// positioned through it lands somewhere arbitrary. Measured at 839 km for a Kerbin anchor
    /// used with the Mun's rotation - see TeleportChecks.
    /// </summary>
    [HarmonyPatch(typeof(Planetarium))]
    [HarmonyPatch("ZupAtT")]
    internal class Planetarium_ZupAtT
    {
        [HarmonyPrefix]
        private static bool PrefixZupAtT(double UT, CelestialBody body, ref Planetarium.CelestialFrame tempZup)
        {

            if (body == null || !body.inverseRotation) return true;

            //Before the first latch there is no anchor to speak for, so the caller's own body is
            //the best available guess - and it is what the anchor is about to be latched to.
            var latched = TiltEm.ZupAnchorBody;
            var anchorBody = latched ?? body;

            //An untilted anchor body still falls through to stock, whose Rz(rot - directRotAngle)
            //is provably the same frame in that case. Leaving it there keeps the untilted path
            //bit-identical to stock rather than merely equal to it.
            if (!TiltEm.TryGetTilt(anchorBody.bodyName, out var tilt)) return true;

            var anchor = TiltEm.ZupAnchor;
            var anchorRotationAngle = TiltEm.ZupAnchorRotationAngle;

            if (latched == null)
            {
                //Guessing the body is not enough on its own. An anchor is a frame paired with
                //the rotation angle it was captured at, and with none latched those two come
                //from different places: ZupAnchor is whatever ResetZupAnchor left behind on the
                //last scene change, while ZupAnchorRotationAngle is a plain zero. Measuring
                //elapsed rotation from that zero yields the body's entire rotationAngle, so the
                //frame returned here is the live Zup turned by up to a full revolution about
                //the pole - and every on-rails vessel is placed through it. Kerbin at 1200 km
                //moves several hundred kilometres; see PersistenceChecks.
                //
                //The window is real. ResetZupAnchor runs on onGameSceneSwitchRequested, before
                //the scene is torn down and therefore before PSystemSetup.OnSceneChange clears
                //inverseRotation, so any body below its threshold stays flagged with no anchor
                //for the rest of that frame. PSystemSetup.SetSpaceCentre reopens the same window
                //from the other side, setting the flag directly without routing through
                //setRotatingFrame.
                //
                //Derive the pair the way CBUpdate is about to instead. At UT = now this returns
                //the live Zup exactly, and at any other UT it advances by the rotation between
                //the two, which is the same contract a latched anchor honours.
                anchor = TiltEmFrames.AnchorFor(tilt, body.rotationAngle, body.BodyFrame, Planetarium.Zup);
                anchorRotationAngle = body.rotationAngle;
            }

            var rotationAngle = TiltEm.RotationAngleAt(anchorBody, UT);

            tempZup = TiltEmFrames.Zup(anchor, tilt, rotationAngle - anchorRotationAngle);
            return false;
        }
    }
}
