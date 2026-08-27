using Kopernicus;
using System;
using OrbitElements = TiltEm.TiltEmFrames.OrbitElements;

namespace TiltEm
{
    /// <summary>
    /// One body's live orbit edit: the six elements a handle can move, and what they were before
    /// the editor touched them.
    /// </summary>
    //The orientation is held here rather than read back off the orbit, for the same reason the
    //tilt is. Recovering elements from a frame has to choose a convention, and the usual one puts
    //inclination in 0..180 by turning the node through 180 instead of letting it go negative.
    //Read back mid-drag, that lands as an inclination handle that jumps to 180 as it crosses the
    //parent's equator.
    public class OrbitEdit
    {
        /// <summary>Eccentricity ceiling. A celestial body on an escape trajectory has no
        /// period, and half of KSP divides by one.</summary>
        private const double MaxEccentricity = 0.99;

        private readonly OrbitElements _originalOrientation;
        private readonly double _originalEccentricity;
        private readonly double _originalSemiMajorAxis;
        private readonly double _originalMeanAnomalyAtEpoch;

        private OrbitElements _orientation;
        private bool _relativeToParent;

        public OrbitEdit(CelestialBody body)
        {
            Body = body;

            _originalOrientation = ParentRelativeOrbit.Read(body.orbit);
            _originalEccentricity = body.orbit.eccentricity;
            _originalSemiMajorAxis = body.orbit.semiMajorAxis;
            _originalMeanAnomalyAtEpoch = body.orbit.meanAnomalyAtEpoch;

            //Seeded from the body's own config: a pack that wrote this orbit against its parent's
            //equator meant those numbers, and they are the ones to hand back.
            _relativeToParent = body.Get("relativeToParent", false);

            Restore();
        }

        public CelestialBody Body { get; }

        /// <summary>The body this one orbits.</summary>
        public CelestialBody Parent => Body.orbit.referenceBody;

        /// <summary>
        /// Whether the three orientation elements are read and written in the parent's equatorial
        /// frame rather than the celestial one KSP stores them in.
        /// </summary>
        public bool RelativeToParent
        {
            get { return _relativeToParent; }
            set
            {
                if (_relativeToParent == value) return;

                //A change of view, so the numbers move and the orbit does not.
                OrbitElements celestial = Celestial(_orientation);

                _relativeToParent = value;
                _orientation = Shown(celestial);
            }
        }

        /// <summary>Whether this body's parent is tilted enough for the flag to mean anything.</summary>
        public bool CanBeRelativeToParent =>
            TiltEm.TryGetTilt(Parent.bodyName, out BodyTilt tilt) && !tilt.IsIdentity;

        /// <summary>Whether a handle has moved since the session opened.</summary>
        public bool Dirty { get; private set; }

        private Orbit Orbit => Body.orbit;

        /// <summary>
        /// The three orientation elements, in whichever frame <see cref="RelativeToParent"/>
        /// selects. Degrees.
        /// </summary>
        public OrbitElements Orientation => _orientation;

        public double Eccentricity => Orbit.eccentricity;

        public double SemiMajorAxis => Orbit.semiMajorAxis;

        /// <summary>Mean anomaly at the orbit's epoch, radians, as KSP stores it.</summary>
        public double MeanAnomalyAtEpoch => Orbit.meanAnomalyAtEpoch;

        /// <summary>Sets the orientation, in whichever frame <see cref="RelativeToParent"/> selects.</summary>
        public void SetOrientation(OrbitElements elements)
        {
            //Inclination is left where the handle put it, negative included. Only the frame it
            //builds matters to the game, and a number that can go round is a handle that can.
            _orientation = new OrbitElements(Wrap180(elements.Inclination),
                Wrap360(elements.LongitudeOfAscendingNode), Wrap360(elements.ArgumentOfPeriapsis));

            ParentRelativeOrbit.Write(Orbit, Celestial(_orientation));
            Dirty = true;

            Apply();
        }

        public void SetOrientation(double inclination, double longitudeOfAscendingNode,
            double argumentOfPeriapsis)
        {
            SetOrientation(new OrbitElements(inclination, longitudeOfAscendingNode, argumentOfPeriapsis));
        }

        /// <summary>Moves the orientation by a delta, for a handle being dragged.</summary>
        public void NudgeOrientation(double deltaInclination, double deltaLan, double deltaArgumentOfPeriapsis)
        {
            SetOrientation(_orientation.Inclination + deltaInclination,
                _orientation.LongitudeOfAscendingNode + deltaLan,
                _orientation.ArgumentOfPeriapsis + deltaArgumentOfPeriapsis);
        }

        /// <summary>Sets the orbit's size and shape.</summary>
        public void SetShape(double semiMajorAxis, double eccentricity)
        {
            //Clamped, not rejected: a handle dragged past the limit should stop against it rather
            //than leave the drag doing nothing.
            Orbit.semiMajorAxis = Math.Max(Math.Abs(semiMajorAxis), 1.0);
            Orbit.eccentricity = Clamp(eccentricity, 0.0, MaxEccentricity);
            Dirty = true;

            Apply();
        }

        /// <summary>Slides the body along its orbit, by its mean anomaly at epoch. Radians.</summary>
        public void SetMeanAnomalyAtEpoch(double radians)
        {
            Orbit.meanAnomalyAtEpoch = radians;
            Dirty = true;

            Apply();
        }

        /// <summary>Puts the orbit back the way the session found it, keeping the tab as it is.</summary>
        public void Revert()
        {
            Restore();
            Dirty = false;
        }

        private void Restore()
        {
            ParentRelativeOrbit.Write(Orbit, _originalOrientation);
            Orbit.eccentricity = _originalEccentricity;
            Orbit.semiMajorAxis = _originalSemiMajorAxis;
            Orbit.meanAnomalyAtEpoch = _originalMeanAnomalyAtEpoch;

            _orientation = Shown(_originalOrientation);

            Apply();
        }

        /// <summary>Celestial elements as the editor shows them.</summary>
        private OrbitElements Shown(OrbitElements celestial)
        {
            if (!_relativeToParent || !TiltEm.TryGetTilt(Parent.bodyName, out BodyTilt tilt)) return celestial;

            return TiltEmFrames.ToLocalElements(tilt, celestial);
        }

        /// <summary>The editor's elements as KSP stores them.</summary>
        private OrbitElements Celestial(OrbitElements shown)
        {
            if (!_relativeToParent) return shown;

            //Returns false for an untilted parent, leaving the elements as they came in.
            ParentRelativeOrbit.TryGetCelestialElements(Parent, shown, out OrbitElements celestial);

            return celestial;
        }

        /// <summary>Rebuilds everything KSP derives from the elements, and moves the body there.</summary>
        //Orbit.Init is the whole job, and it does not disturb where the body should be right now:
        //it rebuilds the orbital frame and rederives ObT at epoch from meanAnomalyAtEpoch, and
        //the position at the current time is read back from the epoch every tick anyway.
        private void Apply()
        {
            Orbit.Init();
            Orbit.UpdateFromUT(Planetarium.GetUniversalTime());
        }

        /// <summary>Onto (-180, 180], so an inclination handle can cross zero rather than fold.</summary>
        private static double Wrap180(double degrees)
        {
            double wrapped = Wrap360(degrees);

            return wrapped > 180.0 ? wrapped - 360.0 : wrapped;
        }

        private static double Wrap360(double degrees)
        {
            double wrapped = degrees % 360.0;

            return wrapped < 0.0 ? wrapped + 360.0 : wrapped;
        }

        private static double Clamp(double value, double min, double max)
        {
            if (value < min) return min;
            return value > max ? max : value;
        }
    }
}
