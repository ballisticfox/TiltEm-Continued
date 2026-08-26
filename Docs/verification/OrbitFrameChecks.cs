using System;
using UnityEngine;

namespace TiltEm.Verification
{
    /// <summary>
    /// The parent-relative orbit frame (I1).
    ///
    /// KSP measures every orbit against the celestial equator. `relativeToParent` lets a config
    /// write the elements against the parent's equator instead, which is what a modder means by
    /// "put this moon in my planet's plane". The conversion is one frame composition,
    /// celestial = T * OrbitalFrame(local), and the whole risk is in decomposing the product back
    /// into elements - so that is what these check, against the frame itself rather than against
    /// a second implementation of the same algebra.
    /// </summary>
    public static class OrbitFrameChecks
    {
        private static BodyTilt[] Parents()
        {
            return new[]
            {
                TiltEmFrames.Untilted,
                TiltEmFrames.FromPole(317.681070, 52.886356),   // Mars
                TiltEmFrames.FromPole(272.760089, 67.159957),   // Venus
                TiltEmFrames.FromPole(257.311000, -15.175000),  // Uranus, pole 98 deg over
                TiltEmFrames.FromLegacyEuler(new Vector3d(20, 0, 5)),
            };
        }

        /// <summary>A spread of orbits, including the equatorial and polar edge cases.</summary>
        private static TiltEmFrames.OrbitElements[] Orbits()
        {
            var incs = new[] { 0.0, 0.001, 15.0, 63.4, 90.0, 116.6, 179.999, 180.0 };
            var lans = new[] { 0.0, 47.5, 190.0, 312.25 };
            var args = new[] { 0.0, 88.0, 201.75, 359.0 };

            var all = new TiltEmFrames.OrbitElements[incs.Length * lans.Length * args.Length];
            var i = 0;

            foreach (var inc in incs)
            foreach (var lan in lans)
            foreach (var arg in args)
            {
                all[i++] = new TiltEmFrames.OrbitElements(inc, lan, arg);
            }

            return all;
        }

        public static void Run()
        {
            TheConvertedElementsRebuildTheComposedFrame();
            AnUntiltedParentChangesNothing();
            ZeroInclinationLandsInTheParentsEquator();
            InclinationIsMeasuredFromTheParentsEquator();
            DecompositionIsTheInverseOfConstruction();
            ItActuallyMovesTheOrbit();
            AZeroTiltMatchesTheParentsPole();
            TiltIsMeasuredFromTheParentsEquator();
            AnUntiltedParentChangesNothingForTilts();
            TheGasGiantCase();
        }

        /// <summary>
        /// I2: the headline behaviour. tiltRelativeToParent with no tilt alongside it puts the
        /// body's pole exactly on its PARENT's pole - a moon that shares its gas giant's axis.
        /// </summary>
        private static void AZeroTiltMatchesTheParentsPole()
        {
            var worst = 0.0;

            foreach (var parent in Parents())
            {
                var tilt = TiltEmFrames.ToCelestialTilt(parent, TiltEmFrames.Untilted);
                worst = Math.Max(worst, Harness.MaxComponentError(tilt.Tilt.Z, parent.Tilt.Z));
            }

            Harness.CheckWithin("I2", "a zero tilt lands the pole exactly on the parent's",
                worst, 1e-15, "abs");
        }

        /// <summary>
        /// I2: and a nonzero tilt is the lean away from the parent's equator, not from the
        /// celestial one. This is the number a pack actually wants to write for a moon.
        /// </summary>
        private static void TiltIsMeasuredFromTheParentsEquator()
        {
            var worst = 0.0;

            foreach (var parent in Parents())
            foreach (var lean in new[] { 0.0, 0.5, 1.54, 23.44, 90.0, 177.36 })
            foreach (var azimuth in new[] { 0.0, 130.0, 300.0 })
            {
                var local = TiltEmFrames.FromPole(azimuth, 90.0 - lean);
                var tilt = TiltEmFrames.ToCelestialTilt(parent, local);

                var dot = Vector3d.Dot(tilt.Tilt.Z, parent.Tilt.Z);
                var measured = Math.Acos(dot < -1.0 ? -1.0 : (dot > 1.0 ? 1.0 : dot)) * (180.0 / Math.PI);

                worst = Math.Max(worst, Math.Abs(measured - lean));
            }

            // Loose because acos is ill-conditioned at 0 and 180, both of which are in the sweep.
            Harness.CheckWithin("I2", "the configured tilt is the lean from the parent's equator",
                worst, 1e-6, "deg");
        }

        /// <summary>
        /// I2: an untilted parent makes the flag an exact no-op, so a pack can set it on every
        /// moon rather than only the ones under a tilted planet.
        /// </summary>
        private static void AnUntiltedParentChangesNothingForTilts()
        {
            var worst = 0.0;

            foreach (var lean in new[] { 0.0, 0.5, 23.44, 90.0 })
            foreach (var azimuth in new[] { 0.0, 130.0, 300.0 })
            {
                var local = TiltEmFrames.FromPole(azimuth, 90.0 - lean);
                var tilt = TiltEmFrames.ToCelestialTilt(TiltEmFrames.Untilted, local);

                worst = Math.Max(worst, Harness.MaxComponentError(tilt.Tilt.Z, local.Tilt.Z));
            }

            Harness.CheckWithin("I2", "an untilted parent leaves the pole exactly where it was",
                worst, 1e-15, "abs");
        }

        /// <summary>
        /// I2: the worked case the feature was asked for - a gas giant at 23 degrees with moons
        /// half a degree off its equator. The celestial obliquity has to land within half a degree
        /// of the giant's, whichever way the moon's half-degree is pointed.
        /// </summary>
        private static void TheGasGiantCase()
        {
            var giant = TiltEmFrames.FromPole(0.0, 90.0 - 23.0);
            var lowest = double.MaxValue;
            var highest = double.MinValue;

            foreach (var azimuth in new[] { 0.0, 45.0, 90.0, 180.0, 270.0, 315.0 })
            {
                var moon = TiltEmFrames.ToCelestialTilt(giant, TiltEmFrames.FromPole(azimuth, 89.5));

                lowest = Math.Min(lowest, moon.Obliquity);
                highest = Math.Max(highest, moon.Obliquity);
            }

            Harness.Check("I2", "a 0.5 deg moon under a 23 deg giant stays within 0.5 deg of it",
                lowest > 22.4999 && highest < 23.5001,
                "celestial obliquity spans " + Harness.Fmt(lowest) + " to " + Harness.Fmt(highest)
                + " deg as the moon's half-degree is swung around the giant's pole");
        }

        /// <summary>
        /// I1, the core property. Whatever elements come out must describe the same plane and the
        /// same periapsis direction as T * OrbitalFrame(local) - including in the equatorial and
        /// polar cases, where LAN and argPe stop being separately determined and only their sum
        /// survives. Comparing frames rather than elements is what makes those cases checkable.
        /// </summary>
        private static void TheConvertedElementsRebuildTheComposedFrame()
        {
            var worst = 0.0;
            var worstOrtho = 0.0;
            var samples = 0;

            foreach (var parent in Parents())
            foreach (var local in Orbits())
            {
                var expected = TiltEmFrames.Multiply(parent.Tilt, TiltEmFrames.OrbitalFrame(local));
                var actual = TiltEmFrames.OrbitalFrame(TiltEmFrames.ToCelestialElements(parent, local));

                worst = Math.Max(worst, Harness.FrameRotationAngle(expected, actual));
                worstOrtho = Math.Max(worstOrtho, Harness.OrthonormalityError(actual));
                samples++;
            }

            Harness.Check("I1", "the orbit sweep actually ran", samples == 5 * 128, samples + " combinations");
            Harness.CheckWithin("I1", "converted elements rebuild the composed frame", worst, 1e-9, "deg");
            Harness.CheckWithin("I1", "and the rebuilt frame stays orthonormal", worstOrtho, 1e-14, "abs");
        }

        /// <summary>
        /// I1: with an untilted parent the flag must be a no-op, and a bit-identical one - not a
        /// round trip through a decomposition that happens to land close. That is what makes it
        /// safe for a config to set relativeToParent on every body regardless of the parent.
        /// </summary>
        private static void AnUntiltedParentChangesNothing()
        {
            var worst = 0.0;

            foreach (var local in Orbits())
            {
                var result = TiltEmFrames.ToCelestialElements(TiltEmFrames.Untilted, local);

                worst = Math.Max(worst, Math.Abs(result.Inclination - local.Inclination));
                worst = Math.Max(worst, Math.Abs(result.LongitudeOfAscendingNode - local.LongitudeOfAscendingNode));
                worst = Math.Max(worst, Math.Abs(result.ArgumentOfPeriapsis - local.ArgumentOfPeriapsis));
            }

            Harness.Check("I1", "an untilted parent leaves the elements bit-identical", worst == 0.0,
                "worst difference " + Harness.Fmt(worst) + " deg");
        }

        /// <summary>
        /// I1: the headline behaviour, and the thing the feature was actually asked for. An
        /// inclination of zero has to put the orbit in the parent's equatorial plane, which means
        /// the orbit normal is the parent's pole - whatever the pole is doing.
        /// </summary>
        private static void ZeroInclinationLandsInTheParentsEquator()
        {
            var worst = 0.0;

            foreach (var parent in Parents())
            foreach (var lan in new[] { 0.0, 47.5, 190.0, 312.25 })
            foreach (var arg in new[] { 0.0, 88.0, 201.75 })
            {
                var local = new TiltEmFrames.OrbitElements(0.0, lan, arg);

                var frame = TiltEmFrames.OrbitalFrame(TiltEmFrames.ToCelestialElements(parent, local));

                // The orbit normal is the frame's Z; the parent's pole is the tilt's Z.
                worst = Math.Max(worst, Harness.MaxComponentError(frame.Z, parent.Tilt.Z));
            }

            Harness.CheckWithin("I1", "inclination 0 puts the orbit in the parent's equatorial plane",
                worst, 1e-14, "abs");
        }

        /// <summary>
        /// I1: and a nonzero inclination is measured from that plane, not from the celestial
        /// equator. The angle between the orbit normal and the parent's pole must come back as
        /// the inclination that was written.
        /// </summary>
        private static void InclinationIsMeasuredFromTheParentsEquator()
        {
            var worst = 0.0;

            foreach (var parent in Parents())
            foreach (var inc in new[] { 0.0, 15.0, 63.4, 90.0, 116.6, 180.0 })
            foreach (var lan in new[] { 0.0, 190.0 })
            {
                var local = new TiltEmFrames.OrbitElements(inc, lan, 88.0);

                var frame = TiltEmFrames.OrbitalFrame(TiltEmFrames.ToCelestialElements(parent, local));

                var dot = Vector3d.Dot(frame.Z, parent.Tilt.Z);
                var measured = Math.Acos(dot < -1.0 ? -1.0 : (dot > 1.0 ? 1.0 : dot)) * (180.0 / Math.PI);

                worst = Math.Max(worst, Math.Abs(measured - inc));
            }

            // Loose next to the others because acos is ill-conditioned at 0 and 180, which are
            // both in the sweep on purpose - a tighter bound here would only measure that.
            Harness.CheckWithin("I1", "inclination is preserved against the parent's pole", worst, 1e-6, "deg");
        }

        /// <summary>
        /// I1: the decomposition on its own, fed frames built directly rather than composed, so a
        /// failure points at the element recovery rather than at the composition.
        /// </summary>
        private static void DecompositionIsTheInverseOfConstruction()
        {
            var worst = 0.0;

            foreach (var local in Orbits())
            {
                var rebuilt = TiltEmFrames.OrbitalFrame(
                    TiltEmFrames.DecomposeOrbitalFrame(TiltEmFrames.OrbitalFrame(local)));

                worst = Math.Max(worst, Harness.FrameRotationAngle(TiltEmFrames.OrbitalFrame(local), rebuilt));
            }

            Harness.CheckWithin("I1", "decomposition inverts construction", worst, 1e-9, "deg");
        }

        /// <summary>
        /// Proof the flag does something. If a tilted parent left the elements alone this would be
        /// a very well-tested no-op.
        /// </summary>
        private static void ItActuallyMovesTheOrbit()
        {
            var mars = TiltEmFrames.FromPole(317.681070, 52.886356);

            var local = new TiltEmFrames.OrbitElements(0.0, 0.0, 0.0);

            var converted = TiltEmFrames.ToCelestialElements(mars, local);

            Harness.Check("I1", "a tilted parent really does move the orbit",
                Math.Abs(converted.Inclination - mars.Obliquity) < 1e-9,
                "an equatorial orbit about Mars comes out at inclination "
                + Harness.Fmt(converted.Inclination) + " deg in the celestial frame, which is Mars's "
                + "obliquity - written by hand, that is the number a modder would have had to find");
        }
    }
}
