namespace TiltEm
{
    /// <summary>
    /// Which axis the map and tracking-station camera treats as "up".
    ///
    /// Neither is more correct than the other; they answer different questions. Pole up frames
    /// the body you are looking at, so its equator is level and its seasons read at a glance.
    /// System up frames everything in one shared plane, so the relative inclinations of a
    /// system's orbits read at a glance instead - which is what you want when planning a
    /// transfer, and what stock gives you for free by virtue of every plane being the same one.
    /// </summary>
    public enum MapCameraRotation
    {
        /// <summary>The focused body's own pole. What the camera has been doing.</summary>
        PoleUp,

        /// <summary>The orbital plane of the star the focused body ultimately orbits.</summary>
        SystemUp,
    }
}
