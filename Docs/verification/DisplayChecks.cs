using System;
using UnityEngine;

namespace TiltEm.Verification
{
    /// <summary>
    /// Parent-relative element readouts (J1).
    ///
    /// The maneuver node editor prints an orbit's inclination, LAN and argument of periapsis, and
    /// the debug menu's Set Orbit reads the same three back in. Both work in the celestial frame,
    /// which on a tilted body is not the frame the player is flying in - an equatorial orbit
    /// around a 26-degree planet reports 26 degrees of inclination.
    ///
    /// TiltEmFrames.ToLocalElements moves the readout into the parent's frame and
    /// ToCelestialElements moves the input back out of it. The pair has to be an exact inverse:
    /// the display and the input are two halves of one feature, and if they disagree then a
    /// player who types "inclination 0" is shown something other than 0 the moment they open the
    /// maneuver editor. That round trip is what these check, along with the property the whole
    /// feature exists for - that "in the parent's equatorial plane" reads as zero.
    ///
    /// Elements are compared as frames rather than as three angles. An equatorial or polar orbit
    /// genuinely has more than one valid (LAN, argPe) pair for the same plane, so comparing the
    /// numbers would fail on cases where the geometry is identical.
    /// </summary>
    public static class DisplayChecks
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
                TiltEmFrames.FromLegacyEuler(new Vector3d(120.4, 0, 35.82)),
            };
        }

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
                all[i].Inclination = inc;
                all[i].LongitudeOfAscendingNode = lan;
                all[i].ArgumentOfPeriapsis = arg;
                i++;
            }

            return all;
        }

        public static void Run()
        {
            TheReadoutInvertsTheInput();
            TheInputInvertsTheReadout();
            AnUntiltedParentChangesNothing();
            AnOrbitInTheParentsEquatorReadsAsZero();
            APolarOrbitReadsAsNinety();
            TheReadoutDiffersFromStockByTheObliquity();
            ANearDegenerateFrameKeepsItsPlane();
            APolarFrameReportsAWholeNumber();
        }

        /// <summary>
        /// J1: the headline guarantee. Whatever the player types into Set Orbit is what the
        /// maneuver node editor shows them, because ToLocalElements undoes ToCelestialElements
        /// exactly. Without this the two halves of the feature contradict each other, and the
        /// result is worse than leaving both in the celestial frame.
        /// </summary>
        private static void TheReadoutInvertsTheInput()
        {
            var worst = 0.0;

            foreach (var parent in Parents())
            foreach (var typed in Orbits())
            {
                var stored = TiltEmFrames.ToCelestialElements(parent, typed);
                var shown = TiltEmFrames.ToLocalElements(parent, stored);

                //As frames: an equatorial orbit has many equivalent (LAN, argPe) pairs and the
                //decomposition is entitled to pick a different one than the caller wrote.
                worst = Math.Max(worst, Harness.FrameRotationAngle(
                    TiltEmFrames.OrbitalFrame(typed), TiltEmFrames.OrbitalFrame(shown)));
            }

            Harness.CheckWithin("J1", "what Set Orbit accepts is what the maneuver editor shows",
                worst, 1e-9, "deg");
        }

        /// <summary>
        /// J1: and the other direction, which is the one a live orbit takes - stored celestial
        /// elements shown to the player, then typed back in unchanged.
        /// </summary>
        private static void TheInputInvertsTheReadout()
        {
            var worst = 0.0;

            foreach (var parent in Parents())
            foreach (var stored in Orbits())
            {
                var shown = TiltEmFrames.ToLocalElements(parent, stored);
                var back = TiltEmFrames.ToCelestialElements(parent, shown);

                worst = Math.Max(worst, Harness.FrameRotationAngle(
                    TiltEmFrames.OrbitalFrame(stored), TiltEmFrames.OrbitalFrame(back)));
            }

            Harness.CheckWithin("J1", "re-entering a displayed orbit reproduces it",
                worst, 1e-9, "deg");
        }

        /// <summary>
        /// J1: an untilted parent has to leave the elements bit-identical, not merely close. Both
        /// readout and input run on every orbit in the game, so a stock system must see stock's
        /// own numbers rather than numbers that have been through a decomposition and back.
        /// </summary>
        private static void AnUntiltedParentChangesNothing()
        {
            var worst = 0.0;

            foreach (var stored in Orbits())
            {
                var shown = TiltEmFrames.ToLocalElements(TiltEmFrames.Untilted, stored);

                worst = Math.Max(worst, Math.Abs(shown.Inclination - stored.Inclination));
                worst = Math.Max(worst,
                    Math.Abs(shown.LongitudeOfAscendingNode - stored.LongitudeOfAscendingNode));
                worst = Math.Max(worst,
                    Math.Abs(shown.ArgumentOfPeriapsis - stored.ArgumentOfPeriapsis));
            }

            Harness.CheckWithin("J1", "an untilted parent leaves the readout bit-identical",
                worst, 0.0, "deg");
        }

        /// <summary>
        /// J1: the reason the feature exists. An orbit lying in the parent's equatorial plane
        /// must read as inclination zero however far over the parent's pole is - that is the
        /// question the player is actually asking the field.
        /// </summary>
        private static void AnOrbitInTheParentsEquatorReadsAsZero()
        {
            var worst = 0.0;

            foreach (var parent in Parents())
            {
                //Taken from the parent's own frame rather than from elements, so this is the
                //equatorial plane by construction and not by the same algebra under test.
                var equatorial = TiltEmFrames.DecomposeOrbitalFrame(parent.Tilt);
                var shown = TiltEmFrames.ToLocalElements(parent, equatorial);

                worst = Math.Max(worst, Math.Abs(shown.Inclination));
            }

            Harness.CheckWithin("J1", "an orbit in the parent's equator reads as zero inclination",
                worst, 1e-9, "deg");
        }

        /// <summary>
        /// J1: and the scale is preserved - an orbit over the parent's poles reads as 90, not as
        /// 90 plus or minus the obliquity. Catches a conversion applied in the wrong direction,
        /// which the round trip alone would not: composing twice and undoing twice also cancels.
        /// </summary>
        private static void APolarOrbitReadsAsNinety()
        {
            var worst = 0.0;

            foreach (var parent in Parents())
            {
                //Ninety degrees to the parent's equator, in the parent's own frame.
                TiltEmFrames.OrbitElements polar;
                polar.Inclination = 90.0;
                polar.LongitudeOfAscendingNode = 0.0;
                polar.ArgumentOfPeriapsis = 0.0;

                var stored = TiltEmFrames.ToCelestialElements(parent, polar);
                var shown = TiltEmFrames.ToLocalElements(parent, stored);

                worst = Math.Max(worst, Math.Abs(shown.Inclination - 90.0));
            }

            Harness.CheckWithin("J1", "a polar orbit reads as ninety degrees", worst, 1e-9, "deg");
        }

        /// <summary>
        /// J1: proof the readout is not a no-op dressed up as one. For an orbit stored in the
        /// celestial equator, the parent-relative inclination is exactly the parent's obliquity -
        /// which is the 26-degrees-of-surprise case stated the other way round.
        /// </summary>
        private static void TheReadoutDiffersFromStockByTheObliquity()
        {
            var worst = 0.0;

            foreach (var parent in Parents())
            {
                if (parent.IsIdentity) continue;

                TiltEmFrames.OrbitElements celestialEquator;
                celestialEquator.Inclination = 0.0;
                celestialEquator.LongitudeOfAscendingNode = 0.0;
                celestialEquator.ArgumentOfPeriapsis = 0.0;

                var shown = TiltEmFrames.ToLocalElements(parent, celestialEquator);

                //Obliquity is the angle between the poles, and inclination the angle between the
                //planes those poles are normal to, so the two are the same number.
                worst = Math.Max(worst, Math.Abs(shown.Inclination - parent.Obliquity));
            }

            Harness.CheckWithin("J1", "a celestial-equatorial orbit reads as the parent's obliquity",
                worst, 1e-9, "deg");
        }

        /// <summary>
        /// J1: the failure this feature actually shipped with, kept as a regression.
        ///
        /// An exactly equatorial or exactly polar orbit has no ascending node - only LAN plus
        /// argPe (retrograde: their difference) is determined - so the decomposition has to
        /// detect that and put the whole angle in one place. Detecting it means measuring how
        /// far the orbit normal has tipped out of the celestial +Z axis, and computing that as
        /// sqrt(1 - cos^2) throws the measurement away: the subtraction cancels, and a frame
        /// that arrived as a PRODUCT rather than as literal elements carries enough rounding to
        /// read as a real, if tiny, inclination. The general branch then splits the undetermined
        /// angle using two pure-noise components and lands the orbit in the wrong plane by tens
        /// of degrees.
        ///
        /// It only bites on a product, which is exactly what both frame conversions are, so the
        /// direct decomposition checks in OrbitFrameChecks cannot see it. Here the frame is
        /// deliberately built by composing a tilt with its own inverse - mathematically the
        /// identity, numerically not - to reproduce the conditions that provoked it.
        /// </summary>
        private static void ANearDegenerateFrameKeepsItsPlane()
        {
            var worst = 0.0;

            foreach (var parent in Parents())
            {
                if (parent.IsIdentity) continue;

                //Both degenerate inclinations, and a node angle far from zero so a collapse to
                //LAN = 0 shows up rather than hiding in an already-zero value.
                foreach (var inc in new[] { 0.0, 180.0 })
                foreach (var lan in new[] { 0.0, 47.5, 190.0, 312.25 })
                {
                    TiltEmFrames.OrbitElements degenerate;
                    degenerate.Inclination = inc;
                    degenerate.LongitudeOfAscendingNode = lan;
                    degenerate.ArgumentOfPeriapsis = 0.0;

                    var built = TiltEmFrames.OrbitalFrame(degenerate);

                    //T * transpose(T) * frame. The identity in exact arithmetic; in doubles it
                    //perturbs the orbit normal off the axis by about 1e-16 per component.
                    var product = TiltEmFrames.Multiply(parent.Tilt,
                        TiltEmFrames.Multiply(parent.TiltTranspose, built));

                    var recovered = TiltEmFrames.OrbitalFrame(
                        TiltEmFrames.DecomposeOrbitalFrame(product));

                    worst = Math.Max(worst, Harness.FrameRotationAngle(built, recovered));
                }
            }

            Harness.CheckWithin("J1", "a near-degenerate frame keeps its plane through a decomposition",
                worst, 1e-9, "deg");
        }

        /// <summary>
        /// J1: and the inclination itself survives the same neighbourhood. acos loses half its
        /// significant digits as its argument approaches +/-1, so a polar orbit that has been
        /// through any frame product reads as 179.999999 - visible in a readout that prints one
        /// decimal place, since it rounds to 180.0 only by luck of the format.
        /// </summary>
        private static void APolarFrameReportsAWholeNumber()
        {
            var worst = 0.0;

            foreach (var parent in Parents())
            {
                if (parent.IsIdentity) continue;

                foreach (var inc in new[] { 0.0, 180.0 })
                {
                    TiltEmFrames.OrbitElements degenerate;
                    degenerate.Inclination = inc;
                    degenerate.LongitudeOfAscendingNode = 190.0;
                    degenerate.ArgumentOfPeriapsis = 0.0;

                    var product = TiltEmFrames.Multiply(parent.Tilt,
                        TiltEmFrames.Multiply(parent.TiltTranspose,
                            TiltEmFrames.OrbitalFrame(degenerate)));

                    var recovered = TiltEmFrames.DecomposeOrbitalFrame(product);

                    worst = Math.Max(worst, Math.Abs(recovered.Inclination - inc));
                }
            }

            Harness.CheckWithin("J1", "a degenerate inclination comes back exact", worst, 1e-12, "deg");
        }
    }
}
