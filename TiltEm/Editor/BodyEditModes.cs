namespace TiltEm
{
    /// <summary>Which of the two editors a session is running.</summary>
    public enum BodyEditTarget
    {
        /// <summary>The body's pole and spin.</summary>
        Tilt,

        /// <summary>The body's orbit around its parent.</summary>
        Orbit,
    }

    /// <summary>Which pair of numbers the tilt handles drive.</summary>
    //Presentation only: both pairs describe the same pole, and TiltEdit stores neither of them.
    public enum TiltEditMode
    {
        /// <summary>tiltx and tiltz, the legacy Unity-Euler pair.</summary>
        Tilt,

        /// <summary>poleRA and poleDec, the IAU pole direction.</summary>
        Pole,
    }
}
