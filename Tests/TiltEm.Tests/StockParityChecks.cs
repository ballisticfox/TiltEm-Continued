using System;
using System.Collections.Generic;
using UnityEngine;
using Xunit;

namespace TiltEm.Verification
{
    /// <summary>
    /// Hand-inlined copies of stock arithmetic, checked bit for bit against the game's own code.
    /// Real-assemblies mode only; the shims do not carry the members being copied.
    /// </summary>
    public static class StockParityChecks
    {
        public static void Run()
        {
            UnityRotationMatchesStock();
        }

        /// <summary>
        /// UnityRotation replaces frame.Rotation.swizzle in CBUpdate, so it has to agree exactly,
        /// on every branch of stock's matrix-to-quaternion conversion.
        /// </summary>
        private static void UnityRotationMatchesStock()
        {
            System.Random random = new System.Random(3);
            int mismatches = 0;
            bool[] branches = new bool[4];

            for (int n = 0; n < 200000; n++)
            {
                BodyTilt tilt = TiltEmFrames.FromPole(random.NextDouble() * 360.0,
                    random.NextDouble() * 180.0 - 90.0);
                Planetarium.CelestialFrame frame = TiltEmFrames.Zup(
                    TiltEmFrames.Spin(random.NextDouble() * 360.0), tilt, random.NextDouble() * 720.0);

                branches[Branch(frame)] = true;

                QuaternionD stock = frame.Rotation.swizzle;
                QuaternionD mod = TiltEmFrames.UnityRotation(frame);

                if (stock.x != mod.x || stock.y != mod.y || stock.z != mod.z || stock.w != mod.w)
                {
                    mismatches++;
                }
            }

            Harness.Check("-", "the sample reaches all four branches of the conversion",
                branches[0] && branches[1] && branches[2] && branches[3], null);
            Harness.Check("-", "UnityRotation equals frame.Rotation.swizzle bit for bit",
                mismatches == 0, mismatches + " of 200000 differ");
        }

        /// <summary>Which branch stock's QuaternionD(X, Y, Z) constructor takes for this frame.</summary>
        private static int Branch(Planetarium.CelestialFrame f)
        {
            if (f.X.x + f.Y.y + f.Z.z >= 0.0) return 0;
            if (f.X.x > f.Y.y && f.X.x > f.Z.z) return 1;
            return f.Y.y > f.Z.z ? 2 : 3;
        }
    }

    /// <summary>Hand-inlined stock arithmetic against the game's own.</summary>
    public class StockParityCheckTests
    {
        private const string Group = "StockParityChecks";

        private static void Body() => StockParityChecks.Run();

        public static IEnumerable<object[]> Cases => CheckRunner.Cases(Group, Body);

        [Theory]
        [MemberData(nameof(Cases))]
        public void Check(int index, string defect, string name)
        {
            CheckRunner.Verify(Group, Body, index, defect, name);
        }
    }
}
