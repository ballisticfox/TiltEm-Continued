namespace TiltEm
{
    /// <summary>
    /// What the map or tracking-station camera is looking at, as a body.
    /// </summary>
    //Shared rather than repeated: the debug readouts, the editor and anything else that needs a
    //selected body have to agree on what a vessel or a maneuver node counts as.
    public static class MapFocus
    {
        /// <summary>
        /// The focused body, walking up from a vessel or a node to the body it orbits. Null when
        /// no map camera is up, or when nothing on the way up leads to a body.
        /// </summary>
        public static CelestialBody Body()
        {
            MapObject target = PlanetariumCamera.fetch == null ? null : PlanetariumCamera.fetch.target;

            if (target == null) return null;
            if (target.celestialBody != null) return target.celestialBody;
            if (target.vessel != null) return target.vessel.mainBody;

            return target.orbit == null ? null : target.orbit.referenceBody;
        }
    }
}
