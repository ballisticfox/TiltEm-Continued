using System;
using UnityEngine;

namespace TiltEm.Verification
{
    /// <summary>
    /// What happens to the sky when the editor moves a body that holds the rotating frame.
    ///
    /// Zup is defined against an anchor latched when a body took the frame, and the rule for
    /// building that anchor exists to hold the BODY still while the sky takes up the difference.
    /// That is right at a threshold crossing and exactly wrong under a drag handle, where the
    /// body is the thing being moved on purpose. Get it backwards and a tilt handle turns the
    /// entire universe around a planet that never moves.
    /// </summary>
    public static class EditorFrameChecks
    {
        private const double KerbinRotationPeriod = 21549.425;
        private const double MunRotationPeriod = 138984.38;
        private const double Ut = 51234.5;

        private static BodyTilt KerbinTilt => TiltEmFrames.FromLegacyEuler(new Vector3d(20, 0, 5));

        private static BodyTilt DraggedTilt => TiltEmFrames.FromPole(140.0, 62.0);

        public static void Run()
        {
            DraggingATiltMovesTheBodyAndNotTheSky();
            DraggingTheSpinMovesTheBodyAndNotTheSky();
            DroppingTheAnchorInsteadWouldTurnTheSky();
            AnotherBodysTiltNeedsNoRelatch();
        }

        /// <summary>Kerbin holding the rotating frame, settled, as the tracking station finds it.</summary>
        private static Sim Rotating(out SimBody kerbin, out SimBody mun)
        {
            Sim sim = new Sim();

            kerbin = new SimBody("Kerbin", KerbinTilt, KerbinRotationPeriod, 137.0);
            mun = new SimBody("Mun", TiltEmFrames.FromLegacyEuler(new Vector3d(15.45, 0, 10.61)),
                MunRotationPeriod, 41.0);

            sim.Register(kerbin, mun);
            sim.DominantBody = "Kerbin";

            //Two ticks: the first latches the anchor, the second is an ordinary one on top of it.
            sim.Tick(new[] { kerbin, mun }, Ut);
            sim.SetRotatingFrame(kerbin, true);
            sim.Tick(new[] { kerbin, mun }, Ut);

            return sim;
        }

        /// <summary>
        /// The headline: a pole dragged onto a new right ascension has to turn the planet, and
        /// leave every other body and the sky behind it exactly where they were.
        /// </summary>
        private static void DraggingATiltMovesTheBodyAndNotTheSky()
        {
            Sim sim = Rotating(out SimBody kerbin, out SimBody mun);

            Planetarium.CelestialFrame skyBefore = sim.Zup;
            Planetarium.CelestialFrame munBefore = mun.BodyFrame;
            BodyTilt before = kerbin.Tilt;

            //What TiltEdit.Apply does: write the tilt, then re-latch against the sky.
            kerbin.Tilt = DraggedTilt;
            sim.HoldSkyStill(kerbin, Ut);
            sim.Tick(new[] { kerbin, mun }, Ut);

            Harness.CheckWithin("-", "dragging a tilt leaves the sky alone",
                Harness.FrameRotationAngle(skyBefore, sim.Zup), 1e-9, "deg");

            Harness.CheckWithin("-", "and leaves the other bodies alone",
                Harness.FrameRotationAngle(munBefore, mun.BodyFrame), 1e-9, "deg");

            //The body has to move by the whole of the change, not some of it.
            double expected = Harness.FrameRotationAngle(Frame(before), Frame(DraggedTilt));
            double moved = Harness.FrameRotationAngle(Frame(before), Frame(kerbin.Tilt));

            Harness.Check("-", "and turns the body by the whole of the change",
                Math.Abs(moved - expected) < 1e-9 && expected > 1.0,
                "moved " + Harness.Fmt(moved) + " deg of " + Harness.Fmt(expected));
        }

        /// <summary>
        /// The same for the spin handle, which is the one that moves the angle Zup is measured
        /// from. Re-latching off a stale rotation angle would hand the whole spin to the sky.
        /// </summary>
        private static void DraggingTheSpinMovesTheBodyAndNotTheSky()
        {
            Sim sim = Rotating(out SimBody kerbin, out SimBody mun);

            Planetarium.CelestialFrame skyBefore = sim.Zup;
            Planetarium.CelestialFrame bodyBefore = kerbin.BodyFrame;

            kerbin.InitialRotation += 47.0;
            sim.HoldSkyStill(kerbin, Ut);
            sim.Tick(new[] { kerbin, mun }, Ut);

            Harness.CheckWithin("-", "dragging the spin leaves the sky alone",
                Harness.FrameRotationAngle(skyBefore, sim.Zup), 1e-9, "deg");

            Harness.CheckWithin("-", "and turns the body by exactly that spin",
                Math.Abs(Harness.FrameRotationAngle(bodyBefore, kerbin.BodyFrame) - 47.0), 1e-9, "deg");
        }

        /// <summary>
        /// The control, and the bug this replaced. Dropping the anchor makes the next tick
        /// rebuild it from the body's own frame, which is built to hold the body still - so the
        /// planet does not move and the sky swings by the whole change instead.
        /// </summary>
        private static void DroppingTheAnchorInsteadWouldTurnTheSky()
        {
            Sim sim = Rotating(out SimBody kerbin, out SimBody mun);

            Planetarium.CelestialFrame skyBefore = sim.Zup;
            Planetarium.CelestialFrame bodyBefore = kerbin.BodyFrame;

            kerbin.Tilt = DraggedTilt;
            sim.ResetZupAnchor();
            sim.Tick(new[] { kerbin, mun }, Ut);

            Harness.Check("-", "dropping the anchor instead moves the sky, not the body",
                Harness.FrameRotationAngle(skyBefore, sim.Zup) > 1.0
                && Harness.FrameRotationAngle(bodyBefore, kerbin.BodyFrame) < 1e-6,
                "sky " + Harness.Fmt(Harness.FrameRotationAngle(skyBefore, sim.Zup)) + " deg, body "
                + Harness.Fmt(Harness.FrameRotationAngle(bodyBefore, kerbin.BodyFrame)) + " deg");
        }

        /// <summary>
        /// Editing a body that does not hold the rotating frame needs no re-latch at all: its
        /// tilt reaches its own frame directly. The call still has to be harmless there.
        /// </summary>
        private static void AnotherBodysTiltNeedsNoRelatch()
        {
            Sim sim = Rotating(out SimBody kerbin, out SimBody mun);

            Planetarium.CelestialFrame skyBefore = sim.Zup;
            Planetarium.CelestialFrame munBefore = mun.BodyFrame;

            mun.Tilt = DraggedTilt;
            sim.HoldSkyStill(mun, Ut);
            sim.Tick(new[] { kerbin, mun }, Ut);

            Harness.CheckWithin("-", "editing a body that holds no frame leaves the sky alone",
                Harness.FrameRotationAngle(skyBefore, sim.Zup), 1e-9, "deg");

            Harness.Check("-", "and still moves that body",
                Harness.FrameRotationAngle(munBefore, mun.BodyFrame) > 1.0,
                "moved " + Harness.Fmt(Harness.FrameRotationAngle(munBefore, mun.BodyFrame)) + " deg");
        }

        /// <summary>A tilt's own frame, with no rotation on it.</summary>
        private static Planetarium.CelestialFrame Frame(BodyTilt tilt)
        {
            Planetarium.CelestialFrame frame = default;

            TiltEmFrames.LocalBodyFrame(tilt, 0.0, ref frame);

            return frame;
        }
    }
}
