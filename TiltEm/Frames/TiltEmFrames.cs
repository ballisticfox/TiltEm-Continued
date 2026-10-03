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
        private const double Deg2Rad = Math.PI / 180.0;

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
        public static bool IsUsableRotation(in Planetarium.CelestialFrame frame)
        {
            double lengths = SquaredLength(frame.X) + SquaredLength(frame.Y) + SquaredLength(frame.Z);

            //Three unit axes sum to 3. NaN fails both comparisons, a zero matrix fails the first.
            return lengths > 2.99 && lengths < 3.01;
        }

        /// <summary>The frame if it is usable, otherwise the identity.</summary>
        //Identity is the right fallback: before the planetarium exists InverseRotAngle is zero,
        //so an identity Zup makes BodyFrame reduce to T * Rz(rotationAngle).
        public static Planetarium.CelestialFrame OrIdentity(in Planetarium.CelestialFrame frame)
        {
            return IsUsableRotation(frame) ? frame : Identity;
        }

        #region Frame algebra

        //Written out in scalars: KSP's Mono does not inline Vector3d's operators, so each one is a
        //call and a struct copy. Sums keep stock's order, so results match LocalToWorld and
        //WorldToLocal to the bit.

        /// <summary>Rz(angle), a pure spin about the celestial +Z axis.</summary>
        public static Planetarium.CelestialFrame Spin(double angle)
        {
            return Spun(Identity, angle);
        }

        /// <summary>frame * Rz(angle): the frame turned about its own Z column. Degrees.</summary>
        //Two trig calls where PlanetaryFrame spends six, and Z carries through exactly.
        public static Planetarium.CelestialFrame Spun(in Planetarium.CelestialFrame frame, double angle)
        {
            double radians = angle * Deg2Rad;
            double cos = Math.Cos(radians);
            double sin = Math.Sin(radians);

            Planetarium.CelestialFrame result;
            result.X = Combine(frame.X, cos, frame.Y, sin);
            result.Y = Combine(frame.Y, cos, frame.X, -sin);
            result.Z = frame.Z;
            return result;
        }

        /// <summary>Matrix product a * b: the frame that applies b, then a.</summary>
        public static Planetarium.CelestialFrame Multiply(in Planetarium.CelestialFrame a,
            in Planetarium.CelestialFrame b)
        {
            Planetarium.CelestialFrame result;
            result.X = LocalToWorld(a, b.X);
            result.Y = LocalToWorld(a, b.Y);
            result.Z = LocalToWorld(a, b.Z);
            return result;
        }

        /// <summary>transpose(a) * b, without building the transpose.</summary>
        public static Planetarium.CelestialFrame MultiplyTransposed(in Planetarium.CelestialFrame a,
            in Planetarium.CelestialFrame b)
        {
            Planetarium.CelestialFrame result;
            result.X = WorldToLocal(a, b.X);
            result.Y = WorldToLocal(a, b.Y);
            result.Z = WorldToLocal(a, b.Z);
            return result;
        }

        /// <summary>Transpose, which for an orthonormal frame is the inverse.</summary>
        public static Planetarium.CelestialFrame Transpose(in Planetarium.CelestialFrame f)
        {
            Planetarium.CelestialFrame result;
            result.X = new Vector3d(f.X.x, f.Y.x, f.Z.x);
            result.Y = new Vector3d(f.X.y, f.Y.y, f.Z.y);
            result.Z = new Vector3d(f.X.z, f.Y.z, f.Z.z);
            return result;
        }

        /// <summary>frame.Rotation.swizzle, the Unity-space rotation of a frame, bit for bit.</summary>
        //Stock's QuaternionD(X, Y, Z) constructor and swizzle in one pass, without the two struct
        //copies. Same branches and operation order, so the result is identical.
        public static QuaternionD UnityRotation(in Planetarium.CelestialFrame frame)
        {
            double xx = frame.X.x, xy = frame.X.y, xz = frame.X.z;
            double yx = frame.Y.x, yy = frame.Y.y, yz = frame.Y.z;
            double zx = frame.Z.x, zy = frame.Z.y, zz = frame.Z.z;
            double x, y, z, w;

            if (xx + yy + zz >= 0.0)
            {
                double t = xx + yy + zz + 1.0;
                double s = 0.5 / Math.Sqrt(t);
                w = t * s;
                z = (xy - yx) * s;
                y = (zx - xz) * s;
                x = (yz - zy) * s;
            }
            else if (xx > yy && xx > zz)
            {
                double t = xx - yy - zz + 1.0;
                double s = 0.5 / Math.Sqrt(t);
                x = t * s;
                y = (xy + yx) * s;
                z = (zx + xz) * s;
                w = (yz - zy) * s;
            }
            else if (yy > zz)
            {
                double t = 0.0 - xx + yy - zz + 1.0;
                double s = 0.5 / Math.Sqrt(t);
                y = t * s;
                x = (xy + yx) * s;
                w = (zx - xz) * s;
                z = (yz + zy) * s;
            }
            else
            {
                double t = 0.0 - xx - yy + zz + 1.0;
                double s = 0.5 / Math.Sqrt(t);
                z = t * s;
                w = (xy - yx) * s;
                x = (zx + xz) * s;
                y = (yz + zy) * s;
            }

            return new QuaternionD(0.0 - x, 0.0 - z, 0.0 - y, w);
        }

        /// <summary>f * r, as CelestialFrame.LocalToWorld computes it.</summary>
        private static Vector3d LocalToWorld(in Planetarium.CelestialFrame f, in Vector3d r)
        {
            Vector3d v;
            v.x = r.x * f.X.x + r.y * f.Y.x + r.z * f.Z.x;
            v.y = r.x * f.X.y + r.y * f.Y.y + r.z * f.Z.y;
            v.z = r.x * f.X.z + r.y * f.Y.z + r.z * f.Z.z;
            return v;
        }

        /// <summary>transpose(f) * r, as CelestialFrame.WorldToLocal computes it.</summary>
        private static Vector3d WorldToLocal(in Planetarium.CelestialFrame f, in Vector3d r)
        {
            Vector3d v;
            v.x = r.x * f.X.x + r.y * f.X.y + r.z * f.X.z;
            v.y = r.x * f.Y.x + r.y * f.Y.y + r.z * f.Y.z;
            v.z = r.x * f.Z.x + r.y * f.Z.y + r.z * f.Z.z;
            return v;
        }

        /// <summary>a * p + b * q.</summary>
        private static Vector3d Combine(in Vector3d a, double p, in Vector3d b, double q)
        {
            Vector3d v;
            v.x = a.x * p + b.x * q;
            v.y = a.y * p + b.y * q;
            v.z = a.z * p + b.z * q;
            return v;
        }

        private static double SquaredLength(in Vector3d v)
        {
            return v.x * v.x + v.y * v.y + v.z * v.z;
        }

        #endregion

        #region Frames

        /// <summary>
        /// The body's orientation in the celestial frame: T * Rz(rot + primeMeridian).
        /// Not what goes into CelestialBody.BodyFrame - see <see cref="BodyFrame"/>.
        /// </summary>
        //PlanetaryFrame(ra, dec, rot) is exactly T * Rz(rot), so spinning the cached T gives the
        //same frame for a third of the trig.
        public static void LocalBodyFrame(in BodyTilt tilt, double rot, ref Planetarium.CelestialFrame frame)
        {
            frame = Spun(tilt.Tilt, rot + tilt.PrimeMeridian);
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
        public static void BodyFrame(in BodyTilt tilt, double rot, in Planetarium.CelestialFrame zup,
            ref Planetarium.CelestialFrame frame)
        {
            Planetarium.CelestialFrame local = Spun(tilt.Tilt, rot + tilt.PrimeMeridian);

            //An unusable Zup stands in for the identity, whose transpose leaves local as it is.
            frame = IsUsableRotation(zup) ? MultiplyTransposed(zup, local) : local;
        }

        /// <summary>
        /// The planetarium frame while <paramref name="tilt"/>'s body is the rotating one:
        ///
        ///     Zup(elapsed) = T * Rz(elapsed) * transpose(T) * anchor
        ///
        /// Sections 5.4-5.5. Driven by elapsed rotation, not Planetarium.InverseRotAngle.
        /// </summary>
        public static Planetarium.CelestialFrame Zup(in Planetarium.CelestialFrame anchor, in BodyTilt tilt,
            double elapsedRotation)
        {
            return ZupFromBasis(tilt, ZupBasis(anchor, tilt), elapsedRotation);
        }

        /// <summary>
        /// transpose(T) * anchor, the part of <see cref="Zup"/> that holds still between latches.
        /// </summary>
        public static Planetarium.CelestialFrame ZupBasis(in Planetarium.CelestialFrame anchor,
            in BodyTilt tilt)
        {
            Planetarium.CelestialFrame usable = OrIdentity(anchor);

            return tilt.IsIdentity ? usable : MultiplyTransposed(tilt.Tilt, usable);
        }

        /// <summary><see cref="Zup"/> from a basis <see cref="ZupBasis"/> built for the same tilt.</summary>
        public static Planetarium.CelestialFrame ZupFromBasis(in BodyTilt tilt,
            in Planetarium.CelestialFrame basis, double elapsedRotation)
        {
            return Multiply(Spun(tilt.Tilt, elapsedRotation), basis);
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

            ReadPole(pole, out double ra, out double dec);

            return FromPole(ra, dec, local.PrimeMeridian);
        }

        #endregion

        #region Construction

        /// <summary>
        /// Brings a right ascension onto [0, 360) and stops a declination at the pole. The right
        /// ascension is carried through untouched, including at the pole itself.
        /// </summary>
        //Clamped, not tipped over the top. Carrying on past 90 keeps the POLE continuous - the
        //declination comes back down the far side with the right ascension turned through 180 -
        //which is why it looks like the right thing to do. It does not keep the BODY continuous:
        //the same 180 lands on the frame's other two axes, so the planet snaps half a turn about
        //its own axis as the handle crosses. Stopping at the pole is the lesser of the two.
        //
        //The right ascension survives the clamp even though a pole at 90 has none of its own,
        //so a handle dragged onto the pole comes back off along the meridian it arrived on.
        public static void NormalizePole(double poleRa, double poleDec, out double ra, out double dec)
        {
            ra = NormalizeDegrees(poleRa);
            dec = Clamp(poleDec, -90.0, 90.0);
        }

        /// <summary>
        /// Inverse of <see cref="ToCelestialTilt"/>: a celestial-frame tilt re-expressed against
        /// the parent's equator.
        /// </summary>
        public static BodyTilt ToLocalTilt(BodyTilt parent, BodyTilt celestial)
        {
            if (parent.IsIdentity) return celestial;

            Vector3d pole = parent.TiltTranspose.LocalToWorld(celestial.Tilt.Z);

            ReadPole(pole, out double ra, out double dec);

            return FromPole(ra, dec, celestial.PrimeMeridian);
        }

        /// <summary>
        /// A pole that stays continuous as its declination reaches 90, where a right ascension
        /// stops being a direction and becomes a spin.
        /// </summary>
        //T is PlanetaryFrame(ra, dec, 0), which at dec 90 is Rz(ra) rather than the identity.
        //FromPole pins the right ascension away there, and that is right for a config - it keeps
        //an untilted body's frame exactly the identity - but it is a step change of ra degrees in
        //the body's orientation. A handle dragged onto the pole would snap the planet round by
        //it, and one held against the clamp would flip it back and forth every frame the
        //declination crossed. Folding the same angle into the prime meridian holds the body
        //still: PlanetaryFrame(0, 90, rot + ra) is exactly PlanetaryFrame(ra, 90, rot).
        public static BodyTilt FromPoleContinuous(double poleRa, double poleDec)
        {
            return FromPole(poleRa, poleDec, poleDec >= 90.0 ? NormalizeDegrees(poleRa) : 0.0);
        }

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

            ReadPole(legacy.Z, out double ra, out double dec);

            BodyTilt tilt = FromPole(ra, dec);

            //Both frames send +Z to the same place, so whatever is left after removing the pole is
            //a spin about it. Recover its signed angle.
            Planetarium.CelestialFrame spin = Multiply(tilt.TiltTranspose, legacy);
            tilt.PrimeMeridian = Math.Atan2(spin.X.y, spin.X.x) * Rad2Deg;

            return tilt;
        }

        /// <summary>
        /// The spin to add to <paramref name="replacement"/>'s rotation angle so that it puts the
        /// body exactly where <paramref name="original"/> does. Degrees.
        /// </summary>
        //Two tilts that share a pole describe the same body up to a turn about that pole, and
        //this is that turn. Rewriting a tilt in another form - a pole as a legacy pair, a legacy
        //pair with its prime meridian folded away - is only lossless if the difference comes off
        //the rotation angle, and reading it off the frames covers the degenerate poles, where the
        //two forms need not even agree on a right ascension.
        public static double SpinOffset(BodyTilt original, BodyTilt replacement)
        {
            Planetarium.CelestialFrame from = default;
            Planetarium.CelestialFrame to = default;

            LocalBodyFrame(original, 0.0, ref from);
            LocalBodyFrame(replacement, 0.0, ref to);

            //transpose(to) * from fixes the shared pole, so it is a spin about +Z and its X
            //column carries the whole angle.
            Planetarium.CelestialFrame residual = Multiply(Transpose(to), from);

            return NormalizeDegrees(Math.Atan2(residual.X.y, residual.X.x) * Rad2Deg);
        }

        /// <summary>
        /// Inverse of <see cref="FromLegacyEuler"/>: the tiltx/tiltz pair whose legacy operator
        /// puts the pole where <paramref name="tilt"/> has it. Y is always zero, the only value
        /// the config format ever carried.
        /// </summary>
        //Only the pole comes back. The legacy pair has two degrees of freedom, so the spin it
        //implies about that pole is whatever falls out - which is why FromLegacyEuler returns a
        //prime meridian and this cannot take one. A pole round-trips exactly; a whole BodyTilt
        //does not.
        public static Vector3d ToLegacyEuler(BodyTilt tilt)
        {
            //The legacy operator is Rx(x) * Rz(z) acting in Unity space, so it sends Unity's up
            //axis - the celestial pole - to (-sin z, cos z cos x, cos z sin x). Reading that off
            //in celestial components, where y and z are swapped, gives both angles directly.
            Vector3d pole = tilt.Tilt.Z;

            //z is taken on the branch where cos z is non-negative; the other branch describes the
            //same pole with x turned through 180 degrees. atan2 against the length of what is
            //left, for the reason ReadPole gives.
            double z = Math.Atan2(-pole.x, Math.Sqrt(pole.y * pole.y + pole.z * pole.z)) * Rad2Deg;

            //Degenerate when the pole lies on the celestial X axis, where every x gives the same
            //pole. atan2 returns zero there, which is as good an answer as any.
            double x = Math.Atan2(pole.y, pole.z) * Rad2Deg;

            return new Vector3d(x, 0.0, z);
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

        /// <summary>Reads a unit direction back as a right ascension and declination, degrees.</summary>
        //atan2 against the equatorial length, never asin of the polar component: asin loses half
        //its digits as its argument approaches 1, which is exactly where a pole spends its time.
        //The same reasoning as the inclination in DecomposeOrbitalFrame; see section 9.1.
        private static void ReadPole(Vector3d pole, out double ra, out double dec)
        {
            dec = Math.Atan2(pole.z, Math.Sqrt(pole.x * pole.x + pole.y * pole.y)) * Rad2Deg;
            ra = Math.Atan2(pole.y, pole.x) * Rad2Deg;
        }

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
