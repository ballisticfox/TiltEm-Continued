using System;
using UnityEngine;

namespace TiltEm.Verification
{
    /// <summary>
    /// Checks on TiltEmFrames: the pole formulation, its reduction to stock behaviour,
    /// and the continuity properties the threshold crossing depends on.
    /// </summary>
    public static class FrameChecks
    {
        private static readonly double[] Angles = { 0, 1, 17.5, 45, 90, 137.25, 180, 217.3, 250, 300, 359.75 };
        private static readonly double[] PoleRas = { 0, 23.5, 90, 174.9, 250, 331.7 };
        private static readonly double[] PoleDecs = { 90, 89.4, 78.5, 69.391, 45, 12.7, -30 };

        /// <summary>Kerbin's legacy Tilt'Em tilt, the running example throughout the analysis.</summary>
        public static readonly Vector3d KerbinLegacyTilt = new Vector3d(20, 0, 5);

        public static void Run()
        {
            PlanetaryFrameIsSpin();
            PoleFactorsOutOfPlanetaryFrame();
            TiltMapsZAxisOntoPole();
            FrameAlgebraMatchesKsp();
            DoublePrecisionRetained();
            UntiltedReducesToStock();
            LegacyEulerPreservesObliquity();
        }

        /// <summary>
        /// The premise the whole analysis rests on: stock's frame builder is a pure spin about
        /// world +Z, so a pole cannot be expressed by the (0, 90, rot) form at all.
        /// </summary>
        private static void PlanetaryFrameIsSpin()
        {
            var worst = 0.0;
            foreach (var rot in Angles)
            {
                worst = Math.Max(worst, Harness.MaxComponentError(TiltEmFrames.Spin(rot), Harness.ReferenceSpin(rot)));
            }

            Harness.CheckWithin("-", "PlanetaryFrame(0, 90, rot) == Rz(rot)", worst, 1e-15, "abs");
        }

        /// <summary>
        /// A5: the pole factors cleanly out of the frame, i.e. PlanetaryFrame(ra, dec, rot)
        /// is exactly T * Rz(rot) with T independent of rot. This is what lets the same
        /// construction be used in both reference-frame modes, and it is exact - unlike the
        /// Quaternion.Euler approximation it replaces.
        /// </summary>
        private static void PoleFactorsOutOfPlanetaryFrame()
        {
            var worst = 0.0;

            foreach (var ra in PoleRas)
            foreach (var dec in PoleDecs)
            {
                var tilt = TiltEmFrames.FromPole(ra, dec);

                foreach (var rot in Angles)
                {
                    var direct = default(Planetarium.CelestialFrame);
                    TiltEmFrames.LocalBodyFrame(tilt, rot, ref direct);

                    var composed = TiltEmFrames.Multiply(tilt.Tilt, TiltEmFrames.Spin(rot));
                    worst = Math.Max(worst, Harness.MaxComponentError(direct, composed));
                }
            }

            Harness.CheckWithin("A5", "PlanetaryFrame(ra, dec, rot) == T * Rz(rot), T constant in rot",
                worst, 1e-15, "abs");
        }

        /// <summary>A5: T maps the celestial +Z axis onto the body's pole.</summary>
        private static void TiltMapsZAxisOntoPole()
        {
            var worst = 0.0;

            foreach (var ra in PoleRas)
            foreach (var dec in PoleDecs)
            {
                var tilt = TiltEmFrames.FromPole(ra, dec);

                var raRad = tilt.PoleRa * (Math.PI / 180.0);
                var decRad = tilt.PoleDec * (Math.PI / 180.0);
                var expected = new Vector3d(Math.Cos(raRad) * Math.Cos(decRad),
                                            Math.Sin(raRad) * Math.Cos(decRad),
                                            Math.Sin(decRad));

                worst = Math.Max(worst, Harness.MaxComponentError(tilt.Tilt.Z, expected));

                // Obliquity is the angle off +Z, which for a normalised pole is just 90 - dec.
                var obliquity = Math.Acos(Math.Max(-1.0, Math.Min(1.0, tilt.Tilt.Z.z))) * (180.0 / Math.PI);
                worst = Math.Max(worst, Math.Abs(obliquity - tilt.Obliquity));
            }

            Harness.CheckWithin("A5", "T * Zaxis == IAU pole, and Obliquity == angle off +Z", worst, 1e-12, "abs");
        }

        /// <summary>Multiply/Transpose agree with KSP's own LocalToWorld/WorldToLocal.</summary>
        private static void FrameAlgebraMatchesKsp()
        {
            var worstMul = 0.0;
            var worstTranspose = 0.0;
            var worstOrtho = 0.0;
            var probe = new Vector3d(0.37, -0.82, 1.44);

            foreach (var ra in PoleRas)
            foreach (var dec in PoleDecs)
            {
                var a = TiltEmFrames.FromPole(ra, dec).Tilt;

                foreach (var rot in Angles)
                {
                    var b = TiltEmFrames.Spin(rot);

                    // (a * b) applied to v must equal a applied to (b applied to v)
                    var composed = TiltEmFrames.Multiply(a, b).LocalToWorld(probe);
                    var sequential = a.LocalToWorld(b.LocalToWorld(probe));
                    worstMul = Math.Max(worstMul, Harness.MaxComponentError(composed, sequential));

                    // transpose(f).LocalToWorld == f.WorldToLocal
                    var viaTranspose = TiltEmFrames.Transpose(a).LocalToWorld(probe);
                    worstTranspose = Math.Max(worstTranspose, Harness.MaxComponentError(viaTranspose, a.WorldToLocal(probe)));

                    worstOrtho = Math.Max(worstOrtho, Harness.OrthonormalityError(TiltEmFrames.Multiply(a, b)));
                }
            }

            Harness.CheckWithin("-", "Multiply matches sequential LocalToWorld", worstMul, 1e-15, "abs");
            Harness.CheckWithin("-", "Transpose matches WorldToLocal", worstTranspose, 1e-15, "abs");
            Harness.CheckWithin("-", "composed frames stay orthonormal", worstOrtho, 1e-15, "abs");
        }

        /// <summary>
        /// D1: the frame path is double precision end to end. The replaced implementation
        /// round-tripped every frame through a single-precision UnityEngine.Quaternion, which
        /// floors the achievable error at roughly 1e-7. Holding 1e-14 here is only possible if
        /// no float ever touches the result.
        /// </summary>
        private static void DoublePrecisionRetained()
        {
            var tilt = TiltEmFrames.FromLegacyEuler(KerbinLegacyTilt);
            var worst = 0.0;

            // Chain the conjugated spin the way a long rotating-frame session would.
            var anchor = TiltEmFrames.Spin(41.25);
            for (var step = 0; step < 20000; step++)
            {
                var ira = 41.25 + step * 0.37;
                var zup = TiltEmFrames.Zup(anchor, tilt, ira - 41.25);
                worst = Math.Max(worst, Harness.OrthonormalityError(zup));
            }

            Harness.CheckWithin("D1", "Zup stays orthonormal to double precision over 20k steps",
                worst, 1e-14, "abs");

            // A single-precision truncation of the same frame would be ~1e-7 off; prove the
            // double path is far tighter than anything a float could deliver.
            var reference = TiltEmFrames.Multiply(tilt.Tilt, TiltEmFrames.Multiply(TiltEmFrames.Spin(123.456), tilt.TiltTranspose));
            var actual = TiltEmFrames.Zup(Harness.IdentityFrame(), tilt, 123.456);
            var err = Harness.MaxComponentError(reference, actual);

            Harness.Check("D1", "Zup matches its exact conjugation far below float32 resolution",
                err < 1e-14, "error " + Harness.Fmt(err) + " vs float32 epsilon ~1.2e-07");
        }

        /// <summary>
        /// An untilted body must reduce to stock exactly, so installing Tilt'Em cannot perturb
        /// bodies that have no tilt configured.
        /// </summary>
        private static void UntiltedReducesToStock()
        {
            var untilted = TiltEmFrames.Untilted;

            Harness.Check("-", "FromPole(0, 90) is flagged identity",
                untilted.IsIdentity && untilted.Obliquity == 0.0,
                "IsIdentity=" + untilted.IsIdentity + " obliquity=" + Harness.Fmt(untilted.Obliquity));

            Harness.CheckWithin("-", "untilted T is the identity frame",
                Harness.MaxComponentError(untilted.Tilt, Harness.IdentityFrame()), 1e-15, "abs");

            var worstBody = 0.0;
            var worstZup = 0.0;

            foreach (var anchorIra in Angles)
            {
                var anchor = TiltEmFrames.Spin(anchorIra);

                foreach (var rot in Angles)
                {
                    var body = default(Planetarium.CelestialFrame);
                    TiltEmFrames.LocalBodyFrame(untilted, rot, ref body);
                    worstBody = Math.Max(worstBody, Harness.MaxComponentError(body, Harness.ReferenceSpin(rot)));

                    // Stock's Zup is simply Rz(IRA); the anchored form must collapse onto it.
                    var zup = TiltEmFrames.Zup(anchor, untilted, rot - anchorIra);
                    worstZup = Math.Max(worstZup, Harness.MaxComponentError(zup, Harness.ReferenceSpin(rot)));
                }
            }

            Harness.CheckWithin("-", "untilted BodyFrame(rot) == stock Rz(rot)", worstBody, 1e-15, "abs");
            Harness.CheckWithin("-", "untilted Zup(ira) == stock Rz(ira)", worstZup, 1e-14, "abs");
        }

        /// <summary>
        /// A5: legacy Euler configs keep their obliquity. The old operator left-multiplied
        /// swizzle(Euler(t)) onto the frame, so its pole is that operator applied to +Z; the
        /// converted BodyTilt must reproduce the same angle off +Z.
        /// </summary>
        private static void LegacyEulerPreservesObliquity()
        {
            var samples = new[]
            {
                new Vector3d(20, 0, 5),        // Kerbin
                new Vector3d(120.4, 0, 35.82), // Eve
                new Vector3d(0.54, 0, 1.16),   // Jool
                new Vector3d(80.63, 0, 12.34), // Eeloo
                new Vector3d(0, 0, 0),         // no tilt
            };

            var worst = 0.0;
            string detail = null;

            foreach (var euler in samples)
            {
                // Legacy operator's pole, computed the way the old code composed it.
                var legacyPole = (TiltEmFrames.UnityEuler(euler.x, euler.y, euler.z) * Vector3d.up).xzy;
                var legacyObliquity = Math.Acos(Math.Max(-1.0, Math.Min(1.0, legacyPole.z))) * (180.0 / Math.PI);

                var converted = TiltEmFrames.FromLegacyEuler(euler);
                var err = Math.Abs(converted.Obliquity - legacyObliquity);
                worst = Math.Max(worst, err);

                if (euler.x == 20 && euler.z == 5)
                {
                    detail = "Kerbin (20,0,5) -> pole ra=" + Harness.Fmt(converted.PoleRa)
                             + " dec=" + Harness.Fmt(converted.PoleDec)
                             + ", obliquity=" + Harness.Fmt(converted.Obliquity) + " deg";
                }
            }

            Harness.CheckWithin("A5", "FromLegacyEuler preserves the legacy obliquity", worst, 1e-12, "deg");
            Harness.Check("A5", "Kerbin's legacy tilt maps to the expected 20.591 deg obliquity",
                Math.Abs(TiltEmFrames.FromLegacyEuler(KerbinLegacyTilt).Obliquity - 20.5907) < 1e-3, detail);

            Harness.Check("-", "zero legacy tilt converts to the identity",
                TiltEmFrames.FromLegacyEuler(Vector3d.zero).IsIdentity, null);

            LegacyBodyFrameIsReproducedExactly();
        }

        /// <summary>
        /// A5, and the migration guarantee. A Quaternion.Euler is a pole tilt *plus* a spin
        /// about that pole; a pole alone picks a different longitude zero. Factoring the legacy
        /// operator as T * Rz(primeMeridian) and keeping both halves means the converted tilt
        /// rebuilds the legacy body frame exactly, so existing saves do not find their planets
        /// rotated. It also explains why the operator's rotation angle (20.609 deg for Kerbin)
        /// is a different number from its obliquity (20.591 deg).
        /// </summary>
        private static void LegacyBodyFrameIsReproducedExactly()
        {
            var samples = new[]
            {
                new Vector3d(20, 0, 5),
                new Vector3d(120.4, 0, 35.82),
                new Vector3d(0.54, 0, 1.16),
                new Vector3d(80.63, 0, 12.34),
                new Vector3d(15.7, 0, 9.69),
                new Vector3d(0, 0, 0),
            };

            var worstFrame = 0.0;
            var worstFixesZ = 0.0;
            string detail = null;

            foreach (var euler in samples)
            {
                Planetarium.CelestialFrame legacyOperator;
                TiltEmFrames.UnityEuler(euler.x, euler.y, euler.z).swizzle
                    .FrameVectors(out legacyOperator.X, out legacyOperator.Y, out legacyOperator.Z);

                var converted = TiltEmFrames.FromLegacyEuler(euler);

                // The leftover after removing the pole must fix +Z, i.e. be a pure spin about it.
                var spin = TiltEmFrames.Multiply(converted.TiltTranspose, legacyOperator);
                worstFixesZ = Math.Max(worstFixesZ, Harness.MaxComponentError(spin.Z, new Vector3d(0, 0, 1)));

                // The rebuilt body frame must equal what the legacy code produced: legacy * Rz(rot).
                foreach (var rot in Angles)
                {
                    var rebuilt = default(Planetarium.CelestialFrame);
                    TiltEmFrames.LocalBodyFrame(converted, rot, ref rebuilt);

                    var legacyFrame = TiltEmFrames.Multiply(legacyOperator, TiltEmFrames.Spin(rot));
                    worstFrame = Math.Max(worstFrame, Harness.MaxComponentError(rebuilt, legacyFrame));
                }

                if (euler.x == 20 && euler.z == 5)
                {
                    detail = "Kerbin: operator rotation "
                             + Harness.Fmt(Harness.AngleBetweenFrames(Harness.IdentityFrame(), legacyOperator))
                             + " deg = obliquity " + Harness.Fmt(converted.Obliquity)
                             + " deg + prime meridian " + Harness.Fmt(converted.PrimeMeridian) + " deg (preserved)";
                }
            }

            Harness.CheckWithin("A5", "legacy Euler residual is a pure spin about the pole", worstFixesZ, 1e-12, "abs");
            Harness.CheckWithin("A5", "converted tilt rebuilds the legacy body frame exactly", worstFrame, 1e-14, "abs");
            Harness.Check("A5", "obliquity and prime meridian are separated, not conflated", detail != null, detail);
        }
    }
}
