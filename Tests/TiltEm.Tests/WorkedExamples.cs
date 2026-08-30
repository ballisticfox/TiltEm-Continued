using System;
using UnityEngine;

namespace TiltEm.Verification
{
    /// <summary>
    /// Prints the numbers quoted in the worked-examples section of
    /// Docs/TILT_MATHEMATICS.typ, computed by the shipped TiltEmFrames rather than by hand, so
    /// the document can be checked against the code it describes.
    ///
    /// Not a check: it asserts nothing. Driven by WorkedExampleTests; see the command in
    /// Docs/TILT_MATHEMATICS.typ to print the numbers.
    /// </summary>
    public static class WorkedExamples
    {
        private const double KerbinRotationPeriod = 21549.425;

        public static void Run()
        {
            var tilt = TiltEmFrames.FromLegacyEuler(new Vector3d(20, 0, 5));

            Console.WriteLine("== 1. the tilt frame ==");
            Console.WriteLine("poleRa      " + F(tilt.PoleRa));
            Console.WriteLine("poleDec     " + F(tilt.PoleDec));
            Console.WriteLine("primeMerid  " + F(tilt.PrimeMeridian));
            Console.WriteLine("obliquity   " + F(tilt.Obliquity));
            Console.WriteLine("T.X         " + V(tilt.Tilt.X));
            Console.WriteLine("T.Y         " + V(tilt.Tilt.Y));
            Console.WriteLine("T.Z (pole)  " + V(tilt.Tilt.Z));

            // A crossing. The body has spin phase theta0 and the sky has already been turned by
            // thetaInv when the vessel drops below the threshold.
            const double theta0 = 137.4;
            const double thetaInv = 100.0;
            const double elapsed = 90.0;

            var zupPrev = TiltEmFrames.Spin(thetaInv);

            var local0 = default(Planetarium.CelestialFrame);
            TiltEmFrames.LocalBodyFrame(tilt, theta0, ref local0);

            var c = default(Planetarium.CelestialFrame);
            TiltEmFrames.BodyFrame(tilt, theta0, zupPrev, ref c);

            Console.WriteLine();
            Console.WriteLine("== 2. entering the rotating frame, theta0=" + F(theta0)
                              + " thetaInv=" + F(thetaInv) + " ==");
            Console.WriteLine("L(theta0).Z " + V(local0.Z) + "   (the pole, celestial)");
            Console.WriteLine("C = B.Z     " + V(c.Z) + "   (the pole, world)");

            var anchor = TiltEmFrames.AnchorFor(tilt, theta0, c, zupPrev);
            Console.WriteLine("anchor A    " + V(anchor.X) + " / " + V(anchor.Y) + " / " + V(anchor.Z));
            Console.WriteLine("A vs Zup-   " + Harness.Fmt(Harness.FrameRotationAngle(anchor, zupPrev)) + " deg");

            // Advance.
            var zupE = TiltEmFrames.Zup(anchor, tilt, elapsed);
            var bE = default(Planetarium.CelestialFrame);
            TiltEmFrames.BodyFrame(tilt, theta0 + elapsed, zupE, ref bE);

            Console.WriteLine();
            Console.WriteLine("== 3. after e=" + F(elapsed) + " of elapsed rotation ==");
            Console.WriteLine("Zup turned  " + Harness.Fmt(Harness.FrameRotationAngle(zupPrev, zupE)) + " deg");
            Console.WriteLine("B moved     " + Harness.Fmt(Harness.FrameRotationAngle(c, bE)) + " deg");
            Console.WriteLine("B(e).Z      " + V(bE.Z));
            Console.WriteLine("pole moved  " + Harness.Fmt(Angle(c.Z, bE.Z)) + " deg");

            // Leaving. Nothing writes Zup once the flag clears, so it freezes where it stood,
            // the anchor is dropped, and the body resumes turning under a stationary sky.
            const double coast = 60.0;
            var thetaExit = theta0 + elapsed;
            var zupFrozen = zupE;

            var bExit = default(Planetarium.CelestialFrame);
            TiltEmFrames.BodyFrame(tilt, thetaExit, zupFrozen, ref bExit);

            var bCoasted = default(Planetarium.CelestialFrame);
            TiltEmFrames.BodyFrame(tilt, thetaExit + coast, zupFrozen, ref bCoasted);

            Console.WriteLine();
            Console.WriteLine("== 4. leaving, then coasting " + F(coast) + " deg inertial ==");
            Console.WriteLine("B at exit   " + Harness.Fmt(Harness.FrameRotationAngle(bE, bExit)) + " deg from B(e)");
            Console.WriteLine("Zup drift   " + Harness.Fmt(Harness.FrameRotationAngle(zupE, zupFrozen)) + " deg");
            Console.WriteLine("B turned    " + Harness.Fmt(Harness.FrameRotationAngle(bExit, bCoasted)) + " deg");
            Console.WriteLine("pole moved  " + Harness.Fmt(Angle(bExit.Z, bCoasted.Z)) + " deg");

            // Re-entry: released anchor vs the H1 failure of resuming a stale one.
            var fresh = TiltEmFrames.AnchorFor(tilt, thetaExit + coast, bCoasted, zupFrozen);
            var zupFresh = TiltEmFrames.Zup(fresh, tilt, 0.0);
            var zupStale = TiltEmFrames.Zup(anchor, tilt, thetaExit + coast - theta0);

            Console.WriteLine("re-entry    " + Harness.Fmt(Harness.FrameRotationAngle(zupFrozen, zupFresh))
                              + " deg with the anchor released");
            Console.WriteLine("stale H1    " + Harness.Fmt(Harness.FrameRotationAngle(zupFrozen, zupStale))
                              + " deg if it is not");

            // The naive construction, same inputs.
            var naive = default(Planetarium.CelestialFrame);
            TiltEmFrames.LocalBodyFrame(tilt, theta0 - thetaInv, ref naive);

            Console.WriteLine();
            Console.WriteLine("== 5. the naive construction at the same instant ==");
            Console.WriteLine("naive B.Z   " + V(naive.Z));
            Console.WriteLine("correct B.Z " + V(c.Z));
            Console.WriteLine("pole error  " + Harness.Fmt(Angle(naive.Z, c.Z)) + " deg");
            Console.WriteLine("predicted   " + Harness.Fmt(Predicted(tilt.Obliquity, thetaInv)) + " deg");
            Console.WriteLine("at 180 deg  " + Harness.Fmt(Predicted(tilt.Obliquity, 180.0)) + " deg");

            // Untilted reduction.
            var flat = TiltEmFrames.Untilted;
            var flatBody = default(Planetarium.CelestialFrame);
            TiltEmFrames.BodyFrame(flat, theta0, zupPrev, ref flatBody);
            var stock = TiltEmFrames.Spin(theta0 - thetaInv);

            Console.WriteLine();
            Console.WriteLine("== 6. untilted reduction ==");
            Console.WriteLine("B untilted  " + V(flatBody.X));
            Console.WriteLine("Rz(t - IRA) " + V(stock.X));
            Console.WriteLine("difference  " + Harness.Fmt(Harness.MaxComponentError(flatBody, stock)));
        }

        private static double Predicted(double obliquity, double thetaInv)
        {
            var e = obliquity * Math.PI / 180.0;
            var t = thetaInv * Math.PI / 180.0;
            return 2.0 * Math.Asin(Math.Min(1.0, Math.Sin(e) * Math.Abs(Math.Sin(t / 2.0)))) * 180.0 / Math.PI;
        }

        private static double Angle(Vector3d a, Vector3d b)
        {
            var d = Vector3d.Dot(a.normalized, b.normalized);
            return Math.Acos(Math.Max(-1.0, Math.Min(1.0, d))) * 180.0 / Math.PI;
        }

        private static string F(double v)
        {
            return v.ToString("F6");
        }

        private static string V(Vector3d v)
        {
            return "(" + v.x.ToString("F6") + ", " + v.y.ToString("F6") + ", " + v.z.ToString("F6") + ")";
        }
    }
}
