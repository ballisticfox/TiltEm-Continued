using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace TiltEm.Verification
{
    /// <summary>
    /// Minimal pass/fail harness. Each check reports the worst error it saw so a
    /// regression shows up as a number, not just a red line.
    /// </summary>
    public static class Harness
    {
        public class Result
        {
            public string Defect;
            public string Name;
            public bool Passed;
            public string Detail;
        }

        private static List<Result> Results = new List<Result>();
        private static string _section = "";

        /// <summary>
        /// Runs one group of checks in isolation and hands back what it recorded, so each
        /// check can be surfaced as its own test rather than one pass/fail for the group.
        /// </summary>
        public static IReadOnlyList<Result> Capture(Action group)
        {
            List<Result> outer = Results;
            Results = new List<Result>();

            try
            {
                group();
                return Results;
            }
            finally
            {
                Results = outer;
            }
        }

        public static void Section(string name)
        {
            _section = name;
            Console.WriteLine();
            Console.WriteLine("=== " + name + " ===");
        }

        /// <param name="defect">Defect ID from Docs/REFERENCE_FRAME_DEFECTS.md, or "-" for a supporting check.</param>
        public static void Check(string defect, string name, bool passed, string detail)
        {
            Results.Add(new Result { Defect = defect, Name = name, Passed = passed, Detail = detail });
            Console.WriteLine("  [" + (passed ? "PASS" : "FAIL") + "] " + defect.PadRight(4) + " " + name);
            if (!string.IsNullOrEmpty(detail))
            {
                Console.WriteLine("         " + detail);
            }
        }

        /// <summary>Checks that <paramref name="error"/> is within tolerance, reporting the value either way.</summary>
        public static void CheckWithin(string defect, string name, double error, double tolerance, string units)
        {
            Check(defect, name, error <= tolerance,
                "max error " + Fmt(error) + " " + units + "  (tolerance " + Fmt(tolerance) + ")");
        }

        public static string Fmt(double v)
        {
            if (v == 0.0) return "0";
            var abs = Math.Abs(v);
            return abs < 1e-4 || abs >= 1e6 ? v.ToString("0.###e+00") : v.ToString("0.######");
        }

        public static int Report()
        {
            var failed = 0;
            var defects = new SortedDictionary<string, bool>();

            foreach (var r in Results)
            {
                if (!r.Passed) failed++;
                if (r.Defect == "-") continue;
                if (!defects.ContainsKey(r.Defect)) defects[r.Defect] = true;
                if (!r.Passed) defects[r.Defect] = false;
            }

            Console.WriteLine();
            Console.WriteLine("=== Summary ===");
            Console.WriteLine("  checks: " + Results.Count + "   passed: " + (Results.Count - failed) + "   failed: " + failed);
            Console.Write("  defects covered:");
            foreach (var kv in defects) Console.Write(" " + kv.Key + (kv.Value ? "" : "(FAIL)"));
            Console.WriteLine();
            Console.WriteLine();
            Console.WriteLine(failed == 0 ? "ALL CHECKS PASSED" : failed + " CHECK(S) FAILED");
            return failed == 0 ? 0 : 1;
        }

        #region KSP assembly loading

        /// <summary>
        /// KSP's assemblies reference each other by simple name and are not copied next to
        /// the harness, so redirect resolution at KSP's Managed folder.
        /// </summary>
        public static void HookAssemblyResolve(string managedPath)
        {
            AppDomain.CurrentDomain.AssemblyResolve += (sender, args) =>
            {
                var file = Path.Combine(managedPath, new AssemblyName(args.Name).Name + ".dll");
                return File.Exists(file) ? Assembly.LoadFrom(file) : null;
            };
        }

        #endregion

        #region Geometry helpers

        /// <summary>
        /// Rotation angle in degrees between two frames, computed from the Frobenius norm of
        /// their difference rather than from the trace.
        ///
        /// For a rotation of theta, ||R - I||_F == 2*sqrt(2)*sin(theta/2), so inverting through
        /// asin stays well conditioned all the way down to zero. The trace/acos form does not:
        /// near theta = 0 its own rounding noise is around 5e-10 deg, which is larger than the
        /// differences these checks need to resolve.
        ///
        /// Unlike a component-wise comparison this is axis-independent, so a one-tick rotation
        /// about a tilted pole and one about +Z compare equal - which is exactly the question
        /// "does the tilt add any extra rotation" is asking.
        /// </summary>
        public static double FrameRotationAngle(Planetarium.CelestialFrame a, Planetarium.CelestialFrame b)
        {
            var delta = TiltEmFrames.Multiply(TiltEmFrames.Transpose(a), b);

            var sumSquares = 0.0;
            var rows = new[] { delta.X, delta.Y, delta.Z };
            for (var i = 0; i < 3; i++)
            {
                var v = new[] { rows[i].x, rows[i].y, rows[i].z };
                for (var j = 0; j < 3; j++)
                {
                    var d = v[j] - (i == j ? 1.0 : 0.0);
                    sumSquares += d * d;
                }
            }

            var s = Math.Sqrt(sumSquares) / (2.0 * Math.Sqrt(2.0));
            return 2.0 * Math.Asin(Math.Min(1.0, s)) * (180.0 / Math.PI);
        }

        /// <summary>Rotation angle, in degrees, of the frame that takes <paramref name="a"/> to <paramref name="b"/>.</summary>
        public static double AngleBetweenFrames(Planetarium.CelestialFrame a, Planetarium.CelestialFrame b)
        {
            var delta = TiltEmFrames.Multiply(TiltEmFrames.Transpose(a), b);
            var trace = delta.X.x + delta.Y.y + delta.Z.z;
            var c = (trace - 1.0) / 2.0;
            if (c > 1.0) c = 1.0;
            if (c < -1.0) c = -1.0;
            return Math.Acos(c) * (180.0 / Math.PI);
        }

        public static double MaxComponentError(Planetarium.CelestialFrame a, Planetarium.CelestialFrame b)
        {
            return Math.Max(MaxComponentError(a.X, b.X),
                   Math.Max(MaxComponentError(a.Y, b.Y), MaxComponentError(a.Z, b.Z)));
        }

        public static double MaxComponentError(Vector3d a, Vector3d b)
        {
            return Math.Max(Math.Abs(a.x - b.x), Math.Max(Math.Abs(a.y - b.y), Math.Abs(a.z - b.z)));
        }

        /// <summary>Departure from orthonormality: worst |dot| between distinct axes, or |1 - |axis||.</summary>
        public static double OrthonormalityError(Planetarium.CelestialFrame f)
        {
            var err = Math.Abs(1.0 - f.X.magnitude);
            err = Math.Max(err, Math.Abs(1.0 - f.Y.magnitude));
            err = Math.Max(err, Math.Abs(1.0 - f.Z.magnitude));
            err = Math.Max(err, Math.Abs(Vector3d.Dot(f.X, f.Y)));
            err = Math.Max(err, Math.Abs(Vector3d.Dot(f.X, f.Z)));
            err = Math.Max(err, Math.Abs(Vector3d.Dot(f.Y, f.Z)));
            return err;
        }

        /// <summary>Rz(deg) built from raw trigonometry, independent of any KSP or Tilt'Em call.</summary>
        public static Planetarium.CelestialFrame ReferenceSpin(double deg)
        {
            var r = deg * (Math.PI / 180.0);
            double c = Math.Cos(r), s = Math.Sin(r);
            Planetarium.CelestialFrame f;
            f.X = new Vector3d(c, s, 0.0);
            f.Y = new Vector3d(-s, c, 0.0);
            f.Z = new Vector3d(0.0, 0.0, 1.0);
            return f;
        }

        public static Planetarium.CelestialFrame IdentityFrame()
        {
            Planetarium.CelestialFrame f;
            f.X = new Vector3d(1.0, 0.0, 0.0);
            f.Y = new Vector3d(0.0, 1.0, 0.0);
            f.Z = new Vector3d(0.0, 0.0, 1.0);
            return f;
        }

        #endregion
    }
}
