using System;
using UnityEngine;

namespace TiltEm
{
    /// <summary>
    /// A body's obliquity, stored the way astronomers store one: as the direction of the
    /// body's north pole in the celestial frame (IAU-style right ascension / declination).
    ///
    /// This matters more than it sounds. KSP builds every frame through
    /// <see cref="Planetarium.CelestialFrame.PlanetaryFrame"/>, and that call already takes
    /// a pole. PlanetaryFrame(0, 90, rot) is just the degenerate untilted case, which works
    /// out to a pure spin about world +Z. Hand it a real pole and you get a frame that spins
    /// about a tilted axis, with nothing stapled on top.
    ///
    /// The pre-existing approach left-multiplied a <see cref="Quaternion.Euler"/> onto the
    /// finished frame. That cannot express a pole cleanly, and it forced the tilt to be
    /// carried by a different object in each reference-frame mode - which is the root cause
    /// of the threshold-crossing failure. See Docs/REFERENCE_FRAME_DEFECTS.md.
    /// </summary>
    public struct BodyTilt
    {
        /// <summary>Right ascension of the north pole, degrees. Zero when untilted.</summary>
        public double PoleRa;

        /// <summary>Declination of the north pole, degrees. 90 when untilted.</summary>
        public double PoleDec;

        /// <summary>
        /// Constant offset folded into the body's spin angle, degrees.
        ///
        /// A pole plus a spin angle is one parameterisation among many that produce the same
        /// pole, and they disagree on where longitude zero sits. This carries that difference
        /// so a tilt converted from the legacy Euler format reproduces the old body frame
        /// exactly rather than silently shifting every tilted planet's prime meridian.
        ///
        /// It cannot affect the planetarium frame: Zup conjugates by T, and
        /// T * Rz(pm) * Rz(d) * Rz(-pm) * transpose(T) == T * Rz(d) * transpose(T).
        /// </summary>
        public double PrimeMeridian;

        /// <summary>
        /// T, the constant part of the body frame: PlanetaryFrame(PoleRa, PoleDec, 0).
        /// The full body frame is T * Rz(rot), and T maps +Z onto the body's pole.
        /// </summary>
        public Planetarium.CelestialFrame Tilt;

        /// <summary>T transposed, i.e. T inverse. Cached because the Zup conjugation needs it every frame.</summary>
        public Planetarium.CelestialFrame TiltTranspose;

        /// <summary>True when T is the identity, letting the frame math skip the conjugation entirely.</summary>
        public bool IsIdentity;

        /// <summary>Angle between the body's pole and the celestial +Z axis, degrees.</summary>
        public double Obliquity => 90.0 - PoleDec;

    }

    /// <summary>
    /// Frame construction for tilted bodies.
    ///
    /// Everything here is double precision and free of Unity's single-precision
    /// <see cref="Quaternion"/>, and none of it touches an extern/native Unity entry point,
    /// so it can be exercised outside the game (see Docs/verification).
    /// </summary>
    public static class TiltEmFrames
    {
        private const double Rad2Deg = 180.0 / Math.PI;

        /// <summary>An untilted body: pole on +Z, so T is the identity.</summary>
        public static readonly BodyTilt Untilted = FromPole(0.0, 90.0);

        /// <summary>The identity frame, used as the fallback for an unusable planetarium frame.</summary>
        public static readonly Planetarium.CelestialFrame Identity = MakeIdentity();

        private static Planetarium.CelestialFrame MakeIdentity()
        {
            Planetarium.CelestialFrame f;
            f.X = new Vector3d(1.0, 0.0, 0.0);
            f.Y = new Vector3d(0.0, 1.0, 0.0);
            f.Z = new Vector3d(0.0, 0.0, 1.0);
            return f;
        }

        /// <summary>
        /// True when the frame is a usable rotation rather than a zero matrix or NaN.
        ///
        /// This matters because Planetarium.Zup is a static field with no initialiser, so it is
        /// all zeros until the first Planetarium.Awake - and CBUpdate runs well before that
        /// during system construction, since Kopernicus and several stock systems call it
        /// directly. Composing transpose(Zup) with a zero matrix yields a zero body frame, which
        /// puts every surface feature at the body's centre and throws the floating origin
        /// far off. Stock never reads Zup inside CBUpdate, so it never had to guard against this.
        /// </summary>
        public static bool IsUsableRotation(Planetarium.CelestialFrame frame)
        {
            var lengths = frame.X.sqrMagnitude + frame.Y.sqrMagnitude + frame.Z.sqrMagnitude;

            // Three unit axes sum to 3. NaN fails both comparisons, a zero matrix fails the first.
            return lengths > 2.99 && lengths < 3.01;
        }

        /// <summary>
        /// The frame if it is usable, otherwise the identity. Identity is the right fallback:
        /// before the planetarium exists InverseRotAngle is still zero, so an identity Zup makes
        /// BodyFrame reduce to T * Rz(rotationAngle), exactly what stock would have produced.
        /// </summary>
        public static Planetarium.CelestialFrame OrIdentity(Planetarium.CelestialFrame frame)
        {
            return IsUsableRotation(frame) ? frame : Identity;
        }

        #region Frame algebra

        /// <summary>
        /// Rz(angle) - a pure spin about the celestial +Z axis. This is exactly what stock
        /// KSP uses for every frame it builds, and is verified against
        /// Planetarium.CelestialFrame.PlanetaryFrame(0, 90, angle).
        /// </summary>
        public static Planetarium.CelestialFrame Spin(double angle)
        {
            var frame = default(Planetarium.CelestialFrame);
            Planetarium.CelestialFrame.PlanetaryFrame(0.0, 90.0, angle, ref frame);
            return frame;
        }

        /// <summary>
        /// Matrix product a * b, i.e. the frame that applies b and then a.
        /// A CelestialFrame's X/Y/Z are its basis columns, so composing means mapping
        /// each of b's columns through a.
        /// </summary>
        public static Planetarium.CelestialFrame Multiply(Planetarium.CelestialFrame a, Planetarium.CelestialFrame b)
        {
            Planetarium.CelestialFrame result;
            result.X = a.LocalToWorld(b.X);
            result.Y = a.LocalToWorld(b.Y);
            result.Z = a.LocalToWorld(b.Z);
            return result;
        }

        /// <summary>Transpose, which for an orthonormal frame is the inverse.</summary>
        public static Planetarium.CelestialFrame Transpose(Planetarium.CelestialFrame f)
        {
            Planetarium.CelestialFrame result;
            result.X = new Vector3d(f.X.x, f.Y.x, f.Z.x);
            result.Y = new Vector3d(f.X.y, f.Y.y, f.Z.y);
            result.Z = new Vector3d(f.X.z, f.Y.z, f.Z.z);
            return result;
        }

        #endregion

        #region Frames

        /// <summary>
        /// The body's orientation in the celestial frame: T * Rz(rot + primeMeridian).
        ///
        /// This depends on nothing but the body's own pole and its own spin angle, which is the
        /// property everything else hangs off. Note it is NOT what goes into
        /// CelestialBody.BodyFrame - see <see cref="BodyFrame"/>.
        /// </summary>
        public static void LocalBodyFrame(BodyTilt tilt, double rot, ref Planetarium.CelestialFrame frame)
        {
            Planetarium.CelestialFrame.PlanetaryFrame(tilt.PoleRa, tilt.PoleDec, rot + tilt.PrimeMeridian, ref frame);
        }

        /// <summary>
        /// The world frame KSP stores in CelestialBody.BodyFrame:
        ///
        ///     BodyFrame = transpose(Zup) * T * Rz(rot + primeMeridian)
        ///
        /// The transpose(Zup) factor is essential and easy to miss. KSP's world frame is not
        /// inertial - while a body holds the rotating frame, Zup turns the entire sky. Any body
        /// whose orientation is written directly, without undoing that turn, ends up rotating
        /// relative to the sky by however far Zup has travelled.
        ///
        /// Stock hides this because it writes Rz(rotationAngle - InverseRotAngle), and with
        /// every body sharing the +Z spin axis the -InverseRotAngle term happens to be exactly
        /// transpose(Zup). Give a body a pole that is not +Z and the two stop cancelling: Rz
        /// spins about the body's own pole and so can never move it, while Zup turns the sky
        /// about a different axis. The body's obliquity then points the wrong way by the
        /// rotating body's spin phase - which at InverseRotAngle = 180 inverts its seasons.
        ///
        /// Composing transpose(Zup) explicitly makes sky-in-body reduce to
        /// transpose(LocalBodyFrame) * v, i.e. Zup cancels out completely and a body's
        /// orientation depends only on its own rotation and its own pole. It still collapses to
        /// stock's Rz(rotationAngle - InverseRotAngle) when the tilt is identity.
        /// </summary>
        public static void BodyFrame(BodyTilt tilt, double rot, Planetarium.CelestialFrame zup,
            ref Planetarium.CelestialFrame frame)
        {
            var local = default(Planetarium.CelestialFrame);
            LocalBodyFrame(tilt, rot, ref local);
            frame = Multiply(Transpose(OrIdentity(zup)), local);
        }

        /// <summary>
        /// The planetarium frame while <paramref name="tilt"/>'s body is the rotating one:
        ///
        ///     Zup(elapsed) = T * Rz(elapsed) * transpose(T) * anchor
        ///
        /// where elapsed is how far the body has turned since it took the rotating frame.
        ///
        /// The conjugation by T makes the sky revolve about the body's <em>tilted</em> pole
        /// rather than about world +Z. The anchor multiplies on the right, not the left: that is
        /// what falls out of requiring the rotating body's world frame to stay frozen, which is
        /// the whole reason the rotating frame exists.
        ///
        /// The anchor is latched when the body enters its rotating frame, so at that instant
        /// elapsed is zero, the spin term is the identity, and Zup is exactly the value it
        /// already had. That gives continuity at the threshold for free, and also across a
        /// dominant-body change between bodies with different poles.
        ///
        /// Driven by elapsed rotation rather than by Planetarium.InverseRotAngle, so it does not
        /// depend on that global scalar's bookkeeping.
        /// </summary>
        public static Planetarium.CelestialFrame Zup(Planetarium.CelestialFrame anchor, BodyTilt tilt,
            double elapsedRotation)
        {
            var spin = Spin(elapsedRotation);

            if (!tilt.IsIdentity)
            {
                spin = Multiply(tilt.Tilt, Multiply(spin, tilt.TiltTranspose));
            }

            return Multiply(spin, OrIdentity(anchor));
        }

        /// <summary>
        /// The anchor to latch when a body takes the rotating frame, chosen so that the body's
        /// world frame does not move at that instant:
        ///
        ///     anchor = T * Rz(rot + pm) * transpose(current)
        ///
        /// which is the unique anchor satisfying transpose(anchor) * LocalBodyFrame(rot) == current.
        ///
        /// Why this rather than simply capturing Planetarium.Zup. Stock's CBUpdate does not write
        /// BodyFrame at all while a body is inverse-rotating - the body is frozen literally, by
        /// omission, wherever it happened to be pointing, and Planetarium.Zup absorbs the
        /// difference. This version recomputes the frame every tick instead, and while that is
        /// constant in time, its constant value is fixed by the anchor. Capturing Zup makes that
        /// value transpose(Zup) * T * Rz(rot), which only equals where the body already is when
        /// Zup and BodyFrame were last written in the same tick.
        ///
        /// They are not, on entry to the space centre. PSystemSetup.SetSpaceCentre flips the home
        /// body to inverseRotation and then takes FloatingOrigin.SetOffset(scTransform.position)
        /// - and scTransform hangs off the body transform, so the offset is captured from wherever
        /// the body is pointing right then. The last CBUpdate before that ran during
        /// PSystemSetup.SetupSystem at UT 0, because Planetarium only steps its bodies when
        /// unpaused and SetSpaceCentre is what unpauses it. Recomputing the frame on the next
        /// FixedUpdate therefore swung the body from its UT-0 orientation to the save's, dragging
        /// the KSC out from under an origin that had already been fixed.
        ///
        /// Deriving the anchor from the frame instead makes that impossible by construction, and
        /// it is a no-op wherever the two are already consistent: at a threshold crossing current
        /// is transpose(Zup) * T * Rz(rot), the T * Rz factors cancel, and this returns Zup
        /// unchanged. The sky then absorbs the difference exactly as it does in stock, and it
        /// absorbs it as a spin about the body's own tilted pole, so the body keeps its season.
        /// </summary>
        public static Planetarium.CelestialFrame AnchorFor(BodyTilt tilt, double rot,
            Planetarium.CelestialFrame current, Planetarium.CelestialFrame zup)
        {
            //Before the body's first CBUpdate its frame is still all zeros, and there is no
            //orientation worth preserving. Fall back to the planetarium's own frame.
            if (!IsUsableRotation(current)) return OrIdentity(zup);

            var local = default(Planetarium.CelestialFrame);
            LocalBodyFrame(tilt, rot, ref local);

            return Multiply(local, Transpose(current));
        }


        #endregion

        #region Orbital elements

        /// <summary>
        /// KSP's three orientation elements, in degrees. Eccentricity, semi-major axis and the
        /// anomalies describe the shape and the position along the orbit rather than the plane it
        /// lies in, so none of them are touched by a change of reference frame.
        /// </summary>
        public struct OrbitElements
        {
            public double Inclination;
            public double LongitudeOfAscendingNode;
            public double ArgumentOfPeriapsis;
        }

        /// <summary>
        /// The orbit's orientation as a frame, exactly as Orbit.Init builds it. Same generator as
        /// every other frame here: OrbitalFrame(LAN, inc, argPe) is SetFrame(LAN, inc, argPe) and
        /// PlanetaryFrame(ra, dec, rot) is SetFrame(ra - 90, dec - 90, rot + 90), so the two
        /// compose directly.
        /// </summary>
        public static Planetarium.CelestialFrame OrbitalFrame(OrbitElements elements)
        {
            var frame = default(Planetarium.CelestialFrame);
            Planetarium.CelestialFrame.OrbitalFrame(elements.LongitudeOfAscendingNode, elements.Inclination,
                elements.ArgumentOfPeriapsis, ref frame);

            return frame;
        }

        /// <summary>
        /// Re-expresses elements written against the parent's equator as the celestial-frame
        /// elements KSP actually stores.
        ///
        /// KSP measures every orbit against the celestial equator, which is what you want for a
        /// real system - the IAU publishes real orbits that way - and is a nuisance for a made-up
        /// one, where "in my planet's equatorial plane" is the thing you actually mean and the
        /// numbers that express it are not round. Interpreting the elements in the parent's own
        /// frame instead just means composing the parent's tilt onto the orbit frame:
        ///
        ///     celestial = T * OrbitalFrame(local)
        ///
        /// and reading the elements back off the result. An inclination of zero then puts the
        /// orbit exactly in the parent's equatorial plane whatever the parent's pole is doing,
        /// and a nonzero one is measured from that plane.
        ///
        /// T is the pole alone - PlanetaryFrame(ra, dec, 0), with no prime meridian - so the
        /// reference direction the longitude of the ascending node is measured from is inertial.
        /// Folding the prime meridian in would tie the orbit to where the parent's surface
        /// happens to have its longitude zero, which has nothing to do with orbits.
        ///
        /// Exactly identity for an untilted parent, which is what makes this safe to leave on.
        /// </summary>
        public static OrbitElements ToCelestialElements(BodyTilt parentTilt, OrbitElements local)
        {
            //Not merely an optimisation: it keeps the elements bit-identical rather than
            //round-tripped through a decomposition that need not reproduce them exactly.
            if (parentTilt.IsIdentity) return local;

            return DecomposeOrbitalFrame(Multiply(parentTilt.Tilt, OrbitalFrame(local)));
        }

        /// <summary>
        /// The inverse of <see cref="OrbitalFrame"/>: recovers LAN, inclination and argument of
        /// periapsis from a frame. Reading straight off the ZXZ generator in
        /// Planetarium.CelestialFrame.SetFrame, whose columns are
        ///
        ///     Z = (sin A sin B, -cos A sin B, cos B)
        ///     X.z = sin B sin C,  Y.z = sin B cos C
        ///
        /// with A = LAN, B = inclination, C = argument of periapsis.
        /// </summary>
        public static OrbitElements DecomposeOrbitalFrame(Planetarium.CelestialFrame frame)
        {
            OrbitElements elements;

            var cosInc = Clamp(frame.Z.z, -1.0, 1.0);
            var sinInc = Math.Sqrt(Math.Max(0.0, 1.0 - cosInc * cosInc));

            elements.Inclination = Math.Acos(cosInc) * Rad2Deg;

            if (sinInc > 1e-12)
            {
                elements.LongitudeOfAscendingNode = Math.Atan2(frame.Z.x, -frame.Z.y) * Rad2Deg;
                elements.ArgumentOfPeriapsis = Math.Atan2(frame.X.z, frame.Y.z) * Rad2Deg;
            }
            else
            {
                //Equatorial: there is no ascending node, and only LAN + argPe (or their
                //difference, retrograde) is determined. Put the whole angle in argPe, which is
                //the convention KSP's own editors use, and leave the node at zero. Setting
                //LAN = 0 reduces the frame to Rx(inc) * Rz(argPe), whose X column is
                //(cos C, sin C cos B, 0), so argPe comes off that.
                elements.LongitudeOfAscendingNode = 0.0;
                elements.ArgumentOfPeriapsis = Math.Atan2(frame.X.y * cosInc, frame.X.x) * Rad2Deg;
            }

            elements.LongitudeOfAscendingNode = NormalizeDegrees(elements.LongitudeOfAscendingNode);
            elements.ArgumentOfPeriapsis = NormalizeDegrees(elements.ArgumentOfPeriapsis);

            return elements;
        }

        /// <summary>
        /// Re-expresses a tilt written against the parent's equator as the celestial-frame tilt
        /// everything else works in.
        ///
        /// The same composition as <see cref="ToCelestialElements"/>, applied to the other half of
        /// the problem. A gas giant leans 23 degrees; its moons lean half a degree from ITS
        /// equator, not from the celestial one. Writing that without help means adding the two
        /// together as poles by hand for every moon, and redoing all of them whenever the giant's
        /// own tilt is adjusted.
        ///
        /// Composing the parent's tilt onto the moon's does that arithmetic instead. A zero tilt
        /// then puts the moon's pole exactly on the parent's, and a nonzero one leans away from it
        /// by the angle actually meant.
        ///
        /// Only the pole is rebased. The prime meridian rides along untouched, so a legacy
        /// tiltx/tiltz pair keeps the longitude offset it always had and initialRotation goes on
        /// meaning what it meant.
        /// </summary>
        public static BodyTilt ToCelestialTilt(BodyTilt parent, BodyTilt local)
        {
            //Not merely an optimisation, and the same guarantee ToCelestialElements gives: an
            //untilted parent leaves the tilt bit-identical rather than round-tripped through a
            //pole decomposition. PlanetaryFrame(0, 90, 0) is only the identity to within a couple
            //of ulps - it is built out of cos and sin of right angles - so composing with it
            //would otherwise nudge every moon in a pack that sets the flag everywhere.
            if (parent.IsIdentity) return local;

            //The local pole carried into the celestial frame. For an untilted local frame this is
            //the parent's own pole, which is the case the feature exists for.
            var pole = parent.Tilt.LocalToWorld(local.Tilt.Z);

            var dec = Math.Asin(Clamp(pole.z, -1.0, 1.0)) * Rad2Deg;
            var ra = Math.Atan2(pole.y, pole.x) * Rad2Deg;

            return FromPole(ra, dec, local.PrimeMeridian);
        }

        #endregion

        #region Construction

        /// <summary>
        /// Builds a tilt from an IAU-style pole direction.
        /// </summary>
        public static BodyTilt FromPole(double poleRa, double poleDec)
        {
            return FromPole(poleRa, poleDec, 0.0);
        }

        /// <summary>
        /// Builds a tilt from an IAU-style pole direction plus a constant spin offset.
        /// </summary>
        public static BodyTilt FromPole(double poleRa, double poleDec, double primeMeridian)
        {
            BodyTilt tilt;

            tilt.PrimeMeridian = primeMeridian;
            tilt.PoleDec = Clamp(poleDec, -90.0, 90.0);

            // At the pole the right ascension is degenerate. Pinning it to zero there keeps
            // T exactly the identity for an untilted body instead of a stray Rz(ra) spin,
            // which would silently shift the prime meridian. Use initialRotation for that.
            tilt.IsIdentity = tilt.PoleDec >= 90.0;
            tilt.PoleRa = tilt.IsIdentity ? 0.0 : NormalizeDegrees(poleRa);

            tilt.Tilt = default;
            Planetarium.CelestialFrame.PlanetaryFrame(tilt.PoleRa, tilt.PoleDec, 0.0, ref tilt.Tilt);
            tilt.TiltTranspose = Transpose(tilt.Tilt);

            return tilt;
        }

        /// <summary>
        /// Converts the legacy config format - Unity Euler degrees that were left-multiplied
        /// onto the body frame - into the equivalent pole, so existing TiltEm.cfg files and
        /// third-party AddTiltData callers keep working.
        ///
        /// The conversion is exact: the legacy operator is factored as T * Rz(primeMeridian),
        /// where T carries the pole and the leftover spin is folded into PrimeMeridian. That
        /// makes the resulting body frame identical to the one the legacy code produced, so
        /// existing saves do not see their planets rotate under them.
        /// </summary>
        public static BodyTilt FromLegacyEuler(Vector3d euler)
        {
            // The legacy operator as a celestial frame. It acted on the Unity-space frame,
            // where the celestial +Z pole is Unity's +Y, hence the swizzle.
            Planetarium.CelestialFrame legacy;
            UnityEuler(euler.x, euler.y, euler.z).swizzle.FrameVectors(out legacy.X, out legacy.Y, out legacy.Z);

            var pole = legacy.Z;
            var dec = Math.Asin(Clamp(pole.z, -1.0, 1.0)) * Rad2Deg;
            var ra = Math.Atan2(pole.y, pole.x) * Rad2Deg;

            var tilt = FromPole(ra, dec);

            // Whatever is left after removing the pole must be a spin about it, because both
            // frames send +Z to the same place. Recover its signed angle.
            var spin = Multiply(tilt.TiltTranspose, legacy);
            tilt.PrimeMeridian = Math.Atan2(spin.X.y, spin.X.x) * Rad2Deg;

            return tilt;
        }

        /// <summary>
        /// Unity's Euler convention (apply Z, then X, then Y) in double precision.
        ///
        /// QuaternionD.Euler would be the obvious call, but it routes through an extern
        /// native Unity entry point. Composing it from AngleAxis gives the same rotation,
        /// stays in managed code, and remains testable outside the game.
        /// </summary>
        public static QuaternionD UnityEuler(double x, double y, double z)
        {
            return QuaternionD.AngleAxis(y, Vector3d.up)
                   * QuaternionD.AngleAxis(x, Vector3d.right)
                   * QuaternionD.AngleAxis(z, Vector3d.forward);
        }

        #endregion

        #region Helpers

        private static double Clamp(double value, double min, double max)
        {
            if (value < min) return min;
            return value > max ? max : value;
        }

        private static double NormalizeDegrees(double degrees)
        {
            var wrapped = degrees % 360.0;
            return wrapped < 0.0 ? wrapped + 360.0 : wrapped;
        }

        #endregion
    }
}
