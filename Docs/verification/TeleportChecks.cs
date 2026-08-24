using System;
using UnityEngine;

namespace TiltEm.Verification
{
    /// <summary>
    /// The debug menu's teleports (K1).
    ///
    /// Set Position and Set Orbit both run FlightGlobals.PrepForOrbitSet, and it takes a route
    /// through the reference-frame machinery that nothing else does:
    ///
    ///     clearInverseRotation()                  every body's flag written directly, no CBUpdate
    ///     setDominantBody(target)
    ///     ...the new position is computed from the target's BodyFrame...
    ///     PostOrbitSet -> FloatingOrigin.SetOffset(vessel position)
    ///                  -> CheckReferenceFrame() -> setRotatingFrame(true) if below the threshold
    ///
    /// All of it inside one frame, with no tick anywhere in the middle. Two things about that are
    /// worth checking rather than reasoning about.
    ///
    /// First, clearInverseRotation writes the flag on every body and never calls CBUpdate, so the
    /// mod's ReleaseZupAnchor - which lives in CBUpdate's inertial branch - never runs. A teleport
    /// can therefore leave and re-enter the rotating frame with an anchor that is minutes old
    /// still latched, which is the shape of H1.
    ///
    /// Second, the vessel's position is computed, and the floating origin fixed to it, BEFORE
    /// CheckReferenceFrame decides to re-enter the rotating frame. If entering that frame moved
    /// the body at all, the terrain would swing out from under an origin that has already been
    /// pinned - which is F3, reached by a different route.
    ///
    /// So the observable is the one F3 uses: how far a fixed point on the target's surface moves
    /// in world space across the tick after the teleport. While a body holds the rotating frame
    /// it is frozen by construction, so that distance should be zero; if the anchor is wrong, the
    /// whole body swings and takes the terrain with it. Measured against the same tick in an
    /// untouched run rather than against a bare zero, so the check states a difference the
    /// teleport caused rather than a property of the scenario.
    /// </summary>
    public static class TeleportChecks
    {
        private const double Tick = 0.02;

        //Kerbin's radius and day, so the numbers below are in metres a player would recognise.
        private const double Radius = 600000.0;
        private const double KerbinDay = 21549.425;
        private const double MunDay = 138984.38;

        public static void Run()
        {
            ATeleportToTheSameSurfaceLeavesTheGroundWhereItWas();
            ATeleportFromOrbitToTheSurfaceLeavesTheGroundWhereItWas();
            ATeleportBetweenBodiesLeavesTheGroundWhereItWas();
            ATeleportToOrbitDoesNotMoveTheSky();
            TheTeleportKeepsEveryBodyPointingAtItsOwnSky();
            AnAnchorFromTheWrongBodyReallyDoesMoveTheGround();
        }

        private static SimBody Kerbin()
        {
            return new SimBody("Kerbin", TiltEmFrames.FromLegacyEuler(new Vector3d(20, 0, 5)),
                KerbinDay, 137.0);
        }

        private static SimBody Mun()
        {
            return new SimBody("Mun", TiltEmFrames.FromLegacyEuler(new Vector3d(15.45, 0, 10.61)),
                MunDay, 41.0);
        }

        /// <summary>A landing site, in body-fixed coordinates.</summary>
        private static Vector3d LandingSite()
        {
            //Roughly 20 N, 70 E, so it is off both the equator and the prime meridian and an
            //error about any axis shows up rather than cancelling.
            const double lat = 20.0 * Math.PI / 180.0;
            const double lon = 70.0 * Math.PI / 180.0;

            return new Vector3d(Math.Cos(lat) * Math.Cos(lon), Math.Cos(lat) * Math.Sin(lon),
                Math.Sin(lat)) * Radius;
        }

        /// <summary>A fixed star, for the sky-orientation invariant.</summary>
        private static Vector3d Star()
        {
            return new Vector3d(0.36, -0.48, 0.8);
        }

        private static double Settle(Sim sim, SimBody[] bodies, double ut, int ticks)
        {
            for (var i = 0; i < ticks; i++)
            {
                sim.Tick(bodies, ut);
                ut += Tick;
            }

            return ut;
        }

        /// <summary>
        /// One scenario, rebuilt from scratch each time it is asked for, so the teleported and
        /// untouched runs start from bit-identical state rather than sharing a world one of them
        /// has already mutated.
        /// </summary>
        private class Scenario
        {
            public Sim Sim;
            public SimBody[] Bodies;
            public SimBody Target;
            public double Ut;
        }

        /// <summary>Landed on Kerbin, teleporting elsewhere on Kerbin. The anchor stays latched.</summary>
        private static Scenario SameBody()
        {
            var kerbin = Kerbin();
            var scenario = new Scenario { Sim = new Sim(), Bodies = new[] { kerbin }, Target = kerbin };

            kerbin.InverseRotation = true;
            scenario.Ut = Settle(scenario.Sim, scenario.Bodies, 0.0, 500);

            return scenario;
        }

        /// <summary>
        /// In a high orbit, so the frame is inertial and the anchor was released long ago. The
        /// frame is held first, so the teleport is a genuine re-entry rather than a first entry.
        /// </summary>
        private static Scenario FromOrbit()
        {
            var kerbin = Kerbin();
            var scenario = new Scenario { Sim = new Sim(), Bodies = new[] { kerbin }, Target = kerbin };

            kerbin.InverseRotation = true;
            var ut = Settle(scenario.Sim, scenario.Bodies, 0.0, 200);

            kerbin.InverseRotation = false;
            scenario.Ut = Settle(scenario.Sim, scenario.Bodies, ut, 600);

            return scenario;
        }

        /// <summary>
        /// Landed on Kerbin, teleporting to the Mun, so the rotating frame is handed between two
        /// bodies with different poles and very different spin rates.
        /// </summary>
        private static Scenario CrossBody()
        {
            var kerbin = Kerbin();
            var mun = Mun();
            var scenario = new Scenario { Sim = new Sim(), Bodies = new[] { kerbin, mun }, Target = mun };

            kerbin.InverseRotation = true;
            scenario.Ut = Settle(scenario.Sim, scenario.Bodies, 0.0, 500);

            return scenario;
        }

        /// <summary>
        /// The teleport, as FlightGlobals performs it, up to and including the re-entry into the
        /// rotating frame that CheckReferenceFrame triggers for a surface destination.
        ///
        /// <paramref name="landing"/> false teleports to orbit, where the craft ends up above the
        /// threshold and the frame is left inertial.
        /// </summary>
        private static void Teleport(Sim sim, SimBody[] bodies, SimBody target, bool landing)
        {
            //PrepForOrbitSet: every body's flag cleared directly, with no CBUpdate to notice it
            //and therefore no ReleaseZupAnchor.
            sim.ClearInverseRotation(bodies);

            //PostOrbitSet -> CheckReferenceFrame, still inside the same frame, no tick either side.
            if (landing) sim.SetRotatingFrame(target, true);
        }

        /// <summary>
        /// How far a fixed point on the body's surface moves in world space over one tick. This
        /// is the ground the craft was just placed on and the origin was just pinned to, so any
        /// movement here is the craft coming loose from the terrain.
        /// </summary>
        private static double GroundStep(Sim sim, SimBody[] bodies, SimBody target, double ut)
        {
            var site = LandingSite();

            var before = Sim.SurfaceToWorld(target, site);
            sim.Tick(bodies, ut);
            var after = Sim.SurfaceToWorld(target, site);

            return (after - before).magnitude;
        }

        /// <summary>
        /// Runs a scenario twice - once with the teleport, once untouched - and compares how far
        /// the ground moves on the next tick. The difference is what the teleport itself caused.
        /// </summary>
        private static void CheckAgainstAnUntouchedTick(string name, Func<Scenario> make, bool landing)
        {
            var a = make();
            Teleport(a.Sim, a.Bodies, a.Target, landing);
            var teleported = GroundStep(a.Sim, a.Bodies, a.Target, a.Ut);

            var b = make();
            var untouched = GroundStep(b.Sim, b.Bodies, b.Target, b.Ut);

            Harness.Check("K1", name, teleported <= untouched + 1e-6,
                "ground moves " + Harness.Fmt(teleported) + " m after the teleport, "
                + Harness.Fmt(untouched) + " m without it");
        }

        /// <summary>
        /// K1: the common case - already landed, teleporting elsewhere on the same body.
        /// clearInverseRotation drops the flag without releasing the anchor, so the re-entry
        /// later in the same frame resumes an anchor several minutes old.
        /// </summary>
        private static void ATeleportToTheSameSurfaceLeavesTheGroundWhereItWas()
        {
            CheckAgainstAnUntouchedTick("a teleport across one surface leaves the ground where it was",
                SameBody, landing: true);
        }

        /// <summary>
        /// K1: and the case where no anchor is held at all. Zup sat frozen through the whole
        /// inertial arc while the body kept turning, so the re-entry has to latch afresh against
        /// a frame hundreds of ticks removed from the last one Zup saw.
        /// </summary>
        private static void ATeleportFromOrbitToTheSurfaceLeavesTheGroundWhereItWas()
        {
            CheckAgainstAnUntouchedTick("a teleport from orbit to the surface leaves the ground where it was",
                FromOrbit, landing: true);
        }

        /// <summary>
        /// K1: the dominant-body handover, with the outgoing body's anchor still latched when the
        /// incoming one asks for its own.
        /// </summary>
        private static void ATeleportBetweenBodiesLeavesTheGroundWhereItWas()
        {
            CheckAgainstAnUntouchedTick("a teleport between bodies leaves the ground where it was",
                CrossBody, landing: true);
        }

        /// <summary>
        /// K1: teleporting to orbit instead, where the frame is left inertial and the anchor is
        /// released by the next CBUpdate rather than resumed. Nothing writes Zup during an
        /// inertial stretch, so the sky must not move - and an on-rails craft with it.
        /// </summary>
        private static void ATeleportToOrbitDoesNotMoveTheSky()
        {
            var scenario = SameBody();
            var sim = scenario.Sim;

            var before = sim.Zup;
            Teleport(sim, scenario.Bodies, scenario.Target, landing: false);
            sim.Tick(scenario.Bodies, scenario.Ut);

            Harness.CheckWithin("K1", "a teleport to orbit does not move the sky",
                Harness.FrameRotationAngle(before, sim.Zup), 1e-12, "deg");
        }

        /// <summary>
        /// K1: and the invariant the whole mod rests on has to survive the sequence - a body's
        /// orientation relative to the sky depends on its own pole and its own spin angle and on
        /// nothing else. A corrupted anchor shows up here as a body pointing at a different sky
        /// than its own rotation says it should, which is A8 arriving by a different route.
        ///
        /// The body the craft is actually on is exact. The others are allowed one tick of the
        /// rotating body's rotation, which is the update-order lag already catalogued under
        /// "Known residuals": BodyFrame reads Zup, and the body holding the rotating frame is the
        /// one that writes it, so anything updated earlier in tree order sees the previous tick's
        /// value. A teleport that hands the frame to a body further down the tree - Kerbin to the
        /// Mun here - makes that lag visible on the bodies above it. It is a constant offset
        /// rather than a growing one, and it is bounded, which is what this pins.
        /// </summary>
        private static void TheTeleportKeepsEveryBodyPointingAtItsOwnSky()
        {
            var scenario = CrossBody();
            Teleport(scenario.Sim, scenario.Bodies, scenario.Target, landing: true);
            scenario.Sim.Tick(scenario.Bodies, scenario.Ut);

            var target = Harness.MaxComponentError(
                scenario.Sim.SkyInBodyFrame(scenario.Target, Star()),
                Sim.ExpectedSkyInBodyFrame(scenario.Target, Star()));

            Harness.CheckWithin("K1", "the body the craft landed on points at exactly its own sky",
                target, 1e-12, "unit");

            //One tick of the Mun's own rotation, in radians - the ceiling on the update-order lag
            //for a unit vector.
            var ceiling = 2.0 * Math.PI * Tick / MunDay;

            Harness.Check("K1", "no other body is off by more than the update-order lag",
                WorstSkyError(scenario) <= ceiling,
                "worst " + Harness.Fmt(WorstSkyError(scenario)) + " against a one-tick ceiling of "
                + Harness.Fmt(ceiling));
        }

        private static double WorstSkyError(Scenario scenario)
        {
            var worst = 0.0;

            foreach (var body in scenario.Bodies)
            {
                worst = Math.Max(worst, Harness.MaxComponentError(
                    scenario.Sim.SkyInBodyFrame(body, Star()), Sim.ExpectedSkyInBodyFrame(body, Star())));
            }

            return worst;
        }

        /// <summary>
        /// K1: the regression witness. A check that cannot fail proves nothing, so this replays
        /// the cross-body teleport with the re-anchor suppressed, leaving the Mun to build Zup
        /// out of Kerbin's anchor and Kerbin's anchored rotation angle.
        ///
        /// That is not a hypothetical shape. Planetarium_ZupAtT reads TiltEm.ZupAnchor for
        /// whatever body it is handed without checking the anchor belongs to it, so any path that
        /// leaves a body inverse-rotating while a different one holds the anchor lands exactly
        /// here. It has to move the ground; if it ever stops doing so, the checks above are
        /// measuring nothing and their passes mean nothing.
        /// </summary>
        private static void AnAnchorFromTheWrongBodyReallyDoesMoveTheGround()
        {
            var scenario = CrossBody();
            scenario.Sim.SuppressReanchor = true;

            Teleport(scenario.Sim, scenario.Bodies, scenario.Target, landing: true);
            var moved = GroundStep(scenario.Sim, scenario.Bodies, scenario.Target, scenario.Ut);

            Harness.Check("K1", "an anchor from the wrong body really does move the ground",
                moved > 1000.0,
                "the Mun's surface jumps " + Harness.Fmt(moved / 1000.0) + " km in one tick");
        }
    }
}
