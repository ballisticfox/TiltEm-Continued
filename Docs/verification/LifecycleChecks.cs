using System;
using UnityEngine;

namespace TiltEm.Verification
{
    /// <summary>
    /// Initialisation and lifecycle checks.
    ///
    /// Two separate hazards live here, and they share a cause: stock's CBUpdate barely touches
    /// Planetarium.Zup, while the pole-based version is built on top of it.
    ///
    /// F1 - Zup is a static field with no initialiser, so it is a zero matrix until the first
    /// Planetarium.Awake, and CBUpdate runs long before that because Kopernicus and several
    /// stock systems call it directly while the system is still being built.
    ///
    /// F3 - stock does not write BodyFrame at all while a body is inverse-rotating, so the body
    /// is frozen literally, wherever it was pointing. The pole-based version recomputes it, so
    /// the value it freezes at is whatever the latched anchor implies. Getting that wrong moves
    /// the body after FloatingOrigin.SetOffset has already measured it.
    /// </summary>
    public static class LifecycleChecks
    {
        private static Planetarium.CelestialFrame Zeroed
        {
            get { return default(Planetarium.CelestialFrame); }
        }

        private static Planetarium.CelestialFrame NaNs
        {
            get
            {
                Planetarium.CelestialFrame f;
                f.X = new Vector3d(double.NaN, double.NaN, double.NaN);
                f.Y = new Vector3d(double.NaN, double.NaN, double.NaN);
                f.Z = new Vector3d(double.NaN, double.NaN, double.NaN);
                return f;
            }
        }

        private static Planetarium.CelestialFrame Scaled
        {
            get
            {
                Planetarium.CelestialFrame f;
                f.X = new Vector3d(2.0, 0.0, 0.0);
                f.Y = new Vector3d(0.0, 2.0, 0.0);
                f.Z = new Vector3d(0.0, 0.0, 2.0);
                return f;
            }
        }

        /// <summary>The bodies used for the latch checks: untilted, two real poles, a legacy Euler.</summary>
        private static BodyTilt[] Tilts()
        {
            return new[]
            {
                TiltEmFrames.Untilted,
                TiltEmFrames.FromPole(317.681070, 52.886356),   // Mars
                TiltEmFrames.FromPole(272.76, 67.16),           // Jupiter
                TiltEmFrames.FromLegacyEuler(new Vector3d(20, 0, 5)),
            };
        }

        public static void Run()
        {
            DegenerateFramesAreRejected();
            BodyFrameSurvivesAnUninitialisedPlanetarium();
            ZupSurvivesAnUninitialisedAnchor();
            TheGuardHasTeeth();

            LatchingLeavesAConsistentZupAlone();
            LatchingCannotMoveTheBody();
            LatchingOnlySpinsTheSkyAboutTheBodysOwnPole();
            TheBodyStaysFrozenAfterAStaleLatch();
            LatchingFromZupWouldHaveMovedTheBody();
        }

        /// <summary>F1: the predicate itself.</summary>
        private static void DegenerateFramesAreRejected()
        {
            Harness.Check("F1", "zero matrix is rejected", !TiltEmFrames.IsUsableRotation(Zeroed),
                "this is what Planetarium.Zup holds before the first Planetarium.Awake");
            Harness.Check("F1", "NaN frame is rejected", !TiltEmFrames.IsUsableRotation(NaNs), null);
            Harness.Check("F1", "non-unit frame is rejected", !TiltEmFrames.IsUsableRotation(Scaled), null);

            var allValid = true;
            foreach (var rot in new[] { 0.0, 37.0, 180.0, 301.5 })
            {
                allValid &= TiltEmFrames.IsUsableRotation(TiltEmFrames.Spin(rot));
                allValid &= TiltEmFrames.IsUsableRotation(TiltEmFrames.FromPole(123.4, 51.2).Tilt);
            }

            Harness.Check("F1", "real rotations are accepted", allValid, null);
            Harness.Check("F1", "identity is accepted", TiltEmFrames.IsUsableRotation(TiltEmFrames.Identity), null);
        }

        /// <summary>
        /// F1: with an unusable Zup, BodyFrame must fall back to the body's own celestial frame -
        /// which is what stock produces at that point, since InverseRotAngle is still zero.
        /// A zero result would put every surface feature at the body's centre.
        /// </summary>
        private static void BodyFrameSurvivesAnUninitialisedPlanetarium()
        {
            var tilts = new[]
            {
                TiltEmFrames.Untilted,
                TiltEmFrames.FromPole(317.681070, 52.886356),   // Mars
                TiltEmFrames.FromLegacyEuler(new Vector3d(20, 0, 5)),
            };

            var worst = 0.0;
            var worstOrtho = 0.0;

            foreach (var tilt in tilts)
            foreach (var broken in new[] { Zeroed, NaNs, Scaled })
            foreach (var rot in new[] { 0.0, 41.25, 197.6, 330.0 })
            {
                var actual = default(Planetarium.CelestialFrame);
                TiltEmFrames.BodyFrame(tilt, rot, broken, ref actual);

                var expected = default(Planetarium.CelestialFrame);
                TiltEmFrames.LocalBodyFrame(tilt, rot, ref expected);

                worst = Math.Max(worst, Harness.MaxComponentError(actual, expected));
                worstOrtho = Math.Max(worstOrtho, Harness.OrthonormalityError(actual));
            }

            Harness.CheckWithin("F1", "BodyFrame falls back to the body's own frame when Zup is unusable",
                worst, 1e-15, "abs");
            Harness.CheckWithin("F1", "BodyFrame stays orthonormal when Zup is unusable",
                worstOrtho, 1e-15, "abs");
        }

        /// <summary>F1: same guard on the anchor, which starts life as default(CelestialFrame).</summary>
        private static void ZupSurvivesAnUninitialisedAnchor()
        {
            var tilt = TiltEmFrames.FromPole(317.681070, 52.886356);
            var worstOrtho = 0.0;
            var worst = 0.0;

            foreach (var broken in new[] { Zeroed, NaNs, Scaled })
            foreach (var elapsed in new[] { 0.0, 12.5, 240.0 })
            {
                var zup = TiltEmFrames.Zup(broken, tilt, elapsed);
                worstOrtho = Math.Max(worstOrtho, Harness.OrthonormalityError(zup));

                // With the anchor treated as identity, Zup is just the conjugated spin.
                var expected = TiltEmFrames.Zup(TiltEmFrames.Identity, tilt, elapsed);
                worst = Math.Max(worst, Harness.MaxComponentError(zup, expected));
            }

            Harness.CheckWithin("F1", "Zup stays orthonormal with an unusable anchor", worstOrtho, 1e-15, "abs");
            Harness.CheckWithin("F1", "unusable anchor is treated as identity", worst, 1e-15, "abs");
        }

        /// <summary>
        /// Proof the F1 guard is load-bearing: composing with the raw zero matrix, as the code
        /// did before, produces a completely degenerate frame rather than a slightly wrong one.
        /// </summary>
        private static void TheGuardHasTeeth()
        {
            var tilt = TiltEmFrames.FromPole(317.681070, 52.886356);

            var local = default(Planetarium.CelestialFrame);
            TiltEmFrames.LocalBodyFrame(tilt, 41.25, ref local);

            // What BodyFrame used to compute, without OrIdentity.
            var unguarded = TiltEmFrames.Multiply(TiltEmFrames.Transpose(Zeroed), local);
            var collapsed = unguarded.X.sqrMagnitude + unguarded.Y.sqrMagnitude + unguarded.Z.sqrMagnitude;

            Harness.Check("F1", "the unguarded composition really does collapse",
                collapsed < 1e-30,
                "axis lengths sum to " + Harness.Fmt(collapsed) + " instead of 3 - every surface "
                + "point maps to the body's centre");
        }

        /// <summary>
        /// F3: where the old behaviour was right, the new one must be identical. Immediately
        /// before a threshold crossing the body frame is transpose(Zup) * T * Rz(rot), and the
        /// anchor derived from it has to come back as Zup itself - otherwise this fix would be
        /// paid for with a discontinuity at exactly the crossing the mod exists to get right.
        /// </summary>
        private static void LatchingLeavesAConsistentZupAlone()
        {
            var worst = 0.0;

            foreach (var tilt in Tilts())
            foreach (var zup in new[]
            {
                TiltEmFrames.Identity,
                TiltEmFrames.Spin(133.7),
                TiltEmFrames.FromPole(88.0, 12.5).Tilt,
            })
            foreach (var rot in new[] { 0.0, 41.25, 197.6, 330.0 })
            {
                var current = default(Planetarium.CelestialFrame);
                TiltEmFrames.BodyFrame(tilt, rot, zup, ref current);

                var anchor = TiltEmFrames.AnchorFor(tilt, rot, current, TiltEmFrames.Identity);
                worst = Math.Max(worst, Harness.FrameRotationAngle(anchor, zup));
            }

            Harness.CheckWithin("F3", "a consistent frame anchors back to Zup unchanged", worst, 1e-9, "deg");
        }

        /// <summary>
        /// F3: the invariant FloatingOrigin.SetOffset depends on. PSystemSetup.SetSpaceCentre
        /// flips the home body to inverseRotation, then captures scTransform.position - and
        /// scTransform hangs off the body transform. Whatever CBUpdate computes on the next tick
        /// must therefore be the frame the body is already in, however stale that frame is.
        /// </summary>
        private static void LatchingCannotMoveTheBody()
        {
            var worst = 0.0;
            var worstOrtho = 0.0;

            foreach (var tilt in Tilts())
            foreach (var stale in new[] { 0.0, 0.35, 96.0, 187.4, 359.9 })
            foreach (var rot in new[] { 12.0, 210.5 })
            {
                // The frame left behind by an earlier tick at a different angle entirely - in the
                // real failure, the one written at UT 0 during PSystemSetup.SetupSystem.
                var current = default(Planetarium.CelestialFrame);
                TiltEmFrames.BodyFrame(tilt, rot - stale, TiltEmFrames.Identity, ref current);

                var anchor = TiltEmFrames.AnchorFor(tilt, rot, current, TiltEmFrames.Identity);

                // What CBUpdate writes on the very next tick, with elapsed still zero.
                var latched = default(Planetarium.CelestialFrame);
                TiltEmFrames.BodyFrame(tilt, rot, TiltEmFrames.Zup(anchor, tilt, 0.0), ref latched);

                worst = Math.Max(worst, Harness.FrameRotationAngle(latched, current));
                worstOrtho = Math.Max(worstOrtho, Harness.OrthonormalityError(latched));
            }

            Harness.CheckWithin("F3", "taking the rotating frame does not move the body", worst, 1e-9, "deg");
            Harness.CheckWithin("F3", "the latched frame stays orthonormal", worstOrtho, 1e-15, "abs");
        }

        /// <summary>
        /// F3: freezing the body means the sky has to take up the difference, and it matters
        /// which way it turns. A spin about the body's own pole leaves the sub-solar latitude
        /// alone, so the body keeps its season. A spin about anything else would tip the body
        /// relative to the sun, which is the A8 failure over again.
        /// </summary>
        private static void LatchingOnlySpinsTheSkyAboutTheBodysOwnPole()
        {
            var worstAxis = 0.0;
            var tested = 0;

            foreach (var tilt in Tilts())
            {
                if (tilt.IsIdentity) continue;

                foreach (var stale in new[] { 17.0, 96.0, 187.4 })
                {
                    var zup = TiltEmFrames.Spin(64.0);

                    var current = default(Planetarium.CelestialFrame);
                    TiltEmFrames.BodyFrame(tilt, 210.5 - stale, zup, ref current);

                    var anchor = TiltEmFrames.AnchorFor(tilt, 210.5, current, TiltEmFrames.Identity);

                    // anchor * transpose(zup) is the extra turn the sky took. Its axis is the
                    // eigenvector for eigenvalue 1, and that has to be the body's pole.
                    var extra = TiltEmFrames.Multiply(anchor, TiltEmFrames.Transpose(zup));
                    var pole = tilt.Tilt.Z;

                    worstAxis = Math.Max(worstAxis, Harness.MaxComponentError(extra.LocalToWorld(pole), pole));
                    tested++;
                }
            }

            Harness.Check("F3", "the tilted-pole cases were actually exercised", tested == 9,
                "tested " + tested + " of 9");
            Harness.CheckWithin("F3", "the sky only turns about the body's own pole", worstAxis, 1e-12, "abs");
        }

        /// <summary>
        /// F3: a stale latch must not cost the freeze. Once anchored, the body frame has to stay
        /// put as the body keeps spinning, which is the entire point of the rotating frame.
        /// </summary>
        private static void TheBodyStaysFrozenAfterAStaleLatch()
        {
            var worst = 0.0;

            foreach (var tilt in Tilts())
            {
                const double rot0 = 210.5;

                var current = default(Planetarium.CelestialFrame);
                TiltEmFrames.BodyFrame(tilt, rot0 - 187.4, TiltEmFrames.Spin(64.0), ref current);

                var anchor = TiltEmFrames.AnchorFor(tilt, rot0, current, TiltEmFrames.Identity);

                foreach (var elapsed in new[] { 0.0, 0.02, 15.0, 180.0, 359.0, 1440.0 })
                {
                    var zup = TiltEmFrames.Zup(anchor, tilt, elapsed);

                    var frame = default(Planetarium.CelestialFrame);
                    TiltEmFrames.BodyFrame(tilt, rot0 + elapsed, zup, ref frame);

                    worst = Math.Max(worst, Harness.FrameRotationAngle(frame, current));
                }
            }

            Harness.CheckWithin("F3", "the body stays frozen after a stale latch", worst, 1e-9, "deg");
        }

        /// <summary>
        /// Proof the change is load-bearing, and the check that would have caught the bug.
        /// Anchoring on Planetarium.Zup instead swings the body by however far it had turned
        /// since its frame was last written - on entry to the space centre, the whole distance
        /// from UT 0 to the save's UT.
        /// </summary>
        private static void LatchingFromZupWouldHaveMovedTheBody()
        {
            var tilt = TiltEmFrames.FromLegacyEuler(new Vector3d(20, 0, 5));
            var zup = TiltEmFrames.Identity;
            const double rot0 = 210.5;
            const double stale = 187.4;

            var current = default(Planetarium.CelestialFrame);
            TiltEmFrames.BodyFrame(tilt, rot0 - stale, zup, ref current);

            // The old latch: anchor = Zup, so the frame gets rebuilt at the *current* angle.
            var oldLatched = default(Planetarium.CelestialFrame);
            TiltEmFrames.BodyFrame(tilt, rot0, TiltEmFrames.Zup(zup, tilt, 0.0), ref oldLatched);

            var moved = Harness.FrameRotationAngle(oldLatched, current);

            // A frame angle is the shortest turn between the two, so it wraps into [0, 180].
            var expected = stale > 180.0 ? 360.0 - stale : stale;

            // How far that drags a surface point on Kerbin, whose origin was already fixed.
            const double kerbinRadius = 600000.0;
            var metres = 2.0 * kerbinRadius * Math.Sin(moved * Math.PI / 360.0);

            Harness.Check("F3", "anchoring on Zup really does move the body",
                Math.Abs(moved - expected) < 1e-9,
                "the body swings " + Harness.Fmt(moved) + " deg, about " + Harness.Fmt(metres / 1000.0)
                + " km at Kerbin's surface, after FloatingOrigin.SetOffset has already been taken");
        }
    }
}
