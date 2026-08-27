namespace TiltEm
{
    /// <summary>
    /// Everything the editor is holding for one body: its tilt edit, its orbit edit, and the way
    /// back to where both started.
    /// </summary>
    //One per body rather than one per session, and kept in BodyEditor's registry after the
    //session closes: edits last until the game restarts or the player resets them, so leaving
    //the tab has to leave the originals somewhere.
    public class BodyEdit
    {
        public BodyEdit(CelestialBody body)
        {
            Body = body;
            Tilt = new TiltEdit(body);

            //Null for a root star, which orbits nothing. Every caller checks, and the tilt half
            //of the editor works on such a body regardless.
            Orbit = body.orbit == null || body.orbit.referenceBody == null
                    || ReferenceEquals(body.orbit.referenceBody, body)
                ? null
                : new OrbitEdit(body);
        }

        public CelestialBody Body { get; }

        public TiltEdit Tilt { get; }

        /// <summary>The orbit edit, or null when the body has no parent to orbit.</summary>
        public OrbitEdit Orbit { get; }

        /// <summary>Whether anything about this body has actually been moved.</summary>
        public bool Dirty => Tilt.Dirty || (Orbit != null && Orbit.Dirty);

        /// <summary>Puts the body back the way the editor found it.</summary>
        public void Revert()
        {
            Tilt.Revert();

            if (Orbit != null) Orbit.Revert();
        }
    }
}
