namespace TiltEm
{
    /// <summary>
    /// What the debugging aids are allowed to do.
    /// </summary>
    //This used to be #if DEBUG, so a Release build simply did not contain it. It now ships in
    //every build and is switched here instead, which is the seam the configuration layer will
    //drive. Off by default, so a player sees nothing until something turns it on.
    internal static class DebugOptions
    {
        /// <summary>Whether the focused body's axes are drawn wherever a map camera is up.</summary>
        public static bool DrawAxes { get; set; }

        /// <summary>Whether an arrow is drawn along the focused body's own orbital plane normal.</summary>
        public static bool DrawPlaneNormal { get; set; }
    }
}
