using Kopernicus;
using UnityEngine;

namespace TiltEm
{
    /// <summary>
    /// One body's live tilt edit: where its pole points, how far its prime meridian is turned,
    /// and what both were before the editor touched them.
    /// </summary>
    //The editor owns its numbers rather than reading them back off the pole every frame. Both
    //forms lose something in that round trip - a pole carries no right ascension once its
    //declination reaches 90, and recovering tiltx from one has to pick a branch - and either
    //loss lands mid-drag, as a handle that jumps or reverses under the pointer. Keeping the
    //numbers means a drag only ever adds to what the player last saw.
    public class TiltEdit
    {
        private readonly bool _wasRegistered;
        private readonly BodyTilt _originalTilt;
        private readonly double _originalInitialRotation;

        private double _poleRa;
        private double _poleDec;
        private double _tiltX;
        private double _tiltZ;
        private double _initialRotation;
        private bool _relativeToParent;

        public TiltEdit(CelestialBody body)
        {
            Body = body;

            _wasRegistered = TiltEm.TryGetTilt(body.bodyName, out _originalTilt);
            _originalInitialRotation = body.initialRotation;

            //Pole, not Tilt: poleRA and poleDec are the form the IAU publishes and the form
            //TiltConfig prefers, so it is the one to land on unless someone asks otherwise.
            Mode = TiltEditMode.Pole;

            //Seeded from the body's own config, so a pack that leant this body against its
            //parent's equator gets those numbers back rather than celestial ones.
            _relativeToParent = ParentTilt(body, out BodyTilt ignored) && body.Get("tiltRelativeToParent", false);

            Restore();
        }

        public CelestialBody Body { get; }

        /// <summary>Which pair of numbers the handles drive.</summary>
        public TiltEditMode Mode { get; set; }

        /// <summary>Whether a handle has moved since the session opened.</summary>
        //Seeding is not an edit: it can shift PrimeMeridian into initialRotation without the
        //body moving, and a body that only had its numbers tidied should not turn up in an export.
        public bool Dirty { get; private set; }

        /// <summary>
        /// Whether the pole is written against the parent's equator rather than the celestial one.
        /// </summary>
        //Kopernicus calls this tiltRelativeToParent, and it is the number anyone actually wants
        //for a moon: measured from the celestial equator, a moon sitting squarely in its tilted
        //parent's equator reads as leaning.
        public bool RelativeToParent
        {
            get { return _relativeToParent; }
            set
            {
                if (_relativeToParent == value || !ParentTilt(Body, out BodyTilt parent)) return;

                //The numbers change frame; the body does not move. Toggling this is a change of
                //view, and a view that moved the thing being viewed would be a trap.
                BodyTilt shown = Shown();

                _relativeToParent = value;

                SetShown(value ? TiltEmFrames.ToLocalTilt(parent, shown)
                    : TiltEmFrames.ToCelestialTilt(parent, shown));
            }
        }

        /// <summary>Whether this body has a tilted parent for the flag to mean anything against.</summary>
        public bool CanBeRelativeToParent => ParentTilt(Body, out BodyTilt ignored);

        /// <summary>The pole as the mod stores it, in the celestial frame.</summary>
        public BodyTilt Tilt => Celestial(Shown());

        /// <summary>Right ascension of the pole, in whichever frame the editor is showing.</summary>
        public double PoleRa => _poleRa;

        public double PoleDec => _poleDec;

        /// <summary>Angle between the pole and the pole it is measured from, degrees.</summary>
        public double Obliquity => 90.0 - _poleDec;

        public double TiltX => _tiltX;

        public double TiltZ => _tiltZ;

        /// <summary>The body's spin angle at universal time zero, degrees.</summary>
        public double InitialRotation => _initialRotation;

        /// <summary>Points the pole at a right ascension and declination.</summary>
        public void SetPole(double poleRa, double poleDec)
        {
            //Stopped at the pole rather than carried over it, which would snap the body half a
            //turn about its own axis. The right ascension is kept either way.
            TiltEmFrames.NormalizePole(poleRa, poleDec, out _poleRa, out _poleDec);

            SyncLegacyFromPole();
            Dirty = true;

            Apply();
        }

        /// <summary>Moves the pole by a delta, for a handle being dragged.</summary>
        public void NudgePole(double deltaRa, double deltaDec)
        {
            SetPole(_poleRa + deltaRa, _poleDec + deltaDec);
        }

        /// <summary>Points the pole using the legacy tiltx/tiltz pair.</summary>
        public void SetLegacyTilt(double tiltX, double tiltZ)
        {
            //Signed, matching what ToLegacyEuler hands back, so the readout does not jump
            //between 350 and -10 depending on which way the number last arrived.
            _tiltX = WrapSigned(tiltX);
            _tiltZ = WrapSigned(tiltZ);

            SyncPoleFromLegacy();
            Dirty = true;

            Apply();
        }

        /// <summary>Moves the legacy pair by a delta, for a handle being dragged.</summary>
        public void NudgeLegacyTilt(double deltaX, double deltaZ)
        {
            SetLegacyTilt(_tiltX + deltaX, _tiltZ + deltaZ);
        }

        /// <summary>Turns the body about its own pole.</summary>
        public void SetInitialRotation(double degrees)
        {
            _initialRotation = Wrap(degrees);
            Dirty = true;

            Apply();
        }

        public void NudgeInitialRotation(double degrees)
        {
            SetInitialRotation(_initialRotation + degrees);
        }

        /// <summary>Puts the body back the way the session found it, keeping the tab as it is.</summary>
        //The mode and the frame are the player's place in the editor, not part of what is being
        //edited, so starting the values over does not disturb them.
        public void Revert()
        {
            Restore();
            Dirty = false;
        }

        /// <summary>The pole in the frame the numbers are written in.</summary>
        //Continuous, not plain FromPole: a declination handle spends its time sitting against
        //the clamp at 90, which is exactly where the plain one drops the right ascension and
        //steps the body by it.
        private BodyTilt Shown()
        {
            return TiltEmFrames.FromPoleContinuous(_poleRa, _poleDec);
        }

        /// <summary>That pole carried into the celestial frame, if it was not there already.</summary>
        private BodyTilt Celestial(BodyTilt shown)
        {
            if (!_relativeToParent || !ParentTilt(Body, out BodyTilt parent)) return shown;

            return TiltEmFrames.ToCelestialTilt(parent, shown);
        }

        /// <summary>Loads the numbers from the body as the editor first found it.</summary>
        private void Restore()
        {
            BodyTilt seed = _wasRegistered ? _originalTilt : TiltEmFrames.Untilted;

            //The prime meridian is folded into initialRotation rather than carried. It exists
            //only to hold what a legacy tiltx/tiltz pair implied about the spin, and two constant
            //offsets on the same angle are one number to drag, not two. Nothing moves, because
            //the body frame only ever uses their sum.
            _initialRotation = Wrap(_originalInitialRotation + TiltEmFrames.SpinOffset(seed,
                                        TiltEmFrames.FromPoleContinuous(seed.PoleRa, seed.PoleDec)));

            SetShown(_relativeToParent && ParentTilt(Body, out BodyTilt parent)
                ? TiltEmFrames.ToLocalTilt(parent, seed)
                : seed);
        }

        /// <summary>Adopts a pole as the numbers shown, and pushes the result at the game.</summary>
        private void SetShown(BodyTilt shown)
        {
            TiltEmFrames.NormalizePole(shown.PoleRa, shown.PoleDec, out _poleRa, out _poleDec);

            SyncLegacyFromPole();
            Apply();
        }

        //The pair that is not being dragged is derived from the one that is, so both readouts
        //stay live. Only the dragged pair is ever authoritative, which is what keeps a drag from
        //passing through a conversion that could send it back somewhere else.
        private void SyncLegacyFromPole()
        {
            Vector3d legacy = TiltEmFrames.ToLegacyEuler(Shown());

            _tiltX = legacy.x;
            _tiltZ = legacy.z;
        }

        private void SyncPoleFromLegacy()
        {
            BodyTilt legacy = TiltEmFrames.FromLegacyEuler(new Vector3d(_tiltX, 0.0, _tiltZ));

            //The right ascension is kept where it was when the pole reaches the top, where it no
            //longer has one of its own. Snapping it to zero there moves the declination handle's
            //own plane out from under the pointer.
            double previous = _poleRa;

            TiltEmFrames.NormalizePole(legacy.PoleRa, legacy.PoleDec, out _poleRa, out _poleDec);

            if (legacy.IsIdentity) _poleRa = previous;
        }

        /// <summary>Hands the tilt to the mod proper, where CBUpdate picks it up next tick.</summary>
        //A body that had no registered tilt is left registered as untilted rather than removed.
        //CBUpdate, the cameras and the element readouts all treat the two the same, so there is
        //nothing to tell apart.
        private void Apply()
        {
            TiltEm.AddTiltData(Body, Tilt);
            Body.initialRotation = _initialRotation;

            //Moving the pole changes T, which the anchor was built from. Re-latching against the
            //sky as it stands is what makes the body move; dropping the anchor instead would make
            //the next tick rebuild it from the body's old frame, which holds the body still and
            //swings the whole sky by the change.
            PlanetariumAnchor.HoldSkyStill(Body);
        }

        /// <summary>The parent's tilt, or false when there is no tilted parent to lean against.</summary>
        private static bool ParentTilt(CelestialBody body, out BodyTilt tilt)
        {
            tilt = TiltEmFrames.Untilted;

            if (body == null || body.referenceBody == null || ReferenceEquals(body.referenceBody, body))
            {
                return false;
            }

            return TiltEm.TryGetTilt(body.referenceBody.bodyName, out tilt) && !tilt.IsIdentity;
        }

        private static double Wrap(double degrees)
        {
            double wrapped = degrees % 360.0;
            return wrapped < 0.0 ? wrapped + 360.0 : wrapped;
        }

        /// <summary>Onto (-180, 180].</summary>
        private static double WrapSigned(double degrees)
        {
            double wrapped = Wrap(degrees);
            return wrapped > 180.0 ? wrapped - 360.0 : wrapped;
        }
    }
}
