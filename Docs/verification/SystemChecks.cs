using System;
using System.Collections.Generic;
using UnityEngine;

namespace TiltEm.Verification
{
    /// <summary>
    /// Whole-system checks: many differently-tilted bodies coexisting while one of them holds
    /// the rotating frame and drives Zup.
    ///
    /// These exist because the original harness only ever simulated a single body, and with one
    /// body there is nothing for the planetarium frame to desynchronise against - every check
    /// passed just as happily with a formula that rotated every other tilted body by the
    /// rotating body's spin phase.
    /// </summary>
    public static class SystemChecks
    {
        private const double Dt = 0.02;

        /// <summary>Real poles from the IAU 2015 set, as used by the Sol pack, at JD2433647.5.</summary>
        private static List<SimBody> BuildSystem()
        {
            return new List<SimBody>
            {
                // name        poleRA      poleDec     rotation period (s)   initialRotation
                Body("Sun",     286.130000, 63.870000, 2192831.56143,        108.877000),
                Body("Mercury", 281.010290, 61.415499, 5067027.00596,        355.683276),
                Body("Venus",   272.760089, 67.159957, -20996797.0164,       122.900308),
                Body("Earth",     0.000000, 90.000000, 86164.09053984,       100.285139),
                Body("Moon",    270.138485, 66.603230, 2360584.68479999,     104.178222),
                Body("Mars",    317.681070, 52.886356, 88642.6632,            17.429512),
                Body("Phobos",  317.668693, 52.886252, 27553.843872,         185.408404),
                Body("Deimos",  316.397816, 53.639237, 109123.2,             204.792385),
                Body("Jupiter", 268.057763, 64.496027, 35729.711,             36.890988),
                Body("Uranus",  257.310050,-15.172541, -62063.712,           226.570810),
                Body("Pluto",   132.993000, -6.163000, 551856.672,           185.810000),
            };
        }

        private static SimBody Body(string name, double ra, double dec, double period, double rot)
        {
            return new SimBody(name, TiltEmFrames.FromPole(ra, dec), period, rot);
        }

        public static void Run()
        {
            SkyOrientationIsIndependentOfTheRotatingBody();
            UpdateOrderLagIsNegligible();
            RotatingBodyStaysFrozen();
            ObliquityIsStableWhileTheRotatingBodySpins();
            HandoverBetweenTiltedBodiesIsContinuous();
            TheseChecksDetectTheLegacyFormula();
        }

        /// <summary>
        /// The invariant. Every body's orientation relative to the sky must be a function of its
        /// own pole and its own spin angle - never of Zup, and never of which body currently
        /// holds the rotating frame or how far it has turned.
        /// </summary>
        private static void SkyOrientationIsIndependentOfTheRotatingBody()
        {
            var worst = RunSystemAndMeasureDrift(legacy: false, rotatingBody: "Earth", out var worstBody);

            Harness.Check("A8", "sky-in-body is invariant under the rotating body's spin",
                worst < 1e-12,
                "worst drift " + Harness.Fmt(worst) + " over a full day of Earth rotation"
                + (worstBody == null ? "" : " (" + worstBody + ")"));

            // Same again with a *tilted* body driving Zup, which is the harder case: Zup is then
            // a conjugated spin rather than a plain one.
            var worstTilted = RunSystemAndMeasureDrift(legacy: false, rotatingBody: "Mars", out var worstBody2);

            Harness.Check("A8", "invariant also holds with a tilted body driving Zup",
                worstTilted < 1e-12,
                "worst drift " + Harness.Fmt(worstTilted) + " with Mars rotating"
                + (worstBody2 == null ? "" : " (" + worstBody2 + ")"));
        }

        /// <summary>
        /// Runs the system for a day with <paramref name="rotatingBody"/> holding the rotating
        /// frame, and returns the worst deviation of any body's sky orientation from what its
        /// own rotation alone predicts.
        /// </summary>
        private static double RunSystemAndMeasureDrift(bool legacy, string rotatingBody, out string worstBody,
            double stepSeconds = 0.0, bool treeOrder = false)
        {
            var sim = new Sim { UseLegacyFormula = legacy };
            var bodies = BuildSystem();
            var star = new Vector3d(0.48, 0.61, -0.63).normalized;

            var driver = bodies.Find(b => b.Name == rotatingBody);

            // KSP walks the body tree from the root, so the rotating body is usually updated
            // partway through and the bodies before it see last tick's Zup. That one-tick lag is
            // measured separately by UpdateOrderLagIsNegligible; to isolate the formula itself,
            // update the rotating body first so Zup is always current.
            var order = new List<SimBody>(bodies);
            if (!treeOrder)
            {
                order.Remove(driver);
                order.Insert(0, driver);
            }

            var ut = 5000.0;
            for (var i = 0; i < 20; i++, ut += Dt) sim.Tick(order, ut);

            driver.InverseRotation = true;

            var worst = 0.0;
            worstBody = null;

            // A full rotation of the rotating body, so its spin phase sweeps the whole circle.
            var period = Math.Abs(driver.RotationPeriod);
            var step = stepSeconds > 0 ? stepSeconds : period / 720.0;
            var steps = (int)Math.Min(2000, Math.Max(1, period / step));

            //Tick first, then advance. Advancing first would skip a slot: the warm-up loop
            //already left ut on the next untaken tick, so the gap across the latch would have
            //been two ticks rather than one - and the latch is precisely where Zup steps.
            for (var i = 0; i < steps; i++)
            {
                sim.Tick(order, ut);
                ut += step;

                foreach (var body in bodies)
                {
                    var actual = sim.SkyInBodyFrame(body, star);
                    var expected = Sim.ExpectedSkyInBodyFrame(body, star);
                    var err = Harness.MaxComponentError(actual, expected);
                    if (err > worst)
                    {
                        worst = err;
                        worstBody = body.Name;
                    }
                }
            }

            return worst;
        }

        /// <summary>
        /// KSP updates bodies in tree order, so any body updated before the rotating one uses the
        /// previous tick's Zup. Stock has exactly the same lag - bodies ahead of the rotating one
        /// read a stale InverseRotAngle - so this is not something the tilt introduces. Measured
        /// at the real physics tick rate to confirm it is far below anything observable.
        /// </summary>
        private static void UpdateOrderLagIsNegligible()
        {
            string worstBody;
            var drift = RunSystemAndMeasureDrift(legacy: false, rotatingBody: "Earth", out worstBody,
                stepSeconds: Dt, treeOrder: true);

            // Convert the unit-vector error to an angle for a number that means something.
            var arcsec = Math.Asin(Math.Min(1.0, drift)) * (180.0 / Math.PI) * 3600.0;

            // One tick of Earth rotation is the theoretical ceiling on this lag.
            var oneTickArcsec = 360.0 / 86164.09053984 * Dt * 3600.0;

            Harness.Check("A8", "tree-order update lag stays under one tick of the rotating body",
                arcsec <= oneTickArcsec + 1e-6,
                "worst " + Harness.Fmt(arcsec) + " arcsec (" + worstBody + "), ceiling "
                + Harness.Fmt(oneTickArcsec) + " arcsec; stock carries the same lag");
        }

        /// <summary>
        /// The rotating body must be motionless in world - that is the entire purpose of the
        /// rotating frame, and it has to hold for a tilted body too.
        /// </summary>
        private static void RotatingBodyStaysFrozen()
        {
            foreach (var name in new[] { "Earth", "Mars", "Uranus" })
            {
                var sim = new Sim();
                var bodies = BuildSystem();
                var body = bodies.Find(b => b.Name == name);

                var ut = 5000.0;
                for (var i = 0; i < 20; i++, ut += Dt) sim.Tick(bodies, ut);

                body.InverseRotation = true;
                sim.Tick(bodies, ut);

                var ground = new Vector3d(0.62, 0.14, 0.77).normalized * 600000.0;
                var start = Sim.SurfaceToWorld(body, ground);
                var worst = 0.0;

                for (var step = 0; step < 500; step++)
                {
                    ut += Math.Abs(body.RotationPeriod) / 500.0;
                    sim.Tick(bodies, ut);
                    worst = Math.Max(worst, (Sim.SurfaceToWorld(body, ground) - start).magnitude);
                }

                Harness.CheckWithin("A8", name + " is frozen in world while it holds the rotating frame",
                    worst, 1e-6, "m");
            }
        }

        /// <summary>
        /// The observable that went wrong in game: Mars's sub-solar latitude flipped sign because
        /// its pole was effectively rotated by InverseRotAngle. Obliquity relative to a fixed
        /// direction must not move while some other body spins.
        /// </summary>
        private static void ObliquityIsStableWhileTheRotatingBodySpins()
        {
            var sim = new Sim();
            var bodies = BuildSystem();
            var mars = bodies.Find(b => b.Name == "Mars");
            var earth = bodies.Find(b => b.Name == "Earth");

            // Direction of the Sun from Mars at 1951-01-01, in the celestial frame, propagated
            // from the Sol pack's own ICRF orbital elements for Mars.
            var toSun = new Vector3d(-0.918211, 0.349990, 0.185458).normalized;

            var ut = 5000.0;
            for (var i = 0; i < 20; i++, ut += Dt) sim.Tick(bodies, ut);

            earth.InverseRotation = true;

            var first = double.NaN;
            var worst = 0.0;

            for (var step = 0; step < 720; step++)
            {
                ut += earth.RotationPeriod / 720.0;
                sim.Tick(bodies, ut);

                // Sub-solar latitude: the Sun's elevation above Mars's equator, in body coords.
                var sunInBody = sim.SkyInBodyFrame(mars, toSun);
                var lat = Math.Asin(Math.Max(-1.0, Math.Min(1.0, sunInBody.z))) * (180.0 / Math.PI);

                if (double.IsNaN(first)) first = lat;
                worst = Math.Max(worst, Math.Abs(lat - first));
            }

            Harness.Check("A8", "Mars's sub-solar latitude does not drift as Earth rotates",
                worst < 1e-9,
                "sub-solar latitude " + Harness.Fmt(first) + " deg, drift over a full Earth day "
                + Harness.Fmt(worst) + " deg");
        }

        /// <summary>
        /// A7 again, but in a populated system: handing the rotating frame between two tilted
        /// bodies must not move the sky for any body beyond the one-tick bookkeeping step that
        /// stock takes too.
        ///
        /// That step is not a defect and cannot be designed away. Whichever body holds the
        /// rotating frame freezes itself and drives Zup at its own rate, so on the tick the
        /// frame changes hands Zup advances by one tick of the *incoming* body rather than the
        /// outgoing one. Stock does exactly the same thing by a different route: it rebuilds
        /// InverseRotAngle as rotationAngle - directRotAngle, and the incoming body's
        /// directRotAngle is one tick stale. Running the identical handover with every tilt
        /// removed shows stock paying the same price.
        /// </summary>
        private static void HandoverBetweenTiltedBodiesIsContinuous()
        {
            string worstName, stockName;
            var worst = RunPhobosHandover(untilted: false, out worstName);
            var stock = RunPhobosHandover(untilted: true, out stockName);

            // One tick of Phobos's own rotation, as a unit-vector displacement.
            var oneTick = Math.Sin(2.0 * Math.PI * Dt / 27553.843872);

            Harness.Check("A7", "Mars-to-Phobos handover stays within stock's one-tick step",
                worst <= oneTick + 1e-12,
                "worst " + Harness.Fmt(worst) + (worstName == null ? "" : " (" + worstName + ")")
                + ", one tick of Phobos is " + Harness.Fmt(oneTick)
                + "; both bodies are tilted, with poles 0.8 deg apart");

            Harness.Check("A7", "and an untilted system pays the same one-tick step",
                stock > 0.0 && stock <= oneTick + 1e-12,
                "untilted worst " + Harness.Fmt(stock) + (stockName == null ? "" : " (" + stockName + ")")
                + " - so the step is stock's bookkeeping, not something the tilt introduces");
        }

        /// <summary>
        /// Runs Mars-rotating, then inertial, then Phobos taking over, and returns the worst
        /// change in any body's sky orientation across the handover tick.
        /// </summary>
        private static double RunPhobosHandover(bool untilted, out string worstName)
        {
            var sim = new Sim();
            var bodies = BuildSystem();

            if (untilted)
            {
                foreach (var body in bodies) body.Tilt = TiltEmFrames.Untilted;
            }

            var mars = bodies.Find(b => b.Name == "Mars");
            var star = new Vector3d(0.31, -0.77, 0.56).normalized;

            var ut = 5000.0;
            for (var i = 0; i < 20; i++, ut += Dt) sim.Tick(bodies, ut);

            mars.InverseRotation = true;
            for (var i = 0; i < 2000; i++, ut += Dt) sim.Tick(bodies, ut);

            mars.InverseRotation = false;
            for (var i = 0; i < 50; i++, ut += Dt) sim.Tick(bodies, ut);

            // Phobos takes over. Same tick, both modes, so only the flip differs.
            var stayed = sim.Clone();
            var stayedBodies = bodies.ConvertAll(b => b.Clone());
            stayed.Tick(stayedBodies, ut);

            var handed = sim.Clone();
            var handedBodies = bodies.ConvertAll(b => b.Clone());
            handedBodies.Find(b => b.Name == "Phobos").InverseRotation = true;
            handed.Tick(handedBodies, ut);

            var worst = 0.0;
            worstName = null;

            for (var i = 0; i < stayedBodies.Count; i++)
            {
                var err = Harness.MaxComponentError(
                    stayed.SkyInBodyFrame(stayedBodies[i], star),
                    handed.SkyInBodyFrame(handedBodies[i], star));
                if (err > worst)
                {
                    worst = err;
                    worstName = stayedBodies[i].Name;
                }
            }

            return worst;
        }

        /// <summary>
        /// Proof that the checks above have teeth. Replaying them against the pre-fix formula
        /// must produce a large violation - if it did not, passing them would mean nothing.
        /// </summary>
        private static void TheseChecksDetectTheLegacyFormula()
        {
            string worstBody;
            var legacyDrift = RunSystemAndMeasureDrift(legacy: true, rotatingBody: "Earth", out worstBody);

            Harness.Check("A8", "the invariant check detects the pre-fix formula",
                legacyDrift > 0.1,
                "pre-fix drift " + Harness.Fmt(legacyDrift) + " (worst: " + worstBody
                + ") vs post-fix < 1e-12 - a unit-vector error of ~2 means fully inverted");
        }
    }
}
