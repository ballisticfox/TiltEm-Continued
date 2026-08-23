using System;
using UnityEngine;

namespace TiltEm.Verification
{
    /// <summary>
    /// Camera framing.
    ///
    /// Both camera patches replace a hardcoded Vector3.up with a body's pole, but they need
    /// *different* poles, and picking the wrong one is silent: it looks almost right and drifts
    /// only while some body holds the rotating frame.
    ///
    /// G2, the map camera, cancels the sky itself with Planetarium.Rotation, so it needs the
    /// pole in the celestial frame - the tilt's own Z, which no Zup can move.
    ///
    /// G3, the in-flight Orbital camera, positions a transform in world space and cancels
    /// nothing, so it needs the pole in the world frame - BodyFrame's Z, which already carries
    /// transpose(Zup).
    ///
    /// The Unity call each patch then makes, Quaternion.FromToRotation, is native and cannot run
    /// outside the game; what is checked here is the axis handed to it.
    /// </summary>
    public static class CameraChecks
    {
        /// <summary>Celestial +Z swizzled into Unity space - the axis both patches replace.</summary>
        private static readonly Vector3d UnityUp = new Vector3d(0.0, 1.0, 0.0);

        public static void Run()
        {
            AnUntiltedInstallIsUntouched();
            TheWorldPoleDoesNotWobbleAsTheBodySpins();
            TheWorldPoleLeansByTheObliquity();
            TheCelestialPoleIgnoresTheSkyEntirely();
            TheTwoPolesReallyDoDiffer();
            SurfaceNorthIsTheBodysNorth();
        }

        /// <summary>
        /// The north tangent stock's SRF_NORTH case builds:
        /// AngleAxis(90, cross(pole, up)) * -up, with `up` the local vertical.
        ///
        /// QuaternionD.AngleAxis is managed, so this is the double-precision twin of the shipped
        /// expression rather than a re-derivation of it.
        /// </summary>
        private static Vector3d NorthTangent(Vector3d pole, Vector3d up)
        {
            var east = Vector3d.Cross(pole, up);
            return QuaternionD.AngleAxis(90.0, east) * -up;
        }

        /// <summary>What that must equal: the pole with the local vertical projected out of it.</summary>
        private static Vector3d TrueNorthTangent(Vector3d pole, Vector3d up)
        {
            return (pole - up * Vector3d.Dot(pole, up)).normalized;
        }

        /// <summary>
        /// G4: the Free and Auto cameras' north.
        ///
        /// Stock crosses the local vertical against Vector3.up to get east, then rotates straight
        /// down by 90 degrees about it to get north. That is only north if the axis crossed
        /// against the vertical is the body's actual spin axis - true for every stock body and
        /// for no tilted one. Substituting the pole makes the identity hold for any pole.
        /// </summary>
        private static void SurfaceNorthIsTheBodysNorth()
        {
            var poles = new[]
            {
                TiltEmFrames.Untilted.Tilt.Z,
                TiltEmFrames.FromPole(317.681070, 52.886356).Tilt.Z,   // Mars / Phobos
                TiltEmFrames.FromPole(257.311000, -15.175000).Tilt.Z,  // Uranus, pole 98 deg over
                TiltEmFrames.FromLegacyEuler(new Vector3d(20, 0, 5)).Tilt.Z,
            };

            var worst = 0.0;
            var worstStock = 0.0;
            var flipped = 0.0;
            var samples = 0;

            foreach (var pole in poles)
            foreach (var up in LocalVerticals())
            {
                // Skip the pole itself, where north is undefined and both forms are singular.
                if (Math.Abs(Vector3d.Dot(pole, up)) > 0.9999) continue;

                var expected = TrueNorthTangent(pole, up);
                worst = Math.Max(worst, Harness.MaxComponentError(NorthTangent(pole, up), expected));

                // What stock computes: the same expression with the celestial axis in place of
                // the pole. Identical for an untilted body, badly wrong for a tilted one.
                var stock = NorthTangent(new Vector3d(0.0, 0.0, 1.0), up);
                var err = Harness.MaxComponentError(stock, expected);
                worstStock = Math.Max(worstStock, err);
                flipped = Math.Max(flipped, -Vector3d.Dot(stock, expected));
                samples++;
            }

            Harness.Check("G4", "the north-tangent sweep actually ran", samples > 3000,
                samples + " pole/vertical pairs");

            Harness.CheckWithin("G4", "the body's pole gives the true north tangent", worst, 1e-12, "abs");

            Harness.Check("G4", "the celestial axis does not",
                worstStock > 1.0 && flipped > 0.99,
                "stock's north is off by up to " + Harness.Fmt(worstStock)
                + " and fully inverts somewhere (dot " + Harness.Fmt(-flipped)
                + ") - which is the flip seen in flight");
        }

        /// <summary>A spread of local verticals, i.e. surface points all over the body.</summary>
        private static Vector3d[] LocalVerticals()
        {
            var ups = new Vector3d[36 * 36];
            var i = 0;

            for (var lat = 0; lat < 36; lat++)
            for (var lon = 0; lon < 36; lon++)
            {
                var t = (lat + 0.5) * Math.PI / 36.0;
                var p = lon * 2.0 * Math.PI / 36.0;
                ups[i++] = new Vector3d(Math.Sin(t) * Math.Cos(p), Math.Sin(t) * Math.Sin(p), Math.Cos(t));
            }

            return ups;
        }

        /// <summary>
        /// G3: with nothing tilted anywhere, the world pole must come out as exactly Unity up,
        /// so the Orbital camera patch short-circuits and stock framing is untouched. "Exactly"
        /// is the point - a near-miss would still be a no-op visually but would leave the patch
        /// composing a micro-rotation onto every frame.
        /// </summary>
        private static void AnUntiltedInstallIsUntouched()
        {
            var worst = 0.0;

            foreach (var zup in new[] { TiltEmFrames.Identity, TiltEmFrames.Spin(0.0), TiltEmFrames.Spin(217.5) })
            foreach (var rot in new[] { 0.0, 41.25, 197.6, 330.0 })
            {
                var frame = default(Planetarium.CelestialFrame);
                TiltEmFrames.BodyFrame(TiltEmFrames.Untilted, rot, zup, ref frame);

                worst = Math.Max(worst, Harness.MaxComponentError(frame.Z.xzy, UnityUp));
            }

            Harness.CheckWithin("G3", "an untilted body's world pole is exactly Unity up",
                worst, 1e-15, "abs");
        }

        /// <summary>
        /// G3: the pole is the one axis a spin cannot move. If it drifted with rotationAngle the
        /// Orbital camera would creep - and on a fast body, visibly shear - as the planet turned
        /// under it.
        /// </summary>
        private static void TheWorldPoleDoesNotWobbleAsTheBodySpins()
        {
            var worst = 0.0;

            foreach (var tilt in new[]
            {
                TiltEmFrames.Untilted,
                TiltEmFrames.FromPole(317.681070, 52.886356),   // Mars
                TiltEmFrames.FromPole(272.76, 67.16),           // Jupiter
                TiltEmFrames.FromLegacyEuler(new Vector3d(20, 0, 5)),
            })
            {
                var zup = TiltEmFrames.Spin(64.0);

                var reference = default(Planetarium.CelestialFrame);
                TiltEmFrames.BodyFrame(tilt, 0.0, zup, ref reference);

                foreach (var rot in new[] { 0.0, 0.02, 41.25, 197.6, 330.0, 359.99 })
                {
                    var frame = default(Planetarium.CelestialFrame);
                    TiltEmFrames.BodyFrame(tilt, rot, zup, ref frame);

                    worst = Math.Max(worst, Harness.MaxComponentError(frame.Z.xzy, reference.Z.xzy));
                }
            }

            Harness.CheckWithin("G3", "the world pole is invariant under the body's own spin",
                worst, 1e-15, "abs");
        }

        /// <summary>
        /// G3: and it leans by the right amount. The angle between the world pole and Unity up,
        /// with an identity Zup, is the body's obliquity - so the Orbital camera's horizon tips
        /// by the obliquity and no more.
        /// </summary>
        private static void TheWorldPoleLeansByTheObliquity()
        {
            var worst = 0.0;

            foreach (var tilt in new[]
            {
                TiltEmFrames.FromPole(317.681070, 52.886356),   // Mars,    37.11 deg
                TiltEmFrames.FromPole(272.76, 67.16),           // Jupiter, 22.84 deg
                TiltEmFrames.FromLegacyEuler(new Vector3d(20, 0, 5)),
            })
            {
                var frame = default(Planetarium.CelestialFrame);
                TiltEmFrames.BodyFrame(tilt, 123.4, TiltEmFrames.Identity, ref frame);

                var dot = Vector3d.Dot(frame.Z.xzy, UnityUp);
                var degrees = Math.Acos(dot < -1.0 ? -1.0 : (dot > 1.0 ? 1.0 : dot)) * 180.0 / Math.PI;

                worst = Math.Max(worst, Math.Abs(degrees - tilt.Obliquity));
            }

            Harness.CheckWithin("G3", "the world pole leans by exactly the obliquity", worst, 1e-9, "deg");
        }

        /// <summary>
        /// G2: the map camera's pole must be the one Zup cannot touch, because that patch has
        /// already cancelled the sky with Planetarium.Rotation. Handing it a world pole would
        /// apply Zup twice and swing the map view as any craft anywhere crossed a threshold.
        /// </summary>
        private static void TheCelestialPoleIgnoresTheSkyEntirely()
        {
            var tilt = TiltEmFrames.FromPole(317.681070, 52.886356);
            var reference = tilt.Tilt.Z;
            var worst = 0.0;
            var skyMoved = 0.0;

            foreach (var elapsed in new[] { 0.0, 15.0, 90.0, 180.0, 359.0, 1440.0 })
            {
                // The sky really is moving - if it were not, this would prove nothing.
                var zup = TiltEmFrames.Zup(TiltEmFrames.Spin(64.0), tilt, elapsed);

                var frame = default(Planetarium.CelestialFrame);
                TiltEmFrames.BodyFrame(tilt, 41.25, zup, ref frame);
                skyMoved = Math.Max(skyMoved, Harness.MaxComponentError(frame.Z, reference));

                worst = Math.Max(worst, Harness.MaxComponentError(tilt.Tilt.Z, reference));
            }

            Harness.CheckWithin("G2", "the celestial pole is unaffected by the rotating frame",
                worst, 1e-15, "abs");
            Harness.Check("G2", "and the world pole it is being compared against did move",
                skyMoved > 0.1, "world pole shifted by up to " + Harness.Fmt(skyMoved) + " over the same sweep");
        }

        /// <summary>
        /// Proof the distinction is load-bearing rather than pedantry: while a tilted body holds
        /// the rotating frame, even a completely untilted body's world pole is not Unity up. So
        /// the Orbital camera cannot read the pole off the tilt - a body with no tilt configured
        /// at all still needs its pole taken from BodyFrame.
        /// </summary>
        private static void TheTwoPolesReallyDoDiffer()
        {
            var rotating = TiltEmFrames.FromPole(317.681070, 52.886356);
            var zup = TiltEmFrames.Zup(TiltEmFrames.Identity, rotating, 137.0);

            var frame = default(Planetarium.CelestialFrame);
            TiltEmFrames.BodyFrame(TiltEmFrames.Untilted, 41.25, zup, ref frame);

            var dot = Vector3d.Dot(frame.Z.xzy, UnityUp);
            var degrees = Math.Acos(dot < -1.0 ? -1.0 : (dot > 1.0 ? 1.0 : dot)) * 180.0 / Math.PI;

            Harness.Check("G3", "an untilted body's world pole moves with someone else's sky",
                degrees > 1.0,
                "leans " + Harness.Fmt(degrees) + " deg while a tilted body holds the rotating "
                + "frame - reading the pole off the tilt would have returned Unity up here");
        }
    }
}
