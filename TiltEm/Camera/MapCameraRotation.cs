namespace TiltEm
{
    /// <summary>
    /// Which axis the map and tracking-station camera treats as "up".
    /// </summary>
    public enum MapCameraRotation
    {
        /// <summary>The focused body's own pole. What the camera has been doing.</summary>
        PoleUp,

        /// <summary>The orbital plane of the star the focused body ultimately orbits.</summary>
        SystemUp,
    }
}
