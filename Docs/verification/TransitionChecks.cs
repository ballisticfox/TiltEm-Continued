using System;
using System.Collections.Generic;
using UnityEngine;

namespace TiltEm.Verification
{
    /// <summary>
    /// The checks that matter: what actually happens when a vessel crosses
    /// inverseRotThresholdAltitude in either direction.
    /// </summary>
    public static class TransitionChecks
    {
        // Kerbin at its threshold, the worked example throughout the analysis.
        private const double KerbinRadius = 600000.0;
        private const double ThresholdAltitude = 100000.0;
        private const double VesselRadius = KerbinRadius + ThresholdAltitude;
        private const double KerbinRotationPeriod = 21549.425;
        private const double MunRotationPeriod = 138984.38;
        private const double Dt = 0.02; // one physics tick

        private static BodyTilt KerbinTilt
        {
            get { return TiltEmFrames.FromLegacyEuler(new Vector3d(20, 0, 5)); }
        }

        private static BodyTilt MunTilt
        {
            get { return TiltEmFrames.FromLegacyEuler(new Vector3d(15.45, 0, 10.61)); }
        }

        private static readonly Vector3d VesselCelestial = new Vector3d(0.31, -0.77, 0.56).normalized * VesselRadius;
        private static readonly Vector3d GroundBodyFixed = new Vector3d(0.62, 0.14, 0.77).normalized * KerbinRadius;

        public static void Run()
        {
            CrossingIsContinuous(true);
            CrossingIsContinuous(false);
            BothModesAgreeOnTheSky();
            DominantBodyChangeKeepsZupContinuous();
            AngularVelocityTracksThePole();
            AngularVelocityMatchesActualSurfaceMotion();
            NavballBasisIsConsistentWithTheTiltedPlanet();
            ReEntryAfterAnInertialArcIsContinuous(KerbinTilt, "tilted");
            ReEntryAfterAnInertialArcIsContinuous(TiltEmFrames.Untilted, "untilted");
        }

        /// <summary>What one mode flip does to the frames and to the things riding on them.</summary>
        private struct Crossing
        {
            /// <summary>Rotation between staying and flipping, degrees.</summary>
            public double ZupShift;
            public double BodyShift;

            /// <summary>Distance travelled during the tick on which the mode flips.</summary>
            public double VesselStep;
            public double GroundStep;

            /// <summary>The largest distance an ordinary tick moves them, in either mode.</summary>
            public double OrdinaryVesselStep;
            public double OrdinaryGroundStep;
        }

        /// <summary>
        /// Measures a crossing two ways.
        ///
        /// The frame shift replays the identical tick from identical prior state, once staying
        /// and once flipping, and compares the resulting frames component-wise. Component-wise
        /// rather than as a rotation angle, because acos is badly conditioned near zero and its
        /// own noise floor (~5e-10 deg here) sits above anything worth asserting.
        ///
        /// The step measures the actual per-tick motion across the switch and compares it with
        /// ordinary motion in both modes. That is the real "nothing jumped" test: per-tick motion
        /// legitimately changes character at the switch - the ground stops, the sky starts - so
        /// what must not happen is a step larger than either mode's normal one.
        /// </summary>
        private static Crossing MeasureCrossing(BodyTilt tilt, bool downward)
        {
            var sim = new Sim();
            var body = new SimBody("Kerbin", tilt, KerbinRotationPeriod, 137.4);

            // Settle into the mode we will be crossing out of.
            var ut = 5000.0;
            for (var i = 0; i < 50; i++)
            {
                sim.Tick(body, ut);
                ut += Dt;
            }

            if (!downward)
            {
                body.InverseRotation = true;
                for (var i = 0; i < 2000; i++)
                {
                    sim.Tick(body, ut);
                    ut += Dt;
                }
            }

            var beforeVessel = sim.OrbitToWorld(VesselCelestial);
            var beforeGround = Sim.SurfaceToWorld(body, GroundBodyFixed);

            // ut is now exactly one tick past the last one applied.
            var stayed = sim.Clone();
            var stayedBody = body.Clone();
            stayed.Tick(stayedBody, ut);

            var crossed = sim.Clone();
            var crossedBody = body.Clone();
            crossedBody.InverseRotation = downward;
            crossed.Tick(crossedBody, ut);

            // Ordinary per-tick motion in each mode, from the same prior state.
            var ordinaryStayed = OrdinaryStep(sim, body, ut, body.InverseRotation);
            var ordinaryFlipped = OrdinaryStep(sim, body, ut, downward);

            return new Crossing
            {
                ZupShift = Harness.FrameRotationAngle(stayed.Zup, crossed.Zup),
                BodyShift = Harness.FrameRotationAngle(stayedBody.BodyFrame, crossedBody.BodyFrame),
                VesselStep = (crossed.OrbitToWorld(VesselCelestial) - beforeVessel).magnitude,
                GroundStep = (Sim.SurfaceToWorld(crossedBody, GroundBodyFixed) - beforeGround).magnitude,
                OrdinaryVesselStep = Math.Max(ordinaryStayed.Key, ordinaryFlipped.Key),
                OrdinaryGroundStep = Math.Max(ordinaryStayed.Value, ordinaryFlipped.Value),
            };
        }

        /// <summary>Per-tick vessel and ground motion with the mode already settled.</summary>
        private static KeyValuePair<double, double> OrdinaryStep(Sim sim, SimBody body, double ut, bool rotating)
        {
            var s = sim.Clone();
            var b = body.Clone();
            b.InverseRotation = rotating;

            s.Tick(b, ut);
            var vesselFrom = s.OrbitToWorld(VesselCelestial);
            var groundFrom = Sim.SurfaceToWorld(b, GroundBodyFixed);

            s.Tick(b, ut + Dt);
            return new KeyValuePair<double, double>(
                (s.OrbitToWorld(VesselCelestial) - vesselFrom).magnitude,
                (Sim.SurfaceToWorld(b, GroundBodyFixed) - groundFrom).magnitude);
        }

        /// <summary>
        /// A1, A2, A3, A4. The core property.
        ///
        /// Per-tick *motion* legitimately changes character at the switch - that is the whole
        /// point of the reference-frame flip: the ground stops moving and the sky starts. Stock
        /// itself is not perfectly continuous either, because directRotAngle freezes at its
        /// previous value, so entering the rotating frame costs one tick of rotation. That is
        /// inherent to the design and is about four metres at Kerbin's threshold.
        ///
        /// So the property that actually matters is not "nothing moves" but "the tilt changes
        /// nothing". Measure the identical crossing for a tilted body and for an untilted one -
        /// which by definition behaves exactly as stock - and require the two to agree. Any
        /// discontinuity the tilt introduces shows up as a difference; the old code would show
        /// roughly 250 km of it.
        /// </summary>
        private static void CrossingIsContinuous(bool downward)
        {
            var label = downward ? "downward (inertial -> rotating)" : "upward (rotating -> inertial)";

            var tilted = MeasureCrossing(KerbinTilt, downward);
            var stock = MeasureCrossing(TiltEmFrames.Untilted, downward);

            Harness.CheckWithin("A1", "crossing " + label + ": tilt adds no extra Zup rotation",
                Math.Abs(tilted.ZupShift - stock.ZupShift), 1e-13, "deg");
            Harness.CheckWithin("A2", "crossing " + label + ": tilt adds no extra BodyFrame rotation",
                Math.Abs(tilted.BodyShift - stock.BodyShift), 1e-13, "deg");

            Harness.Check("A3", "crossing " + label + ": on-rails vessel takes no oversized step",
                tilted.VesselStep <= tilted.OrdinaryVesselStep + 1e-6,
                "crossing step " + Harness.Fmt(tilted.VesselStep) + " m, largest ordinary tick "
                + Harness.Fmt(tilted.OrdinaryVesselStep) + " m; the old code moved it ~250000 m");

            Harness.Check("A4", "crossing " + label + ": ground takes no oversized step",
                tilted.GroundStep <= tilted.OrdinaryGroundStep + 1e-6,
                "crossing step " + Harness.Fmt(tilted.GroundStep) + " m, largest ordinary tick "
                + Harness.Fmt(tilted.OrdinaryGroundStep) + " m; the old code moved it ~215000 m");
        }

        /// <summary>
        /// H1. An eccentric orbit crosses the threshold repeatedly, so the interesting case is
        /// not one crossing but the *second* entry - and every check above starts from a fresh
        /// world, so none of them ever reached it.
        ///
        /// The anchor is latched on entry and describes one continuous stretch in the rotating
        /// frame. While the body is inertial nothing writes Zup, so it sits frozen, but the body
        /// keeps turning. If the anchor survives that arc, re-entry evaluates Zup at
        /// elapsed = rotationAngle - ZupAnchorRotationAngle, which has grown by the whole arc,
        /// and Zup snaps forward by all of it in a single tick. Every on-rails vessel is
        /// positioned through Zup, so every one of them jumps.
        ///
        /// This is asymmetric by construction, which is exactly how it presents: leaving the
        /// rotating frame only freezes Zup, and freezing is always continuous.
        ///
        /// Checked for an untilted body too, because this is not a tilt bug - the pole-based
        /// rewrite introduced it for every body, where stock kept directRotAngle updated right
        /// through the arc and so had nothing stale to resume.
        /// </summary>
        private static void ReEntryAfterAnInertialArcIsContinuous(BodyTilt tilt, string label)
        {
            //Twenty minutes above the threshold - an ordinary coast for an eccentric orbit,
            //and about 20 degrees of Kerbin rotation.
            const double arcSeconds = 1200.0;

            var jump = MeasureReEntry(tilt, arcSeconds, false);
            var ordinary = OrdinaryStepAtReEntry(tilt, arcSeconds);

            Harness.Check("H1", "re-entry after an inertial arc (" + label + "): vessel takes no oversized step",
                jump <= ordinary + 1e-6,
                "re-entry step " + Harness.Fmt(jump) + " m, ordinary tick " + Harness.Fmt(ordinary) + " m");

            // Witness: the same run with the anchor left latched across the arc.
            var stale = MeasureReEntry(tilt, arcSeconds, true);

            Harness.Check("H1", "re-entry after an inertial arc (" + label + "): a stale anchor really does jump",
                stale > 1000.0,
                "resuming the anchor moves the vessel " + Harness.Fmt(stale / 1000.0) + " km in one tick, "
                + "against an ordinary " + Harness.Fmt(ordinary) + " m");
        }

        /// <summary>
        /// Runs rotating -> inertial -> rotating and returns how far the on-rails vessel moves on
        /// the tick that re-enters.
        /// </summary>
        private static double MeasureReEntry(BodyTilt tilt, double arcSeconds, bool keepStaleAnchor)
        {
            var sim = new Sim { KeepStaleAnchor = keepStaleAnchor };
            var body = new SimBody("Kerbin", tilt, KerbinRotationPeriod, 137.4);

            var ut = RunToReEntry(sim, body, arcSeconds);

            var before = sim.OrbitToWorld(VesselCelestial);

            body.InverseRotation = true;
            sim.Tick(body, ut);

            return (sim.OrbitToWorld(VesselCelestial) - before).magnitude;
        }

        /// <summary>The same arc, but staying inertial - what an ordinary tick moves the vessel.</summary>
        private static double OrdinaryStepAtReEntry(BodyTilt tilt, double arcSeconds)
        {
            var sim = new Sim();
            var body = new SimBody("Kerbin", tilt, KerbinRotationPeriod, 137.4);

            var ut = RunToReEntry(sim, body, arcSeconds);

            var before = sim.OrbitToWorld(VesselCelestial);
            sim.Tick(body, ut);
            var inertial = (sim.OrbitToWorld(VesselCelestial) - before).magnitude;

            // ...and once settled back into the rotating frame, where Zup is what moves.
            body.InverseRotation = true;
            for (var i = 0; i < 10; i++)
            {
                sim.Tick(body, ut);
                ut += Dt;
            }

            before = sim.OrbitToWorld(VesselCelestial);
            sim.Tick(body, ut);

            return Math.Max(inertial, (sim.OrbitToWorld(VesselCelestial) - before).magnitude);
        }

        /// <summary>
        /// Settles into the rotating frame, leaves it, coasts for an arc, and returns the UT of
        /// the tick that has not been applied yet - the one that will re-enter.
        /// </summary>
        private static double RunToReEntry(Sim sim, SimBody body, double arcSeconds)
        {
            var ut = 5000.0;

            for (var i = 0; i < 50; i++)
            {
                sim.Tick(body, ut);
                ut += Dt;
            }

            body.InverseRotation = true;
            for (var i = 0; i < 200; i++)
            {
                sim.Tick(body, ut);
                ut += Dt;
            }

            body.InverseRotation = false;

            // Coarse ticks across the coast, then fine ones so the state at re-entry is settled.
            // Nothing here depends on the step size: RotationAngle is computed from UT directly.
            const int coarse = 200;
            for (var i = 0; i < coarse; i++)
            {
                sim.Tick(body, ut);
                ut += arcSeconds / coarse;
            }

            for (var i = 0; i < 50; i++)
            {
                sim.Tick(body, ut);
                ut += Dt;
            }

            return ut;
        }

        /// <summary>
        /// A6. The sky must revolve about the body's tilted pole, identically on both sides of
        /// the threshold. Run the same body twice from the same state - once staying inertial,
        /// once switching to the rotating frame - and compare where a fixed star sits in
        /// body-fixed coordinates at matching times.
        /// </summary>
        private static void BothModesAgreeOnTheSky()
        {
            var star = new Vector3d(0.48, 0.61, -0.63).normalized;

            var inertial = new Sim();
            var inertialBody = new SimBody("Kerbin", KerbinTilt, KerbinRotationPeriod, 137.4);
            var rotating = new Sim();
            var rotatingBody = new SimBody("Kerbin", KerbinTilt, KerbinRotationPeriod, 137.4);

            var ut = 5000.0;
            for (var i = 0; i < 50; i++, ut += Dt)
            {
                inertial.Tick(inertialBody, ut);
                rotating.Tick(rotatingBody, ut);
            }

            rotatingBody.InverseRotation = true;

            var worst = 0.0;
            var declinationSwing = 0.0;
            var firstDeclination = double.NaN;

            // A full Kerbin day, sampled.
            for (var i = 0; i < 4000; i++)
            {
                ut += KerbinRotationPeriod / 4000.0;
                inertial.Tick(inertialBody, ut);
                rotating.Tick(rotatingBody, ut);

                var a = inertial.SkyInBodyFrame(inertialBody, star);
                var b = rotating.SkyInBodyFrame(rotatingBody, star);
                worst = Math.Max(worst, Harness.MaxComponentError(a, b));

                // The star's angle off the body's own +Z - its declination in body coordinates -
                // must hold steady across a day. That is what "revolves about the pole" means,
                // and it is the mechanism behind seasons and the subsolar latitude.
                if (double.IsNaN(firstDeclination)) firstDeclination = b.z;
                declinationSwing = Math.Max(declinationSwing, Math.Abs(b.z - firstDeclination));
            }

            Harness.CheckWithin("A6", "rotating and inertial modes place the sky identically",
                worst, 1e-12, "abs");
            Harness.CheckWithin("A6", "sky revolves about the body's pole (constant body-frame declination)",
                declinationSwing, 1e-12, "abs");
        }

        /// <summary>
        /// A7, E1. Handing the rotating frame from one body to another with a different pole
        /// must not move the sky. This also covers the Kopernicus case where inverseRotation is
        /// cleared without setRotatingFrame ever firing: the anchor is re-latched from whatever
        /// Zup currently holds, so the handover is continuous either way.
        /// </summary>
        private static void DominantBodyChangeKeepsZupContinuous()
        {
            double frozenDrift, handoverJump, stockHandover;
            RunHandover(KerbinTilt, MunTilt, out frozenDrift, out handoverJump);
            RunHandover(TiltEmFrames.Untilted, TiltEmFrames.Untilted, out _, out stockHandover);

            var worstCase = KerbinTilt.Obliquity + MunTilt.Obliquity;

            Harness.CheckWithin("A7", "Zup stays frozen while no body is rotating",
                frozenDrift, 1e-12, "deg");

            Harness.Check("A7", "handing the rotating frame to a differently-tilted body is continuous",
                Math.Abs(handoverJump - stockHandover) < 1e-12,
                "Zup turned " + Harness.Fmt(handoverJump) + " deg, same as stock's " + Harness.Fmt(stockHandover)
                + " deg one-tick step; without the anchor it would jump by up to " + Harness.Fmt(worstCase) + " deg");

            Harness.Check("E1", "anchor re-latches when setRotatingFrame never fired",
                Math.Abs(handoverJump - stockHandover) < 1e-12,
                "the Mun entered its rotating frame with the anchor still naming Kerbin; CBUpdate re-anchored");
        }

        private static void RunHandover(BodyTilt firstTilt, BodyTilt secondTilt,
            out double frozenDrift, out double handoverJump)
        {
            var sim = new Sim();
            var first = new SimBody("Kerbin", firstTilt, KerbinRotationPeriod, 137.4);

            var ut = 5000.0;
            for (var i = 0; i < 20; i++)
            {
                sim.Tick(first, ut);
                ut += Dt;
            }

            // The first body takes the rotating frame for a while.
            first.InverseRotation = true;
            for (var i = 0; i < 2000; i++)
            {
                sim.Tick(first, ut);
                ut += Dt;
            }

            // Leave it. Zup freezes wherever it stood.
            first.InverseRotation = false;
            for (var i = 0; i < 100; i++)
            {
                sim.Tick(first, ut);
                ut += Dt;
            }

            var frozenZup = sim.Zup;

            // The second body becomes dominant. Nothing clears the anchor - this is the
            // Kopernicus path, where setRotatingFrame never fires for the outgoing body.
            var second = new SimBody("Mun", secondTilt, MunRotationPeriod, 41.2);
            for (var i = 0; i < 10; i++)
            {
                sim.Tick(second, ut);
                ut += Dt;
            }

            frozenDrift = Harness.FrameRotationAngle(frozenZup, sim.Zup);

            // Same tick, both modes, as in MeasureCrossing.
            var stayed = sim.Clone();
            var stayedSecond = second.Clone();
            stayed.Tick(stayedSecond, ut);

            var entered = sim.Clone();
            var enteredSecond = second.Clone();
            enteredSecond.InverseRotation = true;
            entered.Tick(enteredSecond, ut);

            handoverJump = Harness.FrameRotationAngle(stayed.Zup, entered.Zup);
        }

        /// <summary>
        /// B1. The spin axis must be the body's actual pole in both modes, and must collapse
        /// onto stock's hardcoded constants when the body has no tilt.
        /// </summary>
        private static void AngularVelocityTracksThePole()
        {
            var omega = Math.PI * 2.0 / KerbinRotationPeriod;

            // Untilted must be bit-for-bit stock.
            var plain = new Sim();
            var plainBody = new SimBody("Plain", TiltEmFrames.Untilted, KerbinRotationPeriod, 137.4);
            plain.Tick(plainBody, 5000.0);

            var stockZUp = Vector3d.back * omega;
            var stockAngular = Vector3d.down * omega;

            Harness.CheckWithin("B1", "untilted zUpAngularVelocity == stock Vector3d.back * w",
                Harness.MaxComponentError(plainBody.ZUpAngularVelocity, stockZUp), 1e-18, "abs");
            Harness.CheckWithin("B1", "untilted angularVelocity == stock Vector3d.down * w",
                Harness.MaxComponentError(plainBody.AngularVelocity, stockAngular), 1e-18, "abs");

            // Tilted: axis follows the pole, magnitude unchanged, continuous across the switch.
            var sim = new Sim();
            var body = new SimBody("Kerbin", KerbinTilt, KerbinRotationPeriod, 137.4);
            var ut = 5000.0;
            var worstAxis = 0.0;
            var worstMagnitude = 0.0;
            var before = Vector3d.zero;
            var switchDelta = 0.0;

            for (var i = 0; i < 400; i++, ut += Dt)
            {
                if (i == 200)
                {
                    before = body.ZUpAngularVelocity;
                    body.InverseRotation = true;
                }

                sim.Tick(body, ut);

                if (i == 200) switchDelta = (body.ZUpAngularVelocity - before).magnitude;

                // Axis must be antiparallel to the body frame's pole. That sign is stock's
                // convention, pinned by the untilted comparison above.
                worstAxis = Math.Max(worstAxis,
                    Harness.MaxComponentError(body.ZUpAngularVelocity.normalized, body.BodyFrame.Z * -1.0));
                worstMagnitude = Math.Max(worstMagnitude, Math.Abs(body.ZUpAngularVelocity.magnitude - omega));
            }

            Harness.CheckWithin("B1", "tilted spin axis is the body's pole, in both modes", worstAxis, 1e-14, "abs");
            Harness.CheckWithin("B1", "tilted spin rate is unchanged by the tilt", worstMagnitude, 1e-18, "rad/s");
            Harness.CheckWithin("B4", "angular velocity is continuous across the crossing", switchDelta, 1e-15, "rad/s");

            // How wrong the untilted axis was above the threshold, at the threshold radius.
            var tilted = new Sim();
            var tiltedBody = new SimBody("Kerbin", KerbinTilt, KerbinRotationPeriod, 137.4);
            tilted.Tick(tiltedBody, 5000.0);
            var probe = new Vector3d(1, 0, 0) * VesselRadius;
            var correct = Vector3d.Cross(tiltedBody.ZUpAngularVelocity, -probe);
            var stock = Vector3d.Cross(stockZUp, -probe);

            Harness.Check("B2", "rotating-frame velocity step now uses the tilted axis",
                (correct - stock).magnitude > 1.0,
                "correct " + Harness.Fmt(correct.magnitude) + " m/s vs stock-axis " + Harness.Fmt(stock.magnitude)
                + " m/s, error the old code applied: " + Harness.Fmt((correct - stock).magnitude) + " m/s");
        }

        /// <summary>
        /// B2. Independent confirmation: the angular velocity must match the surface's actual
        /// motion, obtained by numerically differentiating a fixed surface point's world
        /// position. This is the consistency stock has for untilted bodies and that the old
        /// code broke by tilting the frames but not the axis.
        /// </summary>
        private static void AngularVelocityMatchesActualSurfaceMotion()
        {
            var sim = new Sim();
            var body = new SimBody("Kerbin", KerbinTilt, KerbinRotationPeriod, 137.4);

            var ut = 5000.0;
            const double h = 0.5; // seconds; central difference

            sim.Tick(body, ut - h);
            var back = Sim.SurfaceToWorld(body, GroundBodyFixed);
            sim.Tick(body, ut + h);
            var fwd = Sim.SurfaceToWorld(body, GroundBodyFixed);
            var measured = (fwd - back) / (2.0 * h);

            sim.Tick(body, ut);
            var here = Sim.SurfaceToWorld(body, GroundBodyFixed);
            var predicted = Vector3d.Cross(body.ZUpAngularVelocity, -here);

            var err = (measured - predicted).magnitude;
            var speed = measured.magnitude;

            Harness.Check("B2", "GetRotFrameVel formula matches differentiated surface motion",
                err / speed < 1e-6,
                "surface speed " + Harness.Fmt(speed) + " m/s, mismatch " + Harness.Fmt(err)
                + " m/s (" + Harness.Fmt(err / speed * 100.0) + "%)");
        }

        /// <summary>
        /// B3. The navball's east vector comes from getRFrmVel, so it is only meaningful if the
        /// angular velocity matches the planet the terrain is actually drawn on: east must be
        /// perpendicular both to local up and to the spin axis.
        /// </summary>
        private static void NavballBasisIsConsistentWithTheTiltedPlanet()
        {
            var sim = new Sim();
            var body = new SimBody("Kerbin", KerbinTilt, KerbinRotationPeriod, 137.4);
            sim.Tick(body, 5000.0);

            var worstUp = 0.0;
            var worstPole = 0.0;

            // Sample a spread of latitudes and longitudes on the surface.
            for (var lat = -80.0; lat <= 80.0; lat += 20.0)
            for (var lon = 0.0; lon < 360.0; lon += 45.0)
            {
                var latR = lat * (Math.PI / 180.0);
                var lonR = lon * (Math.PI / 180.0);
                var bodyFixed = new Vector3d(Math.Cos(latR) * Math.Cos(lonR),
                                             Math.Cos(latR) * Math.Sin(lonR),
                                             Math.Sin(latR)) * KerbinRadius;

                var world = Sim.SurfaceToWorld(body, bodyFixed);
                var east = Vector3d.Cross(body.ZUpAngularVelocity, -world).normalized;
                var up = world.normalized;
                var pole = body.BodyFrame.Z;

                worstUp = Math.Max(worstUp, Math.Abs(Vector3d.Dot(east, up)));
                worstPole = Math.Max(worstPole, Math.Abs(Vector3d.Dot(east, pole)));
            }

            Harness.CheckWithin("B3", "navball east is perpendicular to local up", worstUp, 1e-12, "dot");
            Harness.CheckWithin("B3", "navball east is perpendicular to the tilted spin axis", worstPole, 1e-12, "dot");
        }
    }
}
