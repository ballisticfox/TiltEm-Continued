using System;
using UnityEngine;

namespace TiltEm.Verification
{
    /// <summary>
    /// Category M: serialising and deserialising a scenario - quicksave, quickload, loading a
    /// save from the main menu, and every ordinary scene change.
    ///
    /// What the save file holds decides what can go wrong. A landed vessel persists lat/lon/alt
    /// and srfRelRotation, and ProtoVessel.Load turns those back into world state through
    /// CelestialBody.GetWorldSurfacePosition and bodyTransform.rotation, i.e. through BodyFrame.
    /// A vessel on rails persists orbital elements, and OrbitDriver turns those back into world
    /// state through Planetarium.Zup. Neither stores a world position, so neither can be
    /// restored wrongly on its own: whatever frame the game rebuilds, both are placed in it.
    ///
    /// The exposure is the relationship BETWEEN the two paths. BodyFrame carries transpose(Zup)
    /// and the orbit path carries transpose(Zup) as well, so they agree only while the Zup each
    /// one sees is the same Zup. Rebuilding the world puts three things back in an order the mod
    /// does not control - Planetarium.Awake resets Zup, PSystemSetup.OnSceneChange clears every
    /// inverseRotation flag, and PSystemSetup.SetSpaceCentre sets one of them again by writing
    /// the field directly - and the anchor these checks are about is reset in the middle of it.
    /// </summary>
    public static class PersistenceChecks
    {
        private const double KerbinRadius = 600000.0;
        private const double KerbinRotationPeriod = 21549.425;
        private const double MunRotationPeriod = 138984.38;

        /// <summary>Kerbin's legacy Tilt'Em obliquity, the one the worked examples use.</summary>
        private static BodyTilt KerbinTilt
        {
            get { return TiltEmFrames.FromPole(104.348568, 69.409328); }
        }

        private static BodyTilt MunTilt
        {
            get { return TiltEmFrames.FromPole(12.0, 61.0); }
        }

        /// <summary>The landing site, as lat/lon/alt would give it: a body-fixed vector.</summary>
        private static readonly Vector3d LandingSite =
            new Vector3d(0.41, -0.27, 0.87).normalized * KerbinRadius;

        /// <summary>A vessel on rails, as its orbital elements give it: a celestial-frame vector.</summary>
        private static readonly Vector3d StationCelestial =
            new Vector3d(0.62, 0.51, 0.60).normalized * 1_200_000.0;

        /// <summary>A fixed star, which is what fixes the time of day and the season.</summary>
        private static readonly Vector3d Star = new Vector3d(0.19, -0.88, 0.44).normalized;

        private const double SaveUt = 987_654.321;

        public static void Run()
        {
            AQuickloadKeepsTheStarsWhereTheyWere();
            AQuickloadKeepsTheStationOverTheSameGround();
            AQuickloadFromOrbitKeepsTheGroundTrack();
            ReenteringTheFrameAfterALoadDoesNotMoveTheCraft();
            TheAnchorSurvivesALoadIntoTheSpaceCentre();
            AnUnlatchedAnchorMisplacesEveryOnRailsVessel();
            ReadingTheResetOriginWouldMisplaceThem();
            LoadingAtADifferentTimeAdvancesNothingElse();
            AStaleAnchorAcrossALoadWouldMoveTheSky();
        }

        // -------------------------------------------------------------------------------------
        // the world, and the load sequence
        // -------------------------------------------------------------------------------------

        private sealed class World
        {
            public Sim Sim;
            public SimBody Kerbin;
            public SimBody Mun;
        }

        private static World Fresh()
        {
            var w = new World
            {
                Sim = new Sim(),
                Kerbin = new SimBody("Kerbin", KerbinTilt, KerbinRotationPeriod, 90.0),
                Mun = new SimBody("Mun", MunTilt, MunRotationPeriod, 15.0),
            };

            w.Sim.Register(w.Kerbin, w.Mun);
            return w;
        }

        /// <summary>
        /// A session that has been running long enough for every frame to have been built
        /// against a Zup some rotating body produced, rather than against the identity the
        /// world starts with.
        /// </summary>
        private static World Settled(bool belowThreshold)
        {
            var w = Fresh();

            for (var i = 0; i < 5; i++) w.Sim.Tick(new[] { w.Kerbin, w.Mun }, SaveUt - 0.1 + i * 0.02);

            w.Sim.SetDominantBody(w.Kerbin);

            if (belowThreshold)
            {
                w.Sim.SetRotatingFrame(w.Kerbin, true);
                for (var i = 0; i < 5; i++) w.Sim.Tick(new[] { w.Kerbin, w.Mun }, SaveUt + i * 0.02);
            }

            w.Sim.Tick(new[] { w.Kerbin, w.Mun }, SaveUt);
            return w;
        }

        /// <summary>
        /// Everything between the quicksave and the vessel being live again, in the order KSP
        /// does it. Universal time is persisted exactly, so the reloaded world is asked for the
        /// same instant; nothing here advances it.
        /// </summary>
        private static World Reload(bool belowThreshold, double ut)
        {
            var w = Fresh();

            //TiltEm.SceneRequested, on onGameSceneSwitchRequested.
            w.Sim.ResetZupAnchor();

            //PSystemSetup.OnSceneChange clears the flag on every body, unconditionally.
            w.Sim.ClearInverseRotation(new[] { w.Kerbin, w.Mun });

            //Planetarium.Awake rebuilds Zup from nothing.
            w.Sim.Zup = TiltEmFrames.Identity;

            //The first CBUpdate of the new scene, with every body inertial.
            w.Sim.Tick(new[] { w.Kerbin, w.Mun }, ut);

            if (belowThreshold)
            {
                //OrbitPhysicsManager.checkReferenceFrame, once the vessel is placed and its
                //altitude is known.
                w.Sim.SetDominantBody(w.Kerbin);
                w.Sim.SetRotatingFrame(w.Kerbin, true);
                w.Sim.Tick(new[] { w.Kerbin, w.Mun }, ut);
            }

            return w;
        }

        // -------------------------------------------------------------------------------------
        // observables a player would notice
        // -------------------------------------------------------------------------------------

        /// <summary>
        /// Where a fixed direction sits in body-fixed coordinates: the sun's place in the sky
        /// above the landing site, hence the time of day and the season.
        /// </summary>
        private static Vector3d StarOverhead(Sim sim, SimBody body)
        {
            return sim.SkyInBodyFrame(body, Star);
        }

        /// <summary>
        /// Where an on-rails vessel sits in body-fixed coordinates. This is the one observable
        /// that crosses both restore paths: the station comes back through Zup and the ground
        /// it is measured against comes back through BodyFrame.
        /// </summary>
        private static Vector3d StationOverhead(Sim sim, SimBody body)
        {
            return body.BodyFrame.WorldToLocal(sim.OrbitToWorld(StationCelestial));
        }

        // -------------------------------------------------------------------------------------
        // checks
        // -------------------------------------------------------------------------------------

        /// <summary>
        /// The save stores no world position for anything, so the whole world may legitimately
        /// come back rotated. What may not change is where the sky sits relative to the ground.
        /// </summary>
        private static void AQuickloadKeepsTheStarsWhereTheyWere()
        {
            var before = Settled(belowThreshold: true);
            var after = Reload(belowThreshold: true, ut: SaveUt);

            var error = Harness.MaxComponentError(
                StarOverhead(before.Sim, before.Kerbin),
                StarOverhead(after.Sim, after.Kerbin));

            Harness.CheckWithin("M1", "quickload leaves the sun where it was in the sky",
                error, 1e-6, "of a unit vector");

            //And it is the value the tilt and spin angle alone predict, not merely a value that
            //happens to survive the round trip.
            var predicted = Harness.MaxComponentError(
                StarOverhead(after.Sim, after.Kerbin),
                Sim.ExpectedSkyInBodyFrame(after.Kerbin, Star));

            Harness.CheckWithin("M1", "and it is the pole-and-spin value, not a coincidence",
                predicted, 1e-9, "of a unit vector");
        }

        /// <summary>
        /// The station is restored from orbital elements through Zup; the ground under it is
        /// restored from lat/lon through BodyFrame. If the two paths disagree the station moves
        /// across the sky, and every rendezvous set up before the save is off.
        /// </summary>
        private static void AQuickloadKeepsTheStationOverTheSameGround()
        {
            var before = Settled(belowThreshold: true);
            var after = Reload(belowThreshold: true, ut: SaveUt);

            var moved = (StationOverhead(before.Sim, before.Kerbin)
                         - StationOverhead(after.Sim, after.Kerbin)).magnitude;

            Harness.CheckWithin("M1", "an on-rails vessel comes back over the same ground",
                moved, 1e-4, "m");

            var range = Math.Abs(
                (StationOverhead(before.Sim, before.Kerbin) - LandingSite).magnitude
                - (StationOverhead(after.Sim, after.Kerbin) - LandingSite).magnitude);

            Harness.CheckWithin("M1", "and the range from the landing site is unchanged",
                range, 1e-6, "m");
        }

        /// <summary>
        /// The same round trip from above the threshold, where no body is rotating at all and
        /// the anchor is never latched on either side.
        /// </summary>
        private static void AQuickloadFromOrbitKeepsTheGroundTrack()
        {
            var before = Settled(belowThreshold: false);
            var after = Reload(belowThreshold: false, ut: SaveUt);

            var moved = (StationOverhead(before.Sim, before.Kerbin)
                         - StationOverhead(after.Sim, after.Kerbin)).magnitude;

            Harness.CheckWithin("M1", "a save taken above the threshold restores the ground track",
                moved, 1e-4, "m");

            var star = Harness.MaxComponentError(
                StarOverhead(before.Sim, before.Kerbin),
                StarOverhead(after.Sim, after.Kerbin));

            Harness.CheckWithin("M1", "and the sky with it", star, 1e-6, "of a unit vector");
        }

        /// <summary>
        /// A load lands the vessel in an inertial world and the rotating frame is taken a moment
        /// later, once the altitude is known. That is an ordinary threshold crossing, but with
        /// an anchor that was reset rather than released, so it is worth confirming separately
        /// that the crossing still costs nothing.
        /// </summary>
        private static void ReenteringTheFrameAfterALoadDoesNotMoveTheCraft()
        {
            var w = Fresh();

            w.Sim.ResetZupAnchor();
            w.Sim.ClearInverseRotation(new[] { w.Kerbin, w.Mun });
            w.Sim.Zup = TiltEmFrames.Identity;
            w.Sim.Tick(new[] { w.Kerbin, w.Mun }, SaveUt);

            var groundBefore = Sim.SurfaceToWorld(w.Kerbin, LandingSite);
            var skyBefore = w.Sim.Zup;

            w.Sim.SetDominantBody(w.Kerbin);
            w.Sim.SetRotatingFrame(w.Kerbin, true);
            w.Sim.Tick(new[] { w.Kerbin, w.Mun }, SaveUt);

            var groundMoved = (groundBefore - Sim.SurfaceToWorld(w.Kerbin, LandingSite)).magnitude;
            var skyTurned = Harness.FrameRotationAngle(skyBefore, w.Sim.Zup);

            Harness.CheckWithin("M2", "taking the frame after a load does not move the ground",
                groundMoved, 1e-6, "m");
            Harness.CheckWithin("M2", "and does not turn the sky", skyTurned, 1e-9, "deg");
        }

        /// <summary>
        /// Loading a save from the main menu goes to the space centre, and PSystemSetup writes
        /// the home body's inverseRotation flag directly rather than calling setRotatingFrame.
        /// The mod's prefix therefore never fires, and the anchor has to be established by the
        /// defensive call in CBUpdate instead.
        /// </summary>
        private static void TheAnchorSurvivesALoadIntoTheSpaceCentre()
        {
            var w = Fresh();

            w.Sim.ResetZupAnchor();
            w.Sim.ClearInverseRotation(new[] { w.Kerbin, w.Mun });
            w.Sim.Zup = TiltEmFrames.Identity;
            w.Sim.Tick(new[] { w.Kerbin, w.Mun }, SaveUt);

            var ksc = Sim.SurfaceToWorld(w.Kerbin, LandingSite);

            //PSystemSetup.SetSpaceCentre: the flag, written directly, with no anchor latched.
            w.Sim.SetSpaceCentre(w.Kerbin);

            Harness.Check("M3", "the space centre path really does leave the anchor unlatched",
                w.Sim.AnchorBody == null, null);

            //FloatingOrigin.SetOffset measures the space centre transform at exactly this point,
            //so the first tick afterwards must not move it.
            w.Sim.Tick(new[] { w.Kerbin, w.Mun }, SaveUt);

            var moved = (ksc - Sim.SurfaceToWorld(w.Kerbin, LandingSite)).magnitude;

            Harness.CheckWithin("M3", "the space centre does not move under the floating origin",
                moved, 1e-6, "m");
            Harness.Check("M3", "and the first tick latches the anchor",
                w.Sim.HoldsAnchor("Kerbin"), null);
        }

        /// <summary>
        /// The window between those two lines is not empty.
        /// Orbit.GetOrbitalStateVectorsAtTrueAnomaly reaches Planetarium.ZupAtT, which the mod
        /// patches, and which reads the anchor. With the flag set and no anchor latched there is
        /// no origin to measure elapsed rotation from, and the zero ResetZupAnchor leaves behind
        /// is not one: it makes the elapsed rotation the body's entire rotationAngle.
        /// </summary>
        private static void AnUnlatchedAnchorMisplacesEveryOnRailsVessel()
        {
            var w = Unlatched();

            //What the live Zup says, and what the at-time evaluation says, in the same instant.
            var disagreement = Harness.FrameRotationAngle(w.Sim.Zup, w.Sim.ZupAtT(w.Kerbin, SaveUt));

            Harness.CheckWithin("M4", "ZupAtT agrees with the live Zup before the first latch",
                disagreement, 1e-6, "deg");
            Harness.CheckWithin("M4", "so an on-rails vessel is placed in one spot, not two",
                Displacement(w), 1e-3, "m");

            //And it still tracks time, rather than being pinned to the current instant.
            var later = w.Sim.ZupAtT(w.Kerbin, SaveUt + KerbinRotationPeriod / 4.0);
            var turned = Harness.FrameRotationAngle(w.Sim.Zup, later);

            Harness.CheckWithin("M4", "a quarter day later it has turned a quarter turn",
                Math.Abs(turned - 90.0), 1e-6, "deg");
        }

        /// <summary>
        /// The witness. Reading ZupAnchorRotationAngle with nothing latched is what the patch
        /// used to do, and it puts every on-rails vessel hundreds of kilometres from where the
        /// live frame puts it.
        /// </summary>
        private static void ReadingTheResetOriginWouldMisplaceThem()
        {
            var w = Unlatched();
            w.Sim.UnlatchedZupAtTUsesTheResetOrigin = true;

            var disagreement = Harness.FrameRotationAngle(w.Sim.Zup, w.Sim.ZupAtT(w.Kerbin, SaveUt));

            Harness.Check("M4", "measuring elapsed rotation from the reset origin really does disagree",
                disagreement > 1.0, Harness.Fmt(disagreement) + " deg");
            Harness.Check("M4", "and really does misplace an on-rails vessel",
                Displacement(w) > 100_000.0, Harness.Fmt(Displacement(w) / 1000.0) + " km");
        }

        /// <summary>
        /// A body flagged into the rotating frame with no anchor latched: what
        /// PSystemSetup.SetSpaceCentre produces directly, and what a scene change out of flight
        /// produces for the rest of the frame, ResetZupAnchor having run before
        /// PSystemSetup.OnSceneChange clears the flag.
        /// </summary>
        private static World Unlatched()
        {
            var w = Fresh();

            w.Sim.ResetZupAnchor();
            w.Sim.ClearInverseRotation(new[] { w.Kerbin, w.Mun });
            w.Sim.Zup = TiltEmFrames.Identity;
            w.Sim.Tick(new[] { w.Kerbin, w.Mun }, SaveUt);

            w.Sim.SetSpaceCentre(w.Kerbin);
            return w;
        }

        /// <summary>How far apart the two frames place the same on-rails vessel.</summary>
        private static double Displacement(World w)
        {
            var live = w.Sim.Zup.WorldToLocal(StationCelestial);
            var atT = w.Sim.ZupAtT(w.Kerbin, SaveUt).WorldToLocal(StationCelestial);

            return (live - atT).magnitude;
        }

        /// <summary>
        /// Universal time is persisted exactly, so a reload is asked for the same instant and
        /// nothing should have advanced. Loading an older save is the same operation at a
        /// different time: everything is recomputed from that time, and the relationship between
        /// sky and ground has to be the one that time implies.
        /// </summary>
        private static void LoadingAtADifferentTimeAdvancesNothingElse()
        {
            var earlier = SaveUt - 4_000.0;
            var after = Reload(belowThreshold: true, ut: earlier);

            var error = Harness.MaxComponentError(
                StarOverhead(after.Sim, after.Kerbin),
                Sim.ExpectedSkyInBodyFrame(after.Kerbin, Star));

            Harness.CheckWithin("M1", "loading an older save puts the sky at that save's time",
                error, 1e-9, "of a unit vector");

            //A different time really is a different sky, so the check above is not vacuous.
            var now = Reload(belowThreshold: true, ut: SaveUt);
            var apart = (StarOverhead(after.Sim, after.Kerbin)
                         - StarOverhead(now.Sim, now.Kerbin)).magnitude;

            Harness.Check("M1", "and the two times really do differ", apart > 0.1,
                Harness.Fmt(apart) + " of a unit vector apart");
        }

        /// <summary>
        /// The witness for the reset itself.
        ///
        /// Carrying an anchor across a load is invisible in every body-relative observable, and
        /// deliberately so: a Zup that is wrong by a fixed rotation cancels out of
        /// transpose(BodyFrame) * transpose(Zup), which is Theorem 5.1 doing its job. What does
        /// not cancel is a Zup that CHANGES while the scene is live. The stale anchor makes the
        /// re-entry tick move Zup instead of leaving it alone, and the ground moves under an
        /// active vessel whose rigidbody was placed against the previous value.
        /// </summary>
        private static void AStaleAnchorAcrossALoadWouldMoveTheSky()
        {
            var before = Settled(belowThreshold: true);
            var later = SaveUt + 4_000.0;

            var w = Fresh();

            //The same load, except that the scene-change handler never ran.
            w.Sim.ZupAnchor = before.Sim.ZupAnchor;
            w.Sim.ZupAnchorRotationAngle = before.Sim.ZupAnchorRotationAngle;
            w.Sim.KeepStaleAnchor = true;
            w.Sim.SuppressReanchor = true;

            w.Sim.ClearInverseRotation(new[] { w.Kerbin, w.Mun });
            w.Sim.Zup = TiltEmFrames.Identity;
            w.Sim.Tick(new[] { w.Kerbin, w.Mun }, later);

            var ground = Sim.SurfaceToWorld(w.Kerbin, LandingSite);

            w.Sim.SetDominantBody(w.Kerbin);
            w.Sim.SetRotatingFrame(w.Kerbin, true);
            w.Sim.Tick(new[] { w.Kerbin, w.Mun }, later);

            var moved = (ground - Sim.SurfaceToWorld(w.Kerbin, LandingSite)).magnitude;

            Harness.Check("M5", "a stale anchor really does move the ground at re-entry",
                moved > 100_000.0, Harness.Fmt(moved / 1000.0) + " km");

            //The same sequence with the reset in place, which is what ships.
            var fixedUp = Fresh();
            fixedUp.Sim.ZupAnchor = before.Sim.ZupAnchor;
            fixedUp.Sim.ZupAnchorRotationAngle = before.Sim.ZupAnchorRotationAngle;
            fixedUp.Sim.ResetZupAnchor();

            fixedUp.Sim.ClearInverseRotation(new[] { fixedUp.Kerbin, fixedUp.Mun });
            fixedUp.Sim.Zup = TiltEmFrames.Identity;
            fixedUp.Sim.Tick(new[] { fixedUp.Kerbin, fixedUp.Mun }, later);

            var settled = Sim.SurfaceToWorld(fixedUp.Kerbin, LandingSite);

            fixedUp.Sim.SetDominantBody(fixedUp.Kerbin);
            fixedUp.Sim.SetRotatingFrame(fixedUp.Kerbin, true);
            fixedUp.Sim.Tick(new[] { fixedUp.Kerbin, fixedUp.Mun }, later);

            var held = (settled - Sim.SurfaceToWorld(fixedUp.Kerbin, LandingSite)).magnitude;

            Harness.CheckWithin("M5", "and resetting it holds the ground still",
                held, 1e-6, "m");
        }
    }
}
