using System;
using UnityEngine;

namespace TiltEm.Verification
{
    /// <summary>
    /// Category L: sphere-of-influence handovers, and what happens when a body is left holding
    /// the rotating frame after it stops being dominant.
    ///
    /// The setup is not hypothetical. A body whose inverseRotThresholdAltitude reaches past its
    /// own SOI radius - Mimas in the real-scale packs - has no altitude inside its SOI that is
    /// above the threshold, so stock never gets an opportunity to clear the flag on the way out.
    /// OrbitPhysicsManager.setDominantBody does not clear it either; it only reassigns
    /// dominantBody and rebuilds vessel velocities. The body therefore leaves the SOI still
    /// flagged as rotating, and stays that way for the rest of the session.
    ///
    /// For stock that costs a skipped velocity handover on re-entry, because checkReferenceFrame
    /// calls setRotatingFrame only when the flag is not already set, plus getCentrifugalAcc and
    /// getCoriolisAcc reporting frame accelerations for a body nothing is orbiting. For this mod
    /// the cost is that two flagged bodies drive one Planetarium.Zup and one anchor: each takes
    /// the anchor from the other every tick, leaves Zup at its own freshly derived anchor, and
    /// the sky ends up advancing at whichever body happens to tick last. Measured below, that is
    /// the stale moon rather than the planet the vessel is actually orbiting.
    /// </summary>
    public static class DominantBodyChecks
    {
        private const double Tick = 0.02;
        private const double SaturnRadius = 60268000.0;
        private const double MimasRotationPeriod = 81425.0;
        private const double SaturnRotationPeriod = 38362.0;

        /// <summary>
        /// Long enough that every body's frame has been written against a Zup the rotating body
        /// produced, rather than against the identity Zup the world starts with. Without it the
        /// first handover measures the documented update-order lag instead of the defect.
        /// </summary>
        private const int SettleTicks = 5;

        private static BodyTilt MimasTilt
        {
            get { return TiltEmFrames.FromPole(40.66, 83.52); }
        }

        private static BodyTilt SaturnTilt
        {
            get { return TiltEmFrames.FromPole(40.589, 83.537); }
        }

        private static readonly Vector3d LandingSite = new Vector3d(0.32, 0.58, 0.75).normalized * SaturnRadius;

        public static void Run()
        {
            TheHandoverEndsTheOutgoingFrame();
            AStaleFlagCannotStealTheAnchor();
            AStaleFlagCannotMoveTheSky();
            TheSkyFollowsTheNewDominantBody();
            AnUnarbitratedFrameStillWorks();
            AnUnguardedStaleFlagDrivesTheSkyAtTheWrongRate();

            TheGuardIsInvisibleOnAnOrdinaryHandover();
            TheNewDominantBodyTakesTheFrameCleanly();
            ArrivingAlreadyBelowTheThreshold();
            ReturningToTheFirstBodyReLatches();
        }

        /// <summary>
        /// A vessel in Mimas's SOI with Mimas holding the rotating frame, settled, then handed
        /// over to Saturn without ever crossing a threshold.
        /// </summary>
        private class Scenario
        {
            public readonly Sim Sim = new Sim();
            public readonly SimBody Mimas = new SimBody("Mimas", MimasTilt, MimasRotationPeriod, 337.46);
            public readonly SimBody Saturn = new SimBody("Saturn", SaturnTilt, SaturnRotationPeriod, 38.9);
            public double Ut;

            private SimBody[] Bodies
            {
                get { return new[] { Saturn, Mimas }; }
            }

            public Scenario(double ut)
            {
                Ut = ut;
                Sim.Register(Saturn, Mimas);
                Sim.SetDominantBody(Mimas);
                Sim.SetRotatingFrame(Mimas, true);
                Settle();
            }

            public void Settle()
            {
                for (var i = 0; i < SettleTicks; i++)
                {
                    Ut += Tick;
                    Sim.Tick(Bodies, Ut);
                }
            }

            /// <summary>Leaves Mimas's SOI. With <paramref name="asStock"/> the flag survives.</summary>
            public void HandOver(bool asStock)
            {
                Sim.StockHandover = asStock;
                Sim.SetDominantBody(Saturn);
                Sim.StockHandover = false;
            }

            /// <summary>Saturn takes the rotating frame in the usual way, then settles.</summary>
            public void SaturnTakesTheFrame()
            {
                Sim.SetRotatingFrame(Saturn, true);
                Settle();
            }

            public void Coast(double seconds)
            {
                var end = Ut + seconds;
                while (Ut < end - Tick * 0.5)
                {
                    Ut += Tick;
                    Sim.Tick(Bodies, Ut);
                }
            }

            public Vector3d Ground()
            {
                return Sim.SurfaceToWorld(Saturn, LandingSite);
            }
        }

        /// <summary>
        /// The direct assertion: after the handover Mimas must be neither flagged nor anchored.
        /// </summary>
        private static void TheHandoverEndsTheOutgoingFrame()
        {
            var s = new Scenario(1000.0);
            s.HandOver(false);

            Harness.Check("L1", "the SOI handover ends the outgoing body's rotating frame",
                !s.Mimas.InverseRotation, "Mimas.inverseRotation = " + s.Mimas.InverseRotation);

            Harness.Check("L1", "the SOI handover releases the outgoing body's anchor",
                !s.Sim.HoldsAnchor("Mimas"), "anchor body = " + (s.Sim.AnchorBody ?? "none"));
        }

        /// <summary>
        /// Defence in depth. Even with the flag left set, by another mod, a load order that puts
        /// something between the patches, or a path that bypasses setDominantBody, a body that
        /// is not dominant must not take the anchor away from the one that is.
        /// </summary>
        private static void AStaleFlagCannotStealTheAnchor()
        {
            var s = new Scenario(2000.0);
            s.HandOver(true);
            s.SaturnTakesTheFrame();
            s.Coast(4.0);

            Harness.Check("L2", "a stale flag cannot take the anchor from the dominant body",
                s.Sim.HoldsAnchor("Saturn"), "anchor body = " + (s.Sim.AnchorBody ?? "none"));
        }

        /// <summary>
        /// The observable. A fixed point on the dominant body's surface must land in the same
        /// world position whether or not some other body is still flagged, and it must stay
        /// there, which is what a feedback loop would not do.
        /// </summary>
        private static void AStaleFlagCannotMoveTheSky()
        {
            double cleanDrift, staleDrift;
            var clean = Coasted(false, out cleanDrift);
            var stale = Coasted(true, out staleDrift);

            //FrameRotationAngle, not AngleBetweenFrames: the latter recovers the angle with
            //acos((trace - 1) / 2), which loses half its digits at zero and reports 1.2e-6 deg
            //for frames that are bit-identical. Same trap as the inclination decomposition.
            Harness.CheckWithin("L2", "a stale flag does not move the sky",
                Harness.FrameRotationAngle(clean, stale), 1e-9, "deg");

            Harness.CheckWithin("L2", "and the difference does not grow over ten seconds",
                Math.Abs(staleDrift - cleanDrift), 1e-9, "m");
        }

        private static Planetarium.CelestialFrame Coasted(bool leaveStaleFlag, out double drift)
        {
            var s = new Scenario(3000.0);
            s.HandOver(leaveStaleFlag);
            s.SaturnTakesTheFrame();

            var before = s.Ground();
            s.Coast(10.0);
            drift = (s.Ground() - before).magnitude;

            return s.Sim.Zup;
        }

        /// <summary>
        /// Having refused the stale body, the sky must still turn, about the new dominant body's
        /// pole and at the new dominant body's rate. A guard that simply froze everything would
        /// pass every check above and be useless.
        /// </summary>
        private static void TheSkyFollowsTheNewDominantBody()
        {
            var s = new Scenario(4000.0);
            s.HandOver(true);
            s.SaturnTakesTheFrame();

            const double span = 600.0;
            var start = s.Sim.Zup;
            var startAngle = s.Saturn.RotationAngle;

            s.Coast(span);

            var turned = Harness.FrameRotationAngle(start, s.Sim.Zup);
            var expected = Math.Abs(s.Saturn.RotationAngle - startAngle);

            Harness.CheckWithin("L2", "the sky turns at the new dominant body's rate",
                Math.Abs(turned - expected), 1e-9, "deg");

            //Not the stale body's rate, which is what it would follow if Mimas still drove Zup.
            var mimasRate = 360.0 * span / MimasRotationPeriod;

            Harness.Check("L2", "and not at the stale body's rate",
                Math.Abs(turned - mimasRate) > 1.0,
                "turned " + Harness.Fmt(turned) + " deg, Mimas would give " + Harness.Fmt(mimasRate));
        }

        /// <summary>
        /// Before the physics manager exists there is no dominant body to consult, and the space
        /// centre depends on the home body taking the frame anyway. The permissive fallback has
        /// to keep working, or F3 comes back.
        /// </summary>
        private static void AnUnarbitratedFrameStillWorks()
        {
            var sim = new Sim();
            var saturn = new SimBody("Saturn", SaturnTilt, SaturnRotationPeriod, 38.9);
            sim.Register(saturn);

            //DominantBody left null, as it is during system construction.
            sim.SetRotatingFrame(saturn, true);
            for (var i = 1; i <= SettleTicks; i++) sim.Tick(new[] { saturn }, 5000.0 + i * Tick);

            Harness.Check("L3", "with no dominant body the first claimant still holds the frame",
                sim.HoldsAnchor("Saturn"), "anchor body = " + (sim.AnchorBody ?? "none"));
        }

        /// <summary>
        /// The witness. Replays the same handover with both halves of the fix removed, stock's
        /// handover and no entitlement check in CBUpdate, and requires the result to be wrong.
        ///
        /// What goes wrong is not the obvious thing. The ground barely moves, well under a
        /// millimetre, because SurfaceToWorld composes transpose(Zup) with the body frame and a
        /// common error cancels between the two - the same blindness the teleport checks ran
        /// into, and the reason the observable here is the rate rather than a position.
        ///
        /// The rate is badly wrong. Both bodies re-latch the anchor from each other every tick,
        /// each leaving Zup at its own freshly derived anchor, so Zup advances at whichever body
        /// ticks last. That is Mimas, and the sky comes out turning at Mimas's rate: 2.65 deg
        /// per ten minutes where Saturn calls for 5.63. Every on-rails vessel is positioned
        /// through Zup, so all of them follow the wrong body.
        /// </summary>
        private static void AnUnguardedStaleFlagDrivesTheSkyAtTheWrongRate()
        {
            var s = new Scenario(6000.0);
            s.HandOver(true);
            s.Sim.IgnoreEntitlement = true;
            s.SaturnTakesTheFrame();

            const double span = 600.0;
            var start = s.Sim.Zup;
            var startAngle = s.Saturn.RotationAngle;

            s.Coast(span);

            var turned = Harness.FrameRotationAngle(start, s.Sim.Zup);
            var saturnRate = Math.Abs(s.Saturn.RotationAngle - startAngle);
            var mimasRate = 360.0 * span / MimasRotationPeriod;

            Harness.Check("L2", "unguarded, the sky really does follow the wrong body",
                Math.Abs(turned - saturnRate) > 1.0,
                "sky turned " + Harness.Fmt(turned) + " deg; Saturn calls for "
                + Harness.Fmt(saturnRate) + ", Mimas for " + Harness.Fmt(mimasRate));
        }

        // ---------------------------------------------------------------------------------
        // L4: the ordinary path, where nothing is stale. Everything above hands over while the
        // outgoing body is still rotating, which is the Mimas case. The common sequence is the
        // opposite: cross the threshold on the way out, coast, change SOI, cross the new body's
        // threshold on the way in. The guard has to be invisible there.
        // ---------------------------------------------------------------------------------

        private const double KerbinRotationPeriod = 21549.425;
        private const double MunRotationPeriod = 138984.38;

        private static BodyTilt KerbinTilt
        {
            get { return TiltEmFrames.FromLegacyEuler(new Vector3d(20, 0, 5)); }
        }

        private static BodyTilt MunTilt
        {
            get { return TiltEmFrames.FromLegacyEuler(new Vector3d(15.45, 0, 10.61)); }
        }

        /// <summary>
        /// Kerbin rotating, the vessel climbs out through the threshold, the SOI changes to the
        /// Mun with nothing rotating. <paramref name="trackDominant"/> selects whether the
        /// physics manager is present, which is what decides whether the guard arbitrates or
        /// falls back to the anchor holder.
        /// </summary>
        private static Sim OrdinaryHandover(bool trackDominant, out SimBody kerbin, out SimBody mun, out double ut)
        {
            var sim = new Sim();
            kerbin = new SimBody("Kerbin", KerbinTilt, KerbinRotationPeriod, 137.4);
            mun = new SimBody("Mun", MunTilt, MunRotationPeriod, 41.2);
            sim.Register(kerbin, mun);

            var bodies = new[] { kerbin, mun };
            ut = 7000.0;

            if (trackDominant) sim.SetDominantBody(kerbin);
            sim.SetRotatingFrame(kerbin, true);
            for (var i = 0; i < 50; i++) { ut += Tick; sim.Tick(bodies, ut); }

            //Out through the threshold: stock clears the flag, CBUpdate drops the anchor.
            sim.SetRotatingFrame(kerbin, false);
            for (var i = 0; i < 50; i++) { ut += Tick; sim.Tick(bodies, ut); }

            //Coast across the SOI boundary with nothing rotating.
            if (trackDominant) sim.SetDominantBody(mun);
            for (var i = 0; i < 50; i++) { ut += Tick; sim.Tick(bodies, ut); }

            return sim;
        }

        /// <summary>
        /// The question the guard has to answer for: does arbitrating change anything when there
        /// is nothing to arbitrate? Runs the ordinary sequence twice, once with a dominant body
        /// tracked and once without, and requires the two to agree.
        /// </summary>
        private static void TheGuardIsInvisibleOnAnOrdinaryHandover()
        {
            SimBody kerbinA, munA, kerbinB, munB;
            double utA, utB;

            var guarded = OrdinaryHandover(true, out kerbinA, out munA, out utA);
            var fallback = OrdinaryHandover(false, out kerbinB, out munB, out utB);

            Harness.CheckWithin("L4", "arbitrating changes nothing on an ordinary handover",
                Harness.FrameRotationAngle(guarded.Zup, fallback.Zup), 1e-9, "deg");

            Harness.CheckWithin("L4", "and leaves the body frames identical",
                Harness.MaxComponentError(munA.BodyFrame, munB.BodyFrame), 1e-14, "");

            Harness.Check("L4", "the anchor is released on the way out, before the SOI changes",
                guarded.AnchorBody == null, "anchor body = " + (guarded.AnchorBody ?? "none"));
        }

        /// <summary>
        /// The new dominant body then takes the frame. The guard must let it, the entry must be
        /// continuous, and the sky must go on at the new body's rate.
        /// </summary>
        private static void TheNewDominantBodyTakesTheFrameCleanly()
        {
            SimBody kerbin, mun;
            double ut;
            var sim = OrdinaryHandover(true, out kerbin, out mun, out ut);
            var bodies = new[] { kerbin, mun };

            var before = sim.Zup;
            sim.SetRotatingFrame(mun, true);
            ut += Tick;
            sim.Tick(bodies, ut);

            Harness.Check("L4", "the new dominant body is allowed to take the frame",
                sim.HoldsAnchor("Mun"), "anchor body = " + (sim.AnchorBody ?? "none"));

            //Continuity: the entry tick has to cost one ordinary tick of the Mun's rotation and
            //nothing more. Anything larger is the anchor latched against the wrong frame.
            var step = Harness.FrameRotationAngle(before, sim.Zup);
            var ordinary = 360.0 * Tick / MunRotationPeriod;

            Harness.CheckWithin("L4", "and entry costs exactly one ordinary tick",
                Math.Abs(step - ordinary), 1e-9, "deg");

            var start = sim.Zup;
            var startAngle = mun.RotationAngle;
            for (var i = 0; i < 5000; i++) { ut += Tick; sim.Tick(bodies, ut); }

            Harness.CheckWithin("L4", "and the sky then runs at the new body's rate",
                Math.Abs(Harness.FrameRotationAngle(start, sim.Zup) - Math.Abs(mun.RotationAngle - startAngle)),
                1e-9, "deg");
        }

        /// <summary>
        /// The other ordinary shape: arriving in an SOI already below the new body's threshold,
        /// where checkReferenceFrame calls setDominantBody and setRotatingFrame back to back with
        /// no tick in between. Low-periapsis encounters and aerocaptures do this.
        /// </summary>
        private static void ArrivingAlreadyBelowTheThreshold()
        {
            var sim = new Sim();
            var kerbin = new SimBody("Kerbin", KerbinTilt, KerbinRotationPeriod, 137.4);
            var mun = new SimBody("Mun", MunTilt, MunRotationPeriod, 41.2);
            sim.Register(kerbin, mun);
            var bodies = new[] { kerbin, mun };

            var ut = 8000.0;
            sim.SetDominantBody(kerbin);
            for (var i = 0; i < 50; i++) { ut += Tick; sim.Tick(bodies, ut); }

            //Same frame, no tick between them.
            sim.SetDominantBody(mun);
            sim.SetRotatingFrame(mun, true);

            var before = sim.Zup;
            ut += Tick;
            sim.Tick(bodies, ut);

            Harness.Check("L4", "arriving below the threshold still takes the frame",
                sim.HoldsAnchor("Mun"), "anchor body = " + (sim.AnchorBody ?? "none"));

            var step = Harness.FrameRotationAngle(before, sim.Zup);
            var ordinary = 360.0 * Tick / MunRotationPeriod;

            Harness.CheckWithin("L4", "and that entry is continuous too",
                Math.Abs(step - ordinary), 1e-9, "deg");
        }

        /// <summary>
        /// Kerbin, then the Mun, then Kerbin again. The anchor has to follow all the way round.
        /// This is H1's stale-anchor failure crossed with a dominant-body change, and it is the
        /// shape of an ordinary Mun return.
        /// </summary>
        private static void ReturningToTheFirstBodyReLatches()
        {
            SimBody kerbin, mun;
            double ut;
            var sim = OrdinaryHandover(true, out kerbin, out mun, out ut);
            var bodies = new[] { kerbin, mun };

            sim.SetRotatingFrame(mun, true);
            for (var i = 0; i < 200; i++) { ut += Tick; sim.Tick(bodies, ut); }

            sim.SetRotatingFrame(mun, false);
            for (var i = 0; i < 200; i++) { ut += Tick; sim.Tick(bodies, ut); }

            sim.SetDominantBody(kerbin);
            for (var i = 0; i < 50; i++) { ut += Tick; sim.Tick(bodies, ut); }

            var before = sim.Zup;
            sim.SetRotatingFrame(kerbin, true);
            ut += Tick;
            sim.Tick(bodies, ut);

            Harness.Check("L4", "returning to the first body re-latches to it",
                sim.HoldsAnchor("Kerbin"), "anchor body = " + (sim.AnchorBody ?? "none"));

            var step = Harness.FrameRotationAngle(before, sim.Zup);
            var ordinary = 360.0 * Tick / KerbinRotationPeriod;

            Harness.CheckWithin("L4", "and the return is continuous, not a stale-anchor jump",
                Math.Abs(step - ordinary), 1e-9, "deg");
        }
    }
}
