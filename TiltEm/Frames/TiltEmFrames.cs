using System;
using UnityEngine;

namespace TiltEm
{
    /// <summary>
    /// A body's obliquity, stored as the direction of the body's north pole in the celestial
    /// frame (IAU-style right ascension and declination). See section 4.1 of
    /// Docs/TILT_MATHEMATICS.pdf.
    /// </summary>
    public struct BodyTilt
    {
        /// <summary>Right ascension of the north pole, degrees. Zero when untilted.</summary>
        public double PoleRa;

        /// <summary>Declination of the north pole, degrees. 90 when untilted.</summary>
        public double PoleDec;

        /// <summary>
        /// Constant offset folded into the body's spin angle, degrees. Carries the longitude-zero
        /// difference when converting from the legacy Euler format. Cannot reach the planetarium
        /// frame (section 5.6).
        /// </summary>
        public double PrimeMeridian;

        /// <summary>
        /// T, the constant part of the body frame: PlanetaryFrame(PoleRa, PoleDec, 0). The full
        /// body frame is T * Rz(rot), and T maps +Z onto the body's pole.
        /// </summary>
        public Planetarium.CelestialFrame Tilt;

        /// <summary>T transposed, so T inverse. Cached because the Zup conjugation needs it every frame.</summary>
        public Planetarium.CelestialFrame TiltTranspose;

        /// <summary>True when T is the identity, which lets the frame maths skip the conjugation.</summary>
        public bool IsIdentity;

        /// <summary>Angle between the body's pole and the celestial +Z axis, degrees.</summary>
        public double Obliquity => 90.0 - PoleDec;
    }

    /// <summary>
    /// Frame construction for tilted bodies. See Docs/TILT_MATHEMATICS.pdf.
    ///
    /// Everything here is double precision, avoids Unity's single-precision
    /// <see cref="Quaternion"/>, and touches no native entry point, so the verification harness
    /// can run it outside the game.
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

        /// <summary>True when the frame is a usable rotation rather than a zero matrix or NaN.</summary>
        //Planetarium.Zup has no initialiser, so it is all zeros until Planetarium.Awake - but
        //CBUpdate runs before that. Composing transpose(Zup) with zeros yields a zero body frame.
        public static bool IsUsableRotation(Planetarium.CelestialFrame frame)
        {
            double lengths = frame.X.sqrMagnitude + frame.Y.sqrMagnitude + frame.Z.sqrMagnitude;

            //Three unit axes sum to 3. NaN fails both comparisons, a zero matrix fails the first.
            return lengths > 2.99 && lengths < 3.01;
        }

        /// <summary>The frame if it is usable, otherwise the identity.</summary>
        //Identity is the right fallback: before the planetarium exists InverseRotAngle is zero,
        //so an identity Zup makes BodyFrame reduce to T * Rz(rotationAngle).
        public static Planetarium.CelestialFrame OrIdentity(Planetarium.CelestialFrame frame)
        {
            return IsUsableRotation(frame) ? frame : Identity;
        }

        #region Frame algebra

        /// <summary>Rz(angle), a pure spin about the celestial +Z axis.</summary>
        public static Planetarium.CelestialFrame Spin(double angle)
        {
            Planetarium.CelestialFrame frame = default;
            Planetarium.CelestialFrame.PlanetaryFrame(0.0, 90.0, angle, ref frame);
            return frame;
        }

        /// <summary>Matrix product a * b: the frame that applies b, then a.</summary>
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
        /// Not what goes into CelestialBody.BodyFrame - see <see cref="BodyFrame"/>.
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
        /// See section 5. Collapses to stock when the tilt is the identity.
        /// </summary>
        //The transpose(Zup) undoes the sky rotation while a body holds the rotating frame. Stock
        //hides this: Rz(rot - InverseRotAngle) happens to equal transpose(Zup) when every body
        //spins about +Z. A different pole breaks that cancellation.
        public static void BodyFrame(BodyTilt tilt, double rot, Planetarium.CelestialFrame zup,
            ref Planetarium.CelestialFrame frame)
        {
            Planetarium.CelestialFrame local = default;
            LocalBodyFrame(tilt, rot, ref local);
            frame = Multiply(Transpose(OrIdentity(zup)), local);
        }

        /// <summary>
        /// The planetarium frame while <paramref name="tilt"/>'s body is the rotating one:
        ///
        ///     Zup(elapsed) = T * Rz(elapsed) * transpose(T) * anchor
        ///
        /// Sections 5.4-5.5. Driven by elapsed rotation, not Planetarium.InverseRotAngle.
        /// </summary>
        public static Planetarium.CelestialFrame Zup(Planetarium.CelestialFrame anchor, BodyTilt tilt,
            double elapsedRotation)
        {
            Planetarium.CelestialFrame spin = Spin(elapsedRotation);

            if (!tilt.IsIdentity)
            {
                spin = Multiply(tilt.Tilt, Multiply(spin, tilt.TiltTranspose));
            }

            return Multiply(spin, OrIdentity(anchor));
        }

        /// <summary>
        /// The anchor to latch when a body takes the rotating frame, chosen so the body does not
        /// move at that instant:
        ///
        ///     anchor = T * Rz(rot + pm) * transpose(current)
        ///
        /// Section 5.2. At an ordinary threshold crossing the T * Rz factors cancel and this
        /// returns Zup unchanged.
        /// </summary>
        //Derived from the body's current frame, not Planetarium.Zup: the two disagree during
        //PSystemSetup.SetSpaceCentre, where recomputing from Zup would swing the body and drag
        //the KSC out from under an origin already fixed.
        public static Planetarium.CelestialFrame AnchorFor(BodyTilt tilt, double rot,
            Planetarium.CelestialFrame current, Planetarium.CelestialFrame zup)
        {
            //Before the body's first CBUpdate its frame is all zeros, so there is no orientation
            //worth preserving. Fall back to the planetarium's own frame.
            if (!IsUsableRotation(current)) return OrIdentity(zup);

            Planetarium.CelestialFrame local = default;
            LocalBodyFrame(tilt, rot, ref local);

            return Multiply(local, Transpose(current));
        }

        #endregion

        #region Orbital elements

        /// <summary>KSP's three orientation elements, in degrees.</summary>
        public struct OrbitElements
        {
            public double Inclination;
            public double LongitudeOfAscendingNode;
            public double ArgumentOfPeriapsis;

            public OrbitElements(double inclination, double longitudeOfAscendingNode,
                double argumentOfPeriapsis)
            {
                Inclination = inclination;
                LongitudeOfAscendingNode = longitudeOfAscendingNode;
                ArgumentOfPeriapsis = argumentOfPeriapsis;
            }
        }

        /// <summary>The orbit's orientation as a frame, as Orbit.Init builds it (section 8.1).</summary>
        public static Planetarium.CelestialFrame OrbitalFrame(OrbitElements elements)
        {
            Planetarium.CelestialFrame frame = default;
            Planetarium.CelestialFrame.OrbitalFrame(elements.LongitudeOfAscendingNode, elements.Inclination,
                elements.ArgumentOfPeriapsis, ref frame);

            return frame;
        }

        /// <summary>
        /// Re-expresses elements written against the parent's equator as the celestial-frame
        /// elements KSP stores:
        ///
        ///     celestial = T * OrbitalFrame(local)
        ///
        /// Section 8.2. T is the pole alone (no prime meridian), so the ascending node stays
        /// inertial.
        /// </summary>
        public static OrbitElements ToCelestialElements(BodyTilt parentTilt, OrbitElements local)
        {
            //Not an optimisation: this keeps the elements bit-identical rather than round-tripping
            //them through a decomposition that need not reproduce them exactly.
            if (parentTilt.IsIdentity) return local;

            return DecomposeOrbitalFrame(Multiply(parentTilt.Tilt, OrbitalFrame(local)));
        }

        /// <summary>
        /// Inverse of <see cref="ToCelestialElements"/>: celestial elements back to the parent's
        /// equator.
        ///
        ///     local = transpose(T) * OrbitalFrame(celestial)
        /// </summary>
        public static OrbitElements ToLocalElements(BodyTilt parentTilt, OrbitElements celestial)
        {
            if (parentTilt.IsIdentity) return celestial;

            return DecomposeOrbitalFrame(Multiply(parentTilt.TiltTranspose, OrbitalFrame(celestial)));
        }

        /// <summary>
        /// Inverse of <see cref="OrbitalFrame"/>: recovers LAN, inclination and argument of
        /// periapsis from a frame. Sections 8.3-8.4.
        /// </summary>
        public static OrbitElements DecomposeOrbitalFrame(Planetarium.CelestialFrame frame)
        {
            double cosInc = Clamp(frame.Z.z, -1.0, 1.0);

            //From the Z column's own equatorial length, never sqrt(1 - cos^2): that form inflates
            //the 1e-16 of rounding any product of frames carries to about 2e-8, which clears the
            //threshold below and sends a retrograde-equatorial frame down the general branch to
            //recover its node from noise. See section 9.2.
            double sinInc = Math.Sqrt(frame.Z.x * frame.Z.x + frame.Z.y * frame.Z.y);

            //atan2, not acos: acos loses half its digits near +/-1, so a polar frame would come
            //back as 179.999999 rather than 180. See section 9.1.
            double inclination = Math.Atan2(sinInc, cosInc) * Rad2Deg;

            double lan;
            double argumentOfPeriapsis;

            if (sinInc > 1e-12)
            {
                lan = Math.Atan2(frame.Z.x, -frame.Z.y) * Rad2Deg;
                argumentOfPeriapsis = Math.Atan2(frame.X.z, frame.Y.z) * Rad2Deg;
            }
            else
            {
                //Equatorial: there is no node, and only LAN + argPe is determined. Putting the
                //whole angle in argPe matches KSP's own editors, and a zero node reduces the
                //frame to Rx(inc) * Rz(argPe), whose X column is (cos C, sin C cos B, 0).
                lan = 0.0;
                argumentOfPeriapsis = Math.Atan2(frame.X.y * cosInc, frame.X.x) * Rad2Deg;
            }

            //Inclination is left unwrapped: atan2 of a non-negative sine already returns 0..180.
            return new OrbitElements(inclination, NormalizeDegrees(lan),
                NormalizeDegrees(argumentOfPeriapsis));
        }

        /// <summary>
        /// Re-expresses a tilt written against the parent's equator as the celestial-frame tilt
        /// everything else works in. Only the pole is rebased; the prime meridian is unchanged.
        /// </summary>
        public static BodyTilt ToCelestialTilt(BodyTilt parent, BodyTilt local)
        {
            //Not an optimisation. PlanetaryFrame(0, 90, 0) is the identity only to within a couple
            //of ulps, being built from cos and sin of right angles, so composing with it would
            //nudge every moon in a pack that sets the flag everywhere.
            if (parent.IsIdentity) return local;

            //The local pole carried into the celestial frame. An untilted local frame lands
            //exactly on the parent's pole, which is the case the feature exists for.
            Vector3d pole = parent.Tilt.LocalToWorld(local.Tilt.Z);

            double dec = Math.Asin(Clamp(pole.z, -1.0, 1.0)) * Rad2Deg;
            double ra = Math.Atan2(pole.y, pole.x) * Rad2Deg;

            return FromPole(ra, dec, local.PrimeMeridian);
        }

        #endregion

        #region Construction

        /// <summary>Builds a tilt from an IAU-style pole direction.</summary>
        public static BodyTilt FromPole(double poleRa, double poleDec)
        {
            return FromPole(poleRa, poleDec, 0.0);
        }

        /// <summary>Builds a tilt from an IAU-style pole direction plus a constant spin offset.</summary>
        public static BodyTilt FromPole(double poleRa, double poleDec, double primeMeridian)
        {
            BodyTilt tilt;

            tilt.PrimeMeridian = primeMeridian;
            tilt.PoleDec = Clamp(poleDec, -90.0, 90.0);

            //Right ascension is degenerate at the pole. Pinning it to zero keeps T exactly the
            //identity for an untilted body, rather than a stray Rz(ra) that would shift the prime
            //meridian. Use initialRotation for that.
            tilt.IsIdentity = tilt.PoleDec >= 90.0;
            tilt.PoleRa = tilt.IsIdentity ? 0.0 : NormalizeDegrees(poleRa);

            tilt.Tilt = default;
            Planetarium.CelestialFrame.PlanetaryFrame(tilt.PoleRa, tilt.PoleDec, 0.0, ref tilt.Tilt);
            tilt.TiltTranspose = Transpose(tilt.Tilt);

            return tilt;
        }

        /// <summary>
        /// Converts the legacy config format (Unity Euler degrees left-multiplied onto the body
        /// frame) into the equivalent pole, so old TiltEm.cfg files keep working. The conversion
        /// is exact.
        /// </summary>
        public static BodyTilt FromLegacyEuler(Vector3d euler)
        {
            //The legacy operator as a celestial frame. It acted on the Unity-space frame, where
            //the celestial +Z pole is Unity's +Y, hence the swizzle.
            Planetarium.CelestialFrame legacy;
            UnityEuler(euler.x, euler.y, euler.z).swizzle.FrameVectors(out legacy.X, out legacy.Y, out legacy.Z);

            Vector3d pole = legacy.Z;
            double dec = Math.Asin(Clamp(pole.z, -1.0, 1.0)) * Rad2Deg;
            double ra = Math.Atan2(pole.y, pole.x) * Rad2Deg;

            BodyTilt tilt = FromPole(ra, dec);

            //Both frames send +Z to the same place, so whatever is left after removing the pole is
            //a spin about it. Recover its signed angle.
            Planetarium.CelestialFrame spin = Multiply(tilt.TiltTranspose, legacy);
            tilt.PrimeMeridian = Math.Atan2(spin.X.y, spin.X.x) * Rad2Deg;

            return tilt;
        }

        /// <summary>Unity's Euler convention (Z, X, Y) in double precision.</summary>
        //Composed from AngleAxis rather than QuaternionD.Euler to avoid the native entry point.
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
            double wrapped = degrees % 360.0;
            return wrapped < 0.0 ? wrapped + 360.0 : wrapped;
        }

        #endregion
    }
}
