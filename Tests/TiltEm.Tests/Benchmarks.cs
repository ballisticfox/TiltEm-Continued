using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;

namespace TiltEm.Verification
{
    /// <summary>
    /// Cost of the per-tick frame work, measured rather than argued.
    ///
    /// Planetarium.FixedUpdate calls UpdateCBsRecursive, which calls CelestialBody.CBUpdate once
    /// per body per fixed update - 50 Hz at the stock time step. That loop is the only thing the
    /// mod adds to a running game that scales with anything, so it is the only thing worth
    /// measuring. Everything else is per-scene or per-frame-at-most: the toolbar, the axis
    /// renderer, the camera patches.
    ///
    /// The numbers come off the desktop CLR, not the Mono runtime Unity 2019.4 ships, so treat
    /// them as a ratio and an order of magnitude rather than as absolute times in game.
    /// </summary>
    public static class Benchmarks
    {
        private const int Warmup = 200_000;
        private const int Iterations = 4_000_000;

        private static readonly Dictionary<string, BodyTilt> Tilts = new Dictionary<string, BodyTilt>
        {
            { "Kerbin", TiltEmFrames.FromPole(104.348568, 69.409328) },
            { "Mun", TiltEmFrames.FromPole(12.0, 61.0) },
        };

        private static double _sink;

        public static void Run()
        {
            Console.WriteLine();
            Console.WriteLine("=== Per-body-tick frame cost ===");
            Console.WriteLine();

            var tilt = Tilts["Kerbin"];
            var untilted = TiltEmFrames.Untilted;
            var zup = TiltEmFrames.Zup(TiltEmFrames.Identity, tilt, 37.0);
            var anchor = TiltEmFrames.Identity;

            var stock = Bench("stock CBUpdate frame work", i =>
            {
                var f = default(Planetarium.CelestialFrame);
                Planetarium.CelestialFrame.PlanetaryFrame(0.0, 90.0, i * 1e-4, ref f);
                var q = f.Rotation.swizzle;
                return q.w;
            });

            var modUntilted = Bench("same body under Tilt'Em, no tilt", i =>
            {
                BodyTilt t;
                if (!Tilts.TryGetValue("Eeloo", out t)) t = untilted;

                var f = default(Planetarium.CelestialFrame);
                TiltEmFrames.BodyFrame(t, i * 1e-4, zup, ref f);
                var q = f.Rotation.swizzle;

                var w = f.Z * -0.000291;
                return q.w + w.x;
            });

            var modTilted = Bench("same body under Tilt'Em, tilted", i =>
            {
                BodyTilt t;
                if (!Tilts.TryGetValue("Kerbin", out t)) t = untilted;

                var f = default(Planetarium.CelestialFrame);
                TiltEmFrames.BodyFrame(t, i * 1e-4, zup, ref f);
                var q = f.Rotation.swizzle;

                var w = f.Z * -0.000291;
                return q.w + w.x;
            });

            var rotating = Bench("planetarium frame, the one rotating body", i =>
            {
                var z = TiltEmFrames.Zup(anchor, tilt, i * 1e-4);
                var q = QuaternionD.Inverse(z.Rotation).swizzle;
                return q.w;
            });

            Console.WriteLine();
            Console.WriteLine("=== Where the time goes ===");
            Console.WriteLine();

            Bench("baseline (loop + delegate call)", i => i * 1e-9);

            Bench("dictionary lookup, hit", i =>
            {
                BodyTilt t;
                Tilts.TryGetValue("Kerbin", out t);
                return t.PoleDec;
            });

            Bench("dictionary lookup, miss", i =>
            {
                BodyTilt t;
                if (!Tilts.TryGetValue("Eeloo", out t)) t = untilted;
                return t.PoleDec;
            });

            Bench("PlanetaryFrame, untilted pole", i =>
            {
                var f = default(Planetarium.CelestialFrame);
                Planetarium.CelestialFrame.PlanetaryFrame(0.0, 90.0, i * 1e-4, ref f);
                return f.Z.z;
            });

            Bench("PlanetaryFrame, real pole", i =>
            {
                var f = default(Planetarium.CelestialFrame);
                Planetarium.CelestialFrame.PlanetaryFrame(tilt.PoleRa, tilt.PoleDec, i * 1e-4, ref f);
                return f.Z.z;
            });

            Bench("frame -> quaternion (.Rotation.swizzle)", i =>
            {
                var q = zup.Rotation.swizzle;
                return q.w + i * 1e-9;
            });

            Bench("IsUsableRotation (the OrIdentity guard)", i =>
            {
                return TiltEmFrames.IsUsableRotation(zup) ? i * 1e-9 : 1.0;
            });

            Bench("Transpose + Multiply", i =>
            {
                var f = TiltEmFrames.Multiply(TiltEmFrames.Transpose(zup), zup);
                return f.Z.z + i * 1e-9;
            });

            Console.WriteLine();
            Console.WriteLine("=== Orbit evaluation (Planetarium.ZupAtT) ===");
            Console.WriteLine();

            //Orbit.GetOrbitalStateVectorsAtTrueAnomaly is the only caller, so this runs once per
            //orbit per update: every body, every on-rails vessel, and every patched conic drawn
            //in the map. The overwhelming majority of those have a reference body that is not
            //rotating, and that case leaves stock's own branch untouched.
            Bench("stock, reference body not rotating", i =>
            {
                var f = default(Planetarium.CelestialFrame);
                f.X = zup.X;
                f.Y = zup.Y;
                f.Z = zup.Z;
                return f.Z.z + i * 1e-9;
            });

            Bench("stock, reference body rotating", i =>
            {
                var f = default(Planetarium.CelestialFrame);
                Planetarium.CelestialFrame.PlanetaryFrame(0.0, 90.0, i * 1e-4, ref f);
                return f.Z.z;
            });

            Bench("Tilt'Em, reference body rotating", i =>
            {
                BodyTilt t;
                if (!Tilts.TryGetValue("Kerbin", out t)) t = untilted;

                var f = TiltEmFrames.Zup(anchor, t, i * 1e-4);
                return f.Z.z;
            });

            Console.WriteLine();
            Console.WriteLine("=== Marginal cost ===");
            Console.WriteLine();

            Report("untilted body", modUntilted - stock);
            Report("tilted body", modTilted - stock);
            Report("plus, once per tick, the rotating body's sky", rotating);

            Console.WriteLine();
            Console.WriteLine("=== Whole system, per fixed update at 50 Hz ===");
            Console.WriteLine();

            System("stock Kerbol (17 bodies, 1 tilted)", 17, 1, stock, modUntilted, modTilted, rotating);
            System("a full RSS-scale pack (60 bodies, all tilted)", 60, 60, stock, modUntilted, modTilted, rotating);

            Console.WriteLine();
            Console.WriteLine("checksum " + _sink.ToString("R"));
        }

        private static double Bench(string name, Func<int, double> body)
        {
            for (var i = 0; i < Warmup; i++) _sink += body(i);

            var sw = Stopwatch.StartNew();
            for (var i = 0; i < Iterations; i++) _sink += body(i);
            sw.Stop();

            var ns = sw.Elapsed.TotalMilliseconds * 1e6 / Iterations;
            Console.WriteLine("  " + name.PadRight(46) + ns.ToString("F1").PadLeft(7) + " ns");
            return ns;
        }

        private static void Report(string name, double ns)
        {
            Console.WriteLine("  " + name.PadRight(46) + ns.ToString("F1").PadLeft(7) + " ns");
        }

        /// <summary>
        /// What the whole body loop costs per fixed update, and what fraction of the 20 ms
        /// budget that is.
        /// </summary>
        private static void System(string name, int bodies, int tilted,
            double stock, double modUntilted, double modTilted, double rotating)
        {
            var before = bodies * stock;
            var after = (bodies - tilted) * modUntilted + tilted * modTilted + rotating;

            Console.WriteLine("  " + name);
            Console.WriteLine("      stock      " + (before / 1000.0).ToString("F2").PadLeft(7) + " us");
            Console.WriteLine("      Tilt'Em    " + (after / 1000.0).ToString("F2").PadLeft(7) + " us"
                              + "   (+" + ((after - before) / 1000.0).ToString("F2") + " us, "
                              + ((after - before) / 20_000_000.0 * 100.0).ToString("F4") + "% of a 20 ms step)");
        }
    }
}
