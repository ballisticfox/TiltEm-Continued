using System.Diagnostics.CodeAnalysis;

namespace TiltEm.Event
{
    /// <summary>
    /// Notifications Tilt'Em raises for other mods. Nothing in KSP fires these.
    /// </summary>
    public static class RotatingFrameEvents
    {
        /// <summary>
        /// Raised before the dominant body enters or leaves its rotating frame. The host is that
        /// body, the target is the state it is moving to. Informational only.
        /// </summary>
        //camelCase to match the GameEvents fields subscribers already write against.
        [SuppressMessage("Style", "IDE1006:Naming Styles")]
        public static EventData<GameEvents.HostTargetAction<CelestialBody, bool>> beforeRotatingFrameChange;

        /// <summary>
        /// Creates the events. Call once at startup.
        /// </summary>
        public static void Init()
        {
            //Eagerly, not on first use: EventData registers itself by name for FindEvent.
            beforeRotatingFrameChange = new EventData<GameEvents.HostTargetAction<CelestialBody, bool>>(
                nameof(beforeRotatingFrameChange));
        }
    }
}
