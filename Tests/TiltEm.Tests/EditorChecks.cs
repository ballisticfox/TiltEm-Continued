using System;
using System.Globalization;
using System.Threading;
using UnityEngine;

namespace TiltEm.Verification
{
    /// <summary>
    /// The body editor's two pieces of arithmetic: converting a pole back into the legacy
    /// tiltx/tiltz pair the Tilt mode handles drive, and writing an edit out as a config that
    /// reproduces it.
    ///
    /// Both are exact-inverse problems, and both are easy to get subtly wrong in a way no
    /// screenshot would show: the pole would come back right and the body would sit spun a few
    /// degrees off from where the editor left it.
    /// </summary>
    public static class EditorChecks
    {
        /// <summary>The legacy pairs the shipped config carries, which have to survive a round trip.</summary>
        private static Vector3d[] ShippedTilts()
        {
            return new[]
            {
                new Vector3d(7.57, 0, 2.12),    // Sun
                new Vector3d(20, 0, 5),         // Kerbin
                new Vector3d(15.45, 0, 10.61),  // Mun
                new Vector3d(5.87, 0, 12.63),   // Minmus
                new Vector3d(15.14, 0, 30.25),  // Moho
                new Vector3d(120.4, 0, 35.82),  // Eve
                new Vector3d(5.93, 0, 3.81),    // Duna
                new Vector3d(0.54, 0, 1.16),    // Jool
                new Vector3d(80.63, 0, 12.34),  // Eeloo
            };
        }

        /// <summary>Poles from every quadrant, plus the two the conversion is degenerate at.</summary>
        private static BodyTilt[] Poles()
        {
            return new[]
            {
                TiltEmFrames.Untilted,
                TiltEmFrames.FromPole(0.0, 89.999),
                TiltEmFrames.FromPole(45.0, 66.56),
                TiltEmFrames.FromPole(190.0, 12.0),
                TiltEmFrames.FromPole(317.681070, 52.886356),   // Mars
                TiltEmFrames.FromPole(257.311000, -15.175000),  // Uranus, pole 98 deg over
                TiltEmFrames.FromPole(0.0, 0.0),                // on the celestial X axis
                TiltEmFrames.FromPole(180.0, 0.0),              // and the far side of it
                TiltEmFrames.FromPole(90.0, 0.0),
                TiltEmFrames.FromPole(0.0, -90.0),
            };
        }

        public static void Run()
        {
            TheShippedTiltsSurviveTheRoundTrip();
            EveryPoleSurvivesTheRoundTrip();
            AnUntiltedBodyHasNoLegacyTilt();
            TheLegacyPairNeverLeavesItsBranch();
            TheExportedPoleIsTheEditedPole();
            TheSpinOffsetMakesTwoFormsAgree();
            TheExportReloadsToTheBodyTheEditorHad(false);
            TheExportReloadsToTheBodyTheEditorHad(true);
            TheLegacyExportClearsTheFormItLosesTo();
            TheOrbitBlockUsesKopernicusNames();
            TheOrbitBlockDeclaresItsFrame();
            TheFileIsNamedForTheBodyAndTheTime();
            NumbersAreWrittenInvariantly();
            APoleStopsAtTheTopRatherThanTippingOver();
            TippingOverTheTopWouldTurnTheBodyHalfWayRound();
            APoleHeldAtTheTopKeepsTheBodyStill();
            DroppingTheRightAscensionAtTheTopWouldStepTheBody();
            ANormalisedPoleIsOnItsPrincipalRange();
            AParentRelativePoleRoundTrips();
            TheParentRelativeFlagIsWrittenOnlyWhenItIsSet();
            EveryTiltHandleTurnsItsNumberDegreeForDegree();
            TheSpinHandleTurnsTheBodyAboutItsOwnPole();
            EveryOrbitHandleTurnsItsNumberDegreeForDegree();
        }

        /// <summary>
        /// These are the one set of legacy numbers that certainly exist in the wild - Tilt'Em
        /// ships them as its own configuration - and Tilt mode shows every one of them back to
        /// the player. Anything but an exact round trip means opening the editor renumbers a body
        /// nobody touched.
        /// </summary>
        private static void TheShippedTiltsSurviveTheRoundTrip()
        {
            double worst = 0.0;

            foreach (Vector3d legacy in ShippedTilts())
            {
                Vector3d back = TiltEmFrames.ToLegacyEuler(TiltEmFrames.FromLegacyEuler(legacy));

                worst = Math.Max(worst, Math.Abs(back.x - legacy.x));
                worst = Math.Max(worst, Math.Abs(back.z - legacy.z));
                worst = Math.Max(worst, Math.Abs(back.y));
            }

            Harness.CheckWithin("-", "every shipped legacy tilt comes back unchanged",
                worst, 1e-12, "deg");
        }

        /// <summary>
        /// The other direction, which is the one the editor actually relies on: whatever the pole,
        /// the pair Tilt mode shows has to put it back exactly there.
        /// </summary>
        private static void EveryPoleSurvivesTheRoundTrip()
        {
            double worst = 0.0;

            foreach (BodyTilt tilt in Poles())
            {
                Vector3d legacy = TiltEmFrames.ToLegacyEuler(tilt);
                BodyTilt back = TiltEmFrames.FromLegacyEuler(legacy);

                worst = Math.Max(worst, Harness.MaxComponentError(back.Tilt.Z, tilt.Tilt.Z));
            }

            Harness.CheckWithin("-", "the legacy pair reproduces the pole it was read from",
                worst, 1e-15, "abs");
        }

        /// <summary>
        /// An upright body reads as zero in both modes. A stray right ascension here would show
        /// up as a body that leans the moment the editor is opened on it.
        /// </summary>
        private static void AnUntiltedBodyHasNoLegacyTilt()
        {
            Vector3d legacy = TiltEmFrames.ToLegacyEuler(TiltEmFrames.Untilted);

            Harness.CheckWithin("-", "an untilted body reads as a zero legacy pair",
                Math.Max(Math.Abs(legacy.x), Math.Max(Math.Abs(legacy.y), Math.Abs(legacy.z))),
                1e-15, "deg");
        }

        /// <summary>
        /// tiltz is taken on the branch where its cosine is non-negative. The other branch
        /// describes the same pole with tiltx turned 180 degrees, and a handle that could jump
        /// between the two would flip its own sign mid-drag.
        /// </summary>
        private static void TheLegacyPairNeverLeavesItsBranch()
        {
            double worst = 0.0;

            foreach (BodyTilt tilt in Poles())
            {
                worst = Math.Max(worst, Math.Abs(TiltEmFrames.ToLegacyEuler(tilt).z));
            }

            Harness.Check("-", "tiltz stays on one branch", worst <= 90.0 + 1e-12,
                "worst |tiltz| " + Harness.Fmt(worst) + " deg");
        }

        /// <summary>Pole mode writes the pole it was given, under the keys TiltConfig reads.</summary>
        private static void TheExportedPoleIsTheEditedPole()
        {
            BodyExport body = PoleExport();
            string text = EditExport.Build(body, new DateTime(2026, 8, 26, 21, 45, 12, DateTimeKind.Utc));

            Harness.CheckWithin("-", "the exported poleRA is the edited one",
                Math.Abs(ValueOf(text, "poleRA") - body.PoleRa), 1e-6, "deg");
            Harness.CheckWithin("-", "the exported poleDec is the edited one",
                Math.Abs(ValueOf(text, "poleDec") - body.PoleDec), 1e-6, "deg");
            Harness.Check("-", "the export names the body", text.Contains("@Body[Kerbin]"), null);
        }

        /// <summary>
        /// Rewriting a tilt in another form is only lossless if the spin between the two forms
        /// comes off the rotation angle. This is that spin, checked against the frames themselves
        /// rather than against a second derivation of it.
        /// </summary>
        private static void TheSpinOffsetMakesTwoFormsAgree()
        {
            double worst = 0.0;

            foreach (BodyTilt tilt in Poles())
            foreach (double rotation in new[] { 0.0, 37.5, 190.0, 359.9 })
            {
                BodyTilt rewritten = TiltEmFrames.FromLegacyEuler(TiltEmFrames.ToLegacyEuler(tilt));

                Planetarium.CelestialFrame original = default;
                Planetarium.CelestialFrame replacement = default;

                TiltEmFrames.LocalBodyFrame(tilt, rotation, ref original);
                TiltEmFrames.LocalBodyFrame(rewritten,
                    rotation + TiltEmFrames.SpinOffset(tilt, rewritten), ref replacement);

                worst = Math.Max(worst, Harness.FrameRotationAngle(original, replacement));
            }

            Harness.CheckWithin("-", "the spin offset puts a rewritten tilt back on the original",
                worst, 1e-12, "deg");
        }

        /// <summary>
        /// The one that is genuinely easy to get wrong, in either form. The editor keeps no prime
        /// meridian: it folds whatever a pole implied about the spin into initialRotation, so
        /// there is one number for it. Both written forms put an implied spin back in front of
        /// that number - the legacy pair carries its own, and poleRA is dropped outright at the
        /// pole - so the difference has to come off or the body reloads turned.
        /// </summary>
        //Checked as frames rather than as angles. The two forms can describe the same body
        //through different poles, in which case no two of their numbers are comparable and only
        //the orientation they produce is.
        private static void TheExportReloadsToTheBodyTheEditorHad(bool legacyForm)
        {
            double worst = 0.0;

            foreach (double[] pole in PoleNumbers())
            foreach (double spin in new[] { 0.0, 37.5, 190.0, 359.9 })
            {
                BodyExport body = PoleExport();
                body.LegacyTiltForm = legacyForm;
                body.PoleRa = pole[0];
                body.PoleDec = pole[1];
                body.InitialRotation = spin;

                string text = EditExport.Build(body, DateTime.UtcNow);

                //Read back out of the file, so the six decimals it rounds to are part of the test,
                //and rebuilt the way TiltConfig rebuilds it.
                BodyTilt reloaded = legacyForm
                    ? TiltEmFrames.FromLegacyEuler(
                        new Vector3d(ValueOf(text, "tiltx"), 0.0, ValueOf(text, "tiltz")))
                    : TiltEmFrames.FromPole(ValueOf(text, "poleRA"), ValueOf(text, "poleDec"));

                Planetarium.CelestialFrame edited = default;
                Planetarium.CelestialFrame loaded = default;

                TiltEmFrames.LocalBodyFrame(TiltEmFrames.FromPoleContinuous(pole[0], pole[1]), spin, ref edited);
                TiltEmFrames.LocalBodyFrame(reloaded, ValueOf(text, "initialRotation"), ref loaded);

                worst = Math.Max(worst, Harness.FrameRotationAngle(edited, loaded));
            }

            //Not tighter: the file rounds every angle to six decimals, and that rounding is
            //deliberately inside the test rather than in front of it.
            Harness.CheckWithin("-", (legacyForm ? "a legacy" : "a pole") + " export reloads to the body the editor had",
                worst, 1e-5, "deg");
        }

        /// <summary>
        /// TiltConfig prefers poleRA/poleDec wherever a body has both forms, so a legacy export
        /// onto a body that already has a pole would otherwise be read and thrown away.
        /// </summary>
        private static void TheLegacyExportClearsTheFormItLosesTo()
        {
            BodyExport body = PoleExport();
            body.LegacyTiltForm = true;

            string text = EditExport.Build(body, DateTime.UtcNow);

            Harness.Check("-", "a legacy export deletes any poleRA it would lose to",
                text.Contains("!poleRA = delete") && text.Contains("!poleDec = delete"), null);
            Harness.Check("-", "and does not write the form it just deleted",
                !text.Contains("%poleRA") && !text.Contains("%poleDec"), null);
        }

        /// <summary>
        /// Kopernicus spells the ascending node out in full. KSP's own field is LAN, which is what
        /// the editor reads it from, and writing that name would parse as nothing at all.
        /// </summary>
        private static void TheOrbitBlockUsesKopernicusNames()
        {
            string text = EditExport.Build(OrbitExport(), DateTime.UtcNow);

            Harness.Check("-", "the ascending node is written under Kopernicus's name",
                text.Contains("%longitudeOfAscendingNode"), null);
            Harness.Check("-", "the mean anomaly is written in the degree form",
                text.Contains("%meanAnomalyAtEpochD"), null);
            Harness.Check("-", "every orbit element is written",
                text.Contains("%inclination") && text.Contains("%argumentOfPeriapsis")
                && text.Contains("%eccentricity") && text.Contains("%semiMajorAxis"), null);
        }

        /// <summary>
        /// Elements dragged against a tilted parent's equator only mean that with the flag that
        /// says so. Without it Kopernicus reads them from the celestial equator and the body ends
        /// up in a different plane from the one it was left in.
        /// </summary>
        private static void TheOrbitBlockDeclaresItsFrame()
        {
            BodyExport celestial = OrbitExport();
            BodyExport local = OrbitExport();
            local.OrbitRelativeToParent = true;

            Harness.Check("-", "parent-relative elements say so",
                EditExport.Build(local, DateTime.UtcNow).Contains("%relativeToParent = true"), null);
            Harness.Check("-", "celestial elements do not claim to be parent-relative",
                !EditExport.Build(celestial, DateTime.UtcNow).Contains("relativeToParent"), null);
        }

        private static void TheFileIsNamedForTheBodyAndTheTime()
        {
            DateTime stamp = new DateTime(2026, 8, 26, 21, 45, 12, DateTimeKind.Utc);

            Harness.Check("-", "the file is named Body_TimeStamp.cfg",
                EditExport.FileName("Kerbin", stamp) == "Kerbin_20260826-214512.cfg", null);

            // Planet packs name bodies whatever they like, and a slash in one would send the write
            // somewhere else entirely.
            Harness.Check("-", "a body name that cannot be a file name is replaced",
                EditExport.FileName("Kerbin/A B", stamp) == "Kerbin_A_B_20260826-214512.cfg", null);
        }

        /// <summary>
        /// Config files are read with a decimal point whatever the machine's locale. Written with
        /// the current culture, an export on a comma-decimal machine parses as a different number
        /// or as nothing.
        /// </summary>
        private static void NumbersAreWrittenInvariantly()
        {
            CultureInfo original = Thread.CurrentThread.CurrentCulture;

            try
            {
                Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE");

                string text = EditExport.Build(OrbitExport(), DateTime.UtcNow);

                Harness.Check("-", "numbers are written with a decimal point, not the locale's",
                    text.Contains("%inclination = 12.500000"), null);
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = original;
            }
        }

        /// <summary>
        /// A handle claims that turning the body about its axis by so many degrees raises its
        /// number by the same amount. Every drag depends on it: an axis that is merely close
        /// would make a handle drag at the wrong rate, or drift sideways into the numbers the
        /// other handles own.
        /// </summary>
        private static void EveryTiltHandleTurnsItsNumberDegreeForDegree()
        {
            double worst = 0.0;

            foreach (BodyTilt tilt in Poles())
            foreach (double turn in new[] { 5.0, -12.0, 90.0 })
            {
                Vector3d legacy = TiltEmFrames.ToLegacyEuler(tilt);

                worst = Math.Max(worst, PoleError(tilt, EditHandle.PoleRa, turn,
                    TiltEmFrames.FromPole(tilt.PoleRa + turn, tilt.PoleDec)));

                //Skipped where the perturbed declination would clamp: a handle cannot drive a
                //number past the end of its own range, and the screen stops it there too.
                if (Math.Abs(tilt.PoleDec + turn) <= 90.0)
                {
                    worst = Math.Max(worst, PoleError(tilt, EditHandle.PoleDec, turn,
                        TiltEmFrames.FromPole(tilt.PoleRa, tilt.PoleDec + turn)));
                }

                worst = Math.Max(worst, PoleError(tilt, EditHandle.TiltX, turn,
                    TiltEmFrames.FromLegacyEuler(new Vector3d(legacy.x + turn, 0.0, legacy.z))));

                worst = Math.Max(worst, PoleError(tilt, EditHandle.TiltZ, turn,
                    TiltEmFrames.FromLegacyEuler(new Vector3d(legacy.x, 0.0, legacy.z + turn))));
            }

            Harness.CheckWithin("-", "every tilt handle moves the pole exactly as its number does",
                worst, 1e-9, "abs");
        }

        /// <summary>
        /// The spin handle is the one that leaves the pole alone, so it has to be checked on the
        /// whole body frame rather than on where the axis points.
        /// </summary>
        private static void TheSpinHandleTurnsTheBodyAboutItsOwnPole()
        {
            double worst = 0.0;

            foreach (BodyTilt tilt in Poles())
            foreach (double rotation in new[] { 0.0, 143.0 })
            foreach (double turn in new[] { 5.0, -12.0, 90.0 })
            {
                Planetarium.CelestialFrame before = default;
                Planetarium.CelestialFrame after = default;

                TiltEmFrames.LocalBodyFrame(tilt, rotation, ref before);
                TiltEmFrames.LocalBodyFrame(tilt, rotation + turn, ref after);

                QuaternionD turned = QuaternionD.AngleAxis(turn, Axis(EditHandle.Spin, tilt));

                //FrameRotationAngle, not AngleBetweenFrames: the latter reads the angle off a
                //trace through acos, which loses half its digits near zero and bottoms out around
                //a microdegree. These frames should be identical, so that is the whole question.
                worst = Math.Max(worst, Harness.FrameRotationAngle(Rotate(turned, before), after));
            }

            Harness.CheckWithin("-", "the spin handle turns the body about its own pole",
                worst, 1e-12, "deg");
        }

        /// <summary>The same claim for the three orbit handles, against the orbital frame.</summary>
        private static void EveryOrbitHandleTurnsItsNumberDegreeForDegree()
        {
            double worst = 0.0;

            foreach (TiltEmFrames.OrbitElements elements in Orbits())
            foreach (double turn in new[] { 5.0, -12.0, 90.0 })
            {
                worst = Math.Max(worst, OrbitError(elements, EditHandle.LongitudeOfAscendingNode, turn,
                    new TiltEmFrames.OrbitElements(elements.Inclination,
                        elements.LongitudeOfAscendingNode + turn, elements.ArgumentOfPeriapsis)));

                worst = Math.Max(worst, OrbitError(elements, EditHandle.Inclination, turn,
                    new TiltEmFrames.OrbitElements(elements.Inclination + turn,
                        elements.LongitudeOfAscendingNode, elements.ArgumentOfPeriapsis)));

                worst = Math.Max(worst, OrbitError(elements, EditHandle.ArgumentOfPeriapsis, turn,
                    new TiltEmFrames.OrbitElements(elements.Inclination,
                        elements.LongitudeOfAscendingNode, elements.ArgumentOfPeriapsis + turn)));
            }

            Harness.CheckWithin("-", "every orbit handle moves the orbit exactly as its number does",
                worst, 1e-12, "deg");
        }

        /// <summary>A spread of orbits, including the equatorial case that has no node.</summary>
        private static TiltEmFrames.OrbitElements[] Orbits()
        {
            return new[]
            {
                new TiltEmFrames.OrbitElements(0.0, 0.0, 0.0),
                new TiltEmFrames.OrbitElements(15.0, 47.5, 88.0),
                new TiltEmFrames.OrbitElements(90.0, 190.0, 201.75),
                new TiltEmFrames.OrbitElements(179.0, 312.25, 359.0),
            };
        }

        /// <summary>How far the handle's turn lands the pole from where its number would put it.</summary>
        private static double PoleError(BodyTilt tilt, EditHandle handle, double turn, BodyTilt expected)
        {
            QuaternionD turned = QuaternionD.AngleAxis(turn, Axis(handle, tilt));

            return Harness.MaxComponentError(turned * tilt.Tilt.Z, expected.Tilt.Z);
        }

        /// <summary>The handle's axis, fed the numbers the editor would be holding for this tilt.</summary>
        private static Vector3d Axis(EditHandle handle, BodyTilt tilt)
        {
            return HandleAxes.Tilt(handle, tilt.PoleRa, TiltEmFrames.ToLegacyEuler(tilt).x, tilt.Tilt.Z);
        }

        private static double OrbitError(TiltEmFrames.OrbitElements elements, EditHandle handle,
            double turn, TiltEmFrames.OrbitElements expected)
        {
            QuaternionD turned = QuaternionD.AngleAxis(turn, HandleAxes.Orbit(handle, elements));

            return Harness.FrameRotationAngle(Rotate(turned, TiltEmFrames.OrbitalFrame(elements)),
                TiltEmFrames.OrbitalFrame(expected));
        }

        /// <summary>The frame, turned bodily by a rotation.</summary>
        private static Planetarium.CelestialFrame Rotate(QuaternionD rotation, Planetarium.CelestialFrame frame)
        {
            Planetarium.CelestialFrame turned;

            turned.X = rotation * frame.X;
            turned.Y = rotation * frame.Y;
            turned.Z = rotation * frame.Z;

            return turned;
        }

        /// <summary>
        /// A declination handle stops at the pole, and keeps its right ascension while it sits
        /// there. Snapping that to zero would swing the handle's own plane out from under the
        /// pointer, and there would be no meridian to come back down.
        /// </summary>
        private static void APoleStopsAtTheTopRatherThanTippingOver()
        {
            bool stopped = true;
            double worstRa = 0.0;

            foreach (double ra in new[] { 0.0, 47.5, 190.0, 312.25 })
            foreach (double dec in new[] { -270.0, -140.0, -91.0, -90.0, 90.0, 91.0, 170.0, 265.0 })
            {
                TiltEmFrames.NormalizePole(ra, dec, out double clampedRa, out double clampedDec);

                stopped &= clampedDec >= -90.0 && clampedDec <= 90.0;
                worstRa = Math.Max(worstRa, AngleError(clampedRa, ra));
            }

            Harness.Check("-", "a declination handle stops at the pole", stopped, null);
            Harness.CheckWithin("-", "and keeps the right ascension it arrived on",
                worstRa, 1e-12, "deg");
        }

        /// <summary>
        /// The control, and the reason for the clamp. Past the pole, (ra, 90 + e) and
        /// (ra + 180, 90 - e) name the same pole, so carrying a handle over the top looks
        /// continuous. The body they describe is half a turn apart.
        /// </summary>
        private static void TippingOverTheTopWouldTurnTheBodyHalfWayRound()
        {
            double worstPole = 0.0;
            double worstTurn = 0.0;

            foreach (double ra in new[] { 0.0, 47.5, 190.0, 312.25 })
            foreach (double over in new[] { 0.5, 5.0, 20.0 })
            foreach (double rotation in new[] { 0.0, 143.0 })
            {
                Planetarium.CelestialFrame straight = default;
                Planetarium.CelestialFrame wrapped = default;

                //Built through PlanetaryFrame rather than FromPole, which does the clamping this
                //is here to justify.
                Planetarium.CelestialFrame.PlanetaryFrame(ra, 90.0 + over, rotation, ref straight);
                Planetarium.CelestialFrame.PlanetaryFrame(ra + 180.0, 90.0 - over, rotation, ref wrapped);

                worstPole = Math.Max(worstPole, Harness.MaxComponentError(straight.Z, wrapped.Z));
                worstTurn = Math.Max(worstTurn,
                    Math.Abs(Harness.AngleBetweenFrames(straight, wrapped) - 180.0));
            }

            Harness.CheckWithin("-", "tipping over the top names the same pole",
                worstPole, 1e-12, "abs");
            Harness.CheckWithin("-", "but turns the body half way round, which is why it is clamped",
                worstTurn, 1e-4, "deg");
        }

        private static void ANormalisedPoleIsOnItsPrincipalRange()
        {
            bool ok = true;

            foreach (double ra in new[] { -400.0, 0.0, 190.0, 700.0 })
            foreach (double dec in new[] { -540.0, -91.0, 0.0, 91.0, 450.0 })
            {
                TiltEmFrames.NormalizePole(ra, dec, out double wrappedRa, out double wrappedDec);

                ok &= wrappedDec >= -90.0 && wrappedDec <= 90.0 && wrappedRa >= 0.0 && wrappedRa < 360.0;
            }

            Harness.Check("-", "a wrapped pole lands on the ranges everything else expects", ok, null);
        }

        /// <summary>
        /// The tilt editor writes a pole against the parent's equator and the mod stores one
        /// against the celestial equator, so the two conversions have to be exact inverses or
        /// ticking the box would lean the body.
        /// </summary>
        private static void AParentRelativePoleRoundTrips()
        {
            double worst = 0.0;

            foreach (BodyTilt parent in Poles())
            foreach (BodyTilt local in Poles())
            {
                BodyTilt back = TiltEmFrames.ToLocalTilt(parent, TiltEmFrames.ToCelestialTilt(parent, local));

                worst = Math.Max(worst, Harness.MaxComponentError(back.Tilt.Z, local.Tilt.Z));
            }

            Harness.CheckWithin("-", "a parent-relative pole survives the trip to celestial and back",
                worst, 1e-12, "abs");
        }

        private static void TheParentRelativeFlagIsWrittenOnlyWhenItIsSet()
        {
            BodyExport celestial = PoleExport();
            BodyExport local = PoleExport();
            local.TiltRelativeToParent = true;

            Harness.Check("-", "a parent-relative pole says so",
                EditExport.Build(local, DateTime.UtcNow).Contains("%tiltRelativeToParent = true"), null);
            Harness.Check("-", "a celestial pole does not claim to be parent-relative",
                !EditExport.Build(celestial, DateTime.UtcNow).Contains("tiltRelativeToParent"), null);
        }

        /// <summary>Right ascensions and declinations a handle can actually be holding.</summary>
        //Includes the pole itself with a right ascension still on it, which is where the
        //declination handle comes to rest and where FromPole alone throws that number away.
        private static double[][] PoleNumbers()
        {
            return new[]
            {
                new[] { 0.0, 90.0 },
                new[] { 137.0, 90.0 },
                new[] { 275.25, 90.0 },
                new[] { 47.5, 89.0 },
                new[] { 137.0, 62.0 },
                new[] { 312.25, -12.0 },
                new[] { 190.0, -90.0 },
            };
        }

        /// <summary>
        /// A declination handle sits against the clamp at 90 and jitters across it. The body must
        /// not move when it does, which means the pole at 90 has to be the limit of the poles
        /// just below it rather than a value of its own.
        /// </summary>
        private static void APoleHeldAtTheTopKeepsTheBodyStill()
        {
            double worst = 0.0;

            foreach (double ra in new[] { 0.0, 47.5, 137.0, 275.25 })
            foreach (double rotation in new[] { 0.0, 143.0 })
            {
                Planetarium.CelestialFrame held = default;
                Planetarium.CelestialFrame limit = default;

                TiltEmFrames.LocalBodyFrame(TiltEmFrames.FromPoleContinuous(ra, 90.0), rotation, ref held);

                //What the declination was approaching on its way up, with nothing pinned away.
                Planetarium.CelestialFrame.PlanetaryFrame(ra, 90.0, rotation, ref limit);

                worst = Math.Max(worst, Harness.FrameRotationAngle(held, limit));
            }

            Harness.CheckWithin("-", "a pole held at the top is where the ones below it were heading",
                worst, 1e-12, "deg");
        }

        /// <summary>
        /// The control. FromPole drops the right ascension at the pole, which is right for a
        /// config and a step of that whole angle for a handle: hold one against the clamp and the
        /// body flips back and forth every frame the declination crosses.
        /// </summary>
        private static void DroppingTheRightAscensionAtTheTopWouldStepTheBody()
        {
            double worst = 0.0;

            foreach (double ra in new[] { 47.5, 137.0, 275.25 })
            {
                Planetarium.CelestialFrame dropped = default;
                Planetarium.CelestialFrame limit = default;

                TiltEmFrames.LocalBodyFrame(TiltEmFrames.FromPole(ra, 90.0), 0.0, ref dropped);
                Planetarium.CelestialFrame.PlanetaryFrame(ra, 90.0, 0.0, ref limit);

                double step = Harness.FrameRotationAngle(dropped, limit);

                worst = Math.Max(worst, Math.Abs(step - AngleError(ra, 0.0)));
            }

            Harness.CheckWithin("-", "dropping it instead steps the body by the whole right ascension",
                worst, 1e-9, "deg");
        }

        private static BodyExport PoleExport()
        {
            BodyExport body = default(BodyExport);

            body.BodyName = "Kerbin";
            body.HasTilt = true;
            body.PoleRa = 275.25;
            body.PoleDec = 63.5;
            body.InitialRotation = 114.0;

            return body;
        }

        private static BodyExport OrbitExport()
        {
            BodyExport body = default(BodyExport);

            body.BodyName = "Mun";
            body.HasOrbit = true;
            body.Inclination = 12.5;
            body.LongitudeOfAscendingNode = 45.0;
            body.ArgumentOfPeriapsis = 200.0;
            body.Eccentricity = 0.05;
            body.SemiMajorAxis = 12000000.0;
            body.MeanAnomalyAtEpochD = 100.0;

            return body;
        }

        /// <summary>The value the config gives a key, read back the way KSP would.</summary>
        private static double ValueOf(string text, string key)
        {
            foreach (string line in text.Split('\n'))
            {
                string trimmed = line.Trim();
                int equals = trimmed.IndexOf('=');

                // Only the edit-or-create lines. The legacy form also writes deletes, whose
                // right-hand side is a word rather than a number.
                if (equals < 1 || trimmed[0] != '%') continue;
                if (trimmed.Substring(1, equals - 1).Trim() != key) continue;

                return double.Parse(trimmed.Substring(equals + 1).Trim(), CultureInfo.InvariantCulture);
            }

            throw new InvalidOperationException("the export has no " + key + " key:" + Environment.NewLine + text);
        }

        /// <summary>Difference between two angles, taking the short way round.</summary>
        private static double AngleError(double a, double b)
        {
            double difference = (a - b) % 360.0;

            if (difference < 0.0) difference += 360.0;

            return difference > 180.0 ? 360.0 - difference : difference;
        }
    }
}
