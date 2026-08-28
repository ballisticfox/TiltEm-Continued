namespace TiltEm
{
    /// <summary>
    /// Whether the in-flight orbital camera frames on the plane of the system rather than on the
    /// body's own pole. Session state only; not persisted across a save.
    /// </summary>
    //Separate from MapCamera's equivalent: the two cameras answer different questions and
    //change independently.
    public static class FlightCameraFrame
    {
        private static bool _systemUp;

        /// <summary>
        /// True while the camera is in the System mode this mod adds after Orbital.
        /// </summary>
        //The getter reads the mode back rather than trusting the flag alone: System is Orbital
        //plus this, so it cannot outlive Orbital. Stock drops out of Orbital below 2km altitude,
        //and Auto changes mode without going through setMode, so there is nothing to hook.
        public static bool SystemUp
        {
            get => _systemUp && IsOrbital();
            set => _systemUp = value;
        }

        private static bool IsOrbital()
        {
            return FlightCamera.fetch != null && FlightCamera.fetch.mode == FlightCamera.Modes.ORBITAL;
        }

        /// <summary>Drops back to the stock orbital framing.</summary>
        //Called on a scene change: the mode is not saved, so coming back to flight should start
        //where stock would rather than in a mode nothing on screen would explain.
        public static void Reset()
        {
            SystemUp = false;
        }
    }
}
