using System;
using System.Collections.Generic;
using UnityEngine;

namespace TiltEm.Verification
{
    /// <summary>One body's share of the state CelestialBody_CBUpdate maintains.</summary>
    public class SimBody
    {
        public readonly string Name;
        public BodyTilt Tilt;
        public double RotationPeriod;
        public double InitialRotation;

        public double RotationAngle;
        public double DirectRotAngle;
        public bool InverseRotation;

        public Planetarium.CelestialFrame BodyFrame;
        public Vector3d AngularVelocity;
        public Vector3d ZUpAngularVelocity;

        public SimBody(string name, BodyTilt tilt, double rotationPeriod, double initialRotation)
        {
            Name = name;
            Tilt = tilt;
            RotationPeriod = rotationPeriod;
            InitialRotation = initialRotation;
        }

        public SimBody Clone()
        {
            return (SimBody)MemberwiseClone();
        }

        /// <summary>The body's orientation in the celestial frame - its own pole and spin only.</summary>
        public Planetarium.CelestialFrame LocalFrame()
        {
            var f = default(Planetarium.CelestialFrame);
            TiltEmFrames.LocalBodyFrame(Tilt, RotationAngle, ref f);
            return f;
        }
    }

    /// <summary>
    /// A minimal stand-in for the pieces of KSP state that CelestialBody_CBUpdate touches.
    ///
    /// CelestialBody is a MonoBehaviour and cannot be instantiated outside Unity, so the tick
    /// body here mirrors the patch line for line. Everything that builds a frame or an angular
    /// velocity calls the real shipped TiltEmFrames, so the maths under test is production
    /// code; only the two lines of stock angle bookkeeping are restated.
    /// </summary>
    public class Sim
    {
        // --- planetarium state (static in KSP) ---
        public Planetarium.CelestialFrame Zup;
        public double InverseRotAngle;

        // --- anchor (TiltEm statics) ---
        public Planetarium.CelestialFrame ZupAnchor;
        public double ZupAnchorRotationAngle;

        // Production keys the anchor on the CelestialBody reference; the harness keys on the
        // body's name, which is equivalent here and survives cloning the world.
        private string _zupAnchorBody;

        /// <summary>
        /// Mirrors OrbitPhysicsManager.dominantBody. Null models the case where no physics
        /// manager exists yet - system construction, PSystemSetup.SetSpaceCentre - in which
        /// nothing can arbitrate and the first body to claim the frame keeps it.
        /// </summary>
        public string DominantBody;

        /// <summary>
        /// When set, bodies are updated with the pre-fix formula (BodyFrame = T * Rz(rotationAngle
        /// - InverseRotAngle), Zup left-multiplied by the anchor). Used purely to prove the
        /// multi-body checks actually detect the defect they were written for.
        /// </summary>
        public bool UseLegacyFormula;

        public Sim()
        {
            Zup = Harness.IdentityFrame();
            InverseRotAngle = 0.0;
            ZupAnchor = Zup;
            ZupAnchorRotationAngle = 0.0;
            _zupAnchorBody = null;
        }

        /// <summary>Snapshot of the whole world, so a tick can be replayed under both modes.</summary>
        public Sim Clone()
        {
            return (Sim)MemberwiseClone();
        }

        /// <summary>
        /// When set, the anchor is never released, so a body re-entering the rotating frame
        /// resumes the anchor from its previous stretch. Used to prove the release is
        /// load-bearing.
        /// </summary>
        public bool KeepStaleAnchor;

        /// <summary>
        /// When set, the anchor is never re-latched, so a body taking the rotating frame builds
        /// Zup out of whatever anchor happens to be lying around - including one belonging to a
        /// different body. Used to prove the re-anchor is load-bearing.
        /// </summary>
        public bool SuppressReanchor;

        /// <summary>Mirrors PlanetariumAnchor.EnsureZupAnchor.</summary>
        private void EnsureZupAnchor(SimBody body)
        {
            if (SuppressReanchor) return;
            if (_zupAnchorBody == body.Name) return;

            ZupAnchor = UseLegacyFormula
                ? Zup
                : TiltEmFrames.AnchorFor(body.Tilt, body.RotationAngle, body.BodyFrame, Zup);

            ZupAnchorRotationAngle = body.RotationAngle;
            _zupAnchorBody = body.Name;
        }

        /// <summary>Mirrors PlanetariumAnchor.ReleaseZupAnchor.</summary>
        private void ReleaseZupAnchor(SimBody body)
        {
            if (KeepStaleAnchor) return;
            if (_zupAnchorBody != body.Name) return;

            _zupAnchorBody = null;
        }

        /// <summary>
        /// Mirrors FlightGlobals.clearInverseRotation, which every teleport goes through via
        /// PrepForOrbitSet.
        ///
        /// Note what it does NOT do: it writes inverseRotation directly on every body and never
        /// calls CBUpdate, so nothing downstream of the flag is recomputed and, in the mod's
        /// case, ReleaseZupAnchor never runs. The anchor therefore survives into whatever the
        /// teleport does next - which, for a teleport to a surface, is re-entering the rotating
        /// frame later in the same frame. That is the sequence these checks exist to replay.
        /// </summary>
        public void ClearInverseRotation(IEnumerable<SimBody> bodies)
        {
            foreach (var body in bodies) body.InverseRotation = false;
        }

        /// <summary>
        /// Mirrors OrbitPhysicsManager.setRotatingFrame together with the mod's prefix on it:
        /// the flag is written, and entering the frame latches the anchor.
        ///
        /// Also called out of band, without a tick either side, which is the whole point - stock
        /// reaches it from PostOrbitSet -> CheckReferenceFrame during the teleport itself.
        /// </summary>
        public void SetRotatingFrame(SimBody body, bool rotating)
        {
            body.InverseRotation = rotating;

            if (rotating) EnsureZupAnchor(body);
        }

        /// <summary>
        /// Mirrors PlanetariumAnchor.ResetZupAnchor, which runs from the onGameSceneSwitchRequested
        /// handler. Every load goes through it: quickload, loading a save from the main menu,
        /// and every ordinary scene change.
        ///
        /// Note what the reset leaves behind. The anchor body is cleared, so CBUpdate re-latches
        /// on its next rotating tick, but ZupAnchorRotationAngle is set to zero rather than to
        /// anything meaningful. Any reader that consults the anchor before that re-latch
        /// therefore measures elapsed rotation from zero, which is the body's whole
        /// rotationAngle rather than nothing at all.
        /// </summary>
        public void ResetZupAnchor()
        {
            _zupAnchorBody = null;
            ZupAnchorRotationAngle = 0;
            ZupAnchor = TiltEmFrames.OrIdentity(Zup);
        }

        /// <summary>
        /// Mirrors PlanetariumAnchor.HoldSkyStill, which the body editor calls after writing a
        /// new tilt or spin. Re-latches against the sky as it stands rather than against the
        /// body's own frame, which is what makes the change move the body.
        /// </summary>
        public void HoldSkyStill(SimBody body, double ut)
        {
            if (_zupAnchorBody != body.Name) return;

            ZupAnchor = TiltEmFrames.OrIdentity(Zup);
            ZupAnchorRotationAngle = (body.InitialRotation + 360.0 * (1.0 / body.RotationPeriod) * ut) % 360.0;
        }

        /// <summary>
        /// Mirrors PSystemSetup.SetSpaceCentre, which flips the home body into the rotating
        /// frame by writing the flag directly. It does not route through setRotatingFrame, so
        /// the mod's prefix never fires and no anchor is latched; the next CBUpdate is what
        /// eventually latches one.
        /// </summary>
        public void SetSpaceCentre(SimBody home)
        {
            home.InverseRotation = true;
        }

        /// <summary>
        /// Mirrors the Planetarium_ZupAtT prefix. Orbit.GetOrbitalStateVectorsAtTrueAnomaly
        /// reaches this, so it decides where every on-rails vessel is placed.
        /// </summary>
        public Planetarium.CelestialFrame ZupAtT(SimBody body, double ut)
        {
            if (body == null || !body.InverseRotation) return Zup;

            var anchorBody = body;
            if (_zupAnchorBody != null)
            {
                foreach (var known in _known)
                {
                    if (known.Name != _zupAnchorBody) continue;
                    anchorBody = known;
                    break;
                }
            }

            var anchor = ZupAnchor;
            var anchorRotationAngle = ZupAnchorRotationAngle;

            if (_zupAnchorBody == null && !UnlatchedZupAtTUsesTheResetOrigin)
            {
                anchor = TiltEmFrames.AnchorFor(anchorBody.Tilt, body.RotationAngle, body.BodyFrame, Zup);
                anchorRotationAngle = body.RotationAngle;
            }

            var rotationAngle =
                (anchorBody.InitialRotation + 360.0 * (1.0 / anchorBody.RotationPeriod) * ut) % 360.0;

            return TiltEmFrames.Zup(anchor, anchorBody.Tilt, rotationAngle - anchorRotationAngle);
        }

        /// <summary>
        /// When set, ZupAtT reads ZupAnchorRotationAngle even with no anchor latched, as it did
        /// before that case was handled. Used to prove the handling is load-bearing.
        /// </summary>
        public bool UnlatchedZupAtTUsesTheResetOrigin;

        /// <summary>The body currently holding the anchor, or null. Mirrors PlanetariumAnchor.ZupAnchorBody.</summary>
        public string AnchorBody
        {
            get { return _zupAnchorBody; }
        }

        public bool HoldsAnchor(string name)
        {
            return _zupAnchorBody == name;
        }

        /// <summary>Mirrors PlanetariumAnchor.MayHoldRotatingFrame.</summary>
        public bool MayHoldRotatingFrame(SimBody body)
        {
            if (body == null) return false;
            if (DominantBody != null) return DominantBody == body.Name;

            return _zupAnchorBody == null || _zupAnchorBody == body.Name;
        }

        /// <summary>
        /// Mirrors OrbitPhysicsManager.setDominantBody together with the mod's postfix on it:
        /// stock reassigns the dominant body and rebuilds vessel velocities, and the postfix
        /// ends the outgoing body's rotating frame and releases its anchor.
        ///
        /// Stock on its own does neither, which is the defect these checks cover.
        /// </summary>
        public void SetDominantBody(SimBody incoming)
        {
            var outgoing = DominantBody;
            DominantBody = incoming != null ? incoming.Name : null;

            if (outgoing == null || outgoing == DominantBody) return;
            if (StockHandover) return;

            foreach (var body in _known)
            {
                if (body.Name != outgoing) continue;

                body.InverseRotation = false;
                ReleaseZupAnchor(body);
            }
        }

        /// <summary>
        /// The bodies SetDominantBody can reach, standing in for FlightGlobals.Bodies.
        /// </summary>
        private readonly List<SimBody> _known = new List<SimBody>();

        public void Register(params SimBody[] bodies)
        {
            _known.Clear();
            _known.AddRange(bodies);
        }

        /// <summary>
        /// When set, SetDominantBody behaves as unpatched stock: the outgoing body keeps both
        /// its inverseRotation flag and its anchor. Used to prove the checks have teeth.
        /// </summary>
        public bool StockHandover;

        /// <summary>
        /// When set, the rotating branch is taken by every flagged body, as it was before
        /// MayHoldRotatingFrame existed. The other half of the same witness.
        /// </summary>
        public bool IgnoreEntitlement;

        /// <summary>
        /// Mirrors CelestialBody_CBUpdate.CBUpdate. Call once per body per tick, in the same
        /// order Planetarium.UpdateCBsRecursive would.
        /// </summary>
        public void Tick(SimBody body, double ut)
        {
            var rotPeriodRecip = 1.0 / body.RotationPeriod;
            body.RotationAngle = (body.InitialRotation + 360.0 * rotPeriodRecip * ut) % 360.0;

            if (body.InverseRotation && (IgnoreEntitlement || MayHoldRotatingFrame(body)))
            {
                EnsureZupAnchor(body);

                Zup = UseLegacyFormula
                    ? LegacyZup(ZupAnchor, body.Tilt, InverseRotAngle - ZupAnchorRotationAngle)
                    : TiltEmFrames.Zup(ZupAnchor, body.Tilt, body.RotationAngle - ZupAnchorRotationAngle);

                InverseRotAngle = (body.RotationAngle - body.DirectRotAngle) % 360.0;
            }
            else
            {
                ReleaseZupAnchor(body);

                body.DirectRotAngle = (body.RotationAngle - InverseRotAngle) % 360.0;
            }

            if (UseLegacyFormula)
            {
                TiltEmFrames.LocalBodyFrame(body.Tilt, body.DirectRotAngle, ref body.BodyFrame);
            }
            else
            {
                TiltEmFrames.BodyFrame(body.Tilt, body.RotationAngle, Zup, ref body.BodyFrame);
            }

            var angularSpeed = Math.PI * 2.0 * rotPeriodRecip;
            body.ZUpAngularVelocity = body.BodyFrame.Z * -angularSpeed;
            body.AngularVelocity = body.ZUpAngularVelocity.xzy;
        }

        /// <summary>Ticks a whole system in tree order.</summary>
        public void Tick(IEnumerable<SimBody> bodies, double ut)
        {
            foreach (var body in bodies) Tick(body, ut);
        }

        /// <summary>The pre-fix Zup: anchor left-multiplied instead of right.</summary>
        private static Planetarium.CelestialFrame LegacyZup(Planetarium.CelestialFrame anchor, BodyTilt tilt,
            double elapsed)
        {
            var spin = TiltEmFrames.Spin(elapsed);
            if (!tilt.IsIdentity)
            {
                spin = TiltEmFrames.Multiply(tilt.Tilt, TiltEmFrames.Multiply(spin, tilt.TiltTranspose));
            }

            return TiltEmFrames.Multiply(anchor, spin);
        }

        #region Observables

        /// <summary>
        /// World position of a point fixed to the surface. This is what the terrain, the KSC
        /// and every landed craft ride on.
        /// </summary>
        public static Vector3d SurfaceToWorld(SimBody body, Vector3d bodyFixed)
        {
            return body.BodyFrame.LocalToWorld(bodyFixed);
        }

        /// <summary>
        /// World position of something whose orbit is stored in the celestial frame - every
        /// vessel on rails, every planet. KSP reaches this through Zup.WorldToLocal.
        /// </summary>
        public Vector3d OrbitToWorld(Vector3d celestial)
        {
            return Zup.WorldToLocal(celestial);
        }

        /// <summary>
        /// Direction of a fixed star as seen in body-fixed coordinates. Determines where the
        /// sun sits in the sky, hence seasons and the subsolar latitude.
        /// </summary>
        public Vector3d SkyInBodyFrame(SimBody body, Vector3d celestial)
        {
            return body.BodyFrame.WorldToLocal(Zup.WorldToLocal(celestial));
        }

        /// <summary>
        /// What <see cref="SkyInBodyFrame"/> must equal: a function of the body's own pole and
        /// spin angle alone, with no dependence on Zup or on any other body.
        /// </summary>
        public static Vector3d ExpectedSkyInBodyFrame(SimBody body, Vector3d celestial)
        {
            return body.LocalFrame().WorldToLocal(celestial);
        }

        #endregion
    }
}
