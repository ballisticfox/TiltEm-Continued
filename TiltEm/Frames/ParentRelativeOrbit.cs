namespace TiltEm
{
    /// <summary>
    /// Converts orbital elements between the celestial frame KSP stores them in and the parent
    /// body's equatorial frame.
    /// </summary>
    public static class ParentRelativeOrbit
    {
        /// <summary>
        /// An orbit's elements as measured from its parent's equator. Returns false when those
        /// are the stored elements already.
        /// </summary>
        public static bool TryGetLocalElements(Orbit orbit, out TiltEmFrames.OrbitElements local)
        {
            local = default;

            if (!TryGetParentTilt(orbit, out BodyTilt tilt)) return false;

            local = TiltEmFrames.ToLocalElements(tilt, Read(orbit));
            return true;
        }

        /// <summary>
        /// Elements a player typed against <paramref name="body"/>'s equator, as the celestial
        /// elements KSP wants. Returns false when the body is untilted and the two agree.
        /// </summary>
        public static bool TryGetCelestialElements(CelestialBody body, TiltEmFrames.OrbitElements local,
            out TiltEmFrames.OrbitElements celestial)
        {
            celestial = local;

            if (body == null || !TiltEm.TryGetTilt(body.bodyName, out BodyTilt tilt) || tilt.IsIdentity) return false;

            celestial = TiltEmFrames.ToCelestialElements(tilt, local);
            return true;
        }

        /// <summary>The orbit's three orientation elements, as stored.</summary>
        public static TiltEmFrames.OrbitElements Read(Orbit orbit)
        {
            return new TiltEmFrames.OrbitElements(orbit.inclination, orbit.LAN, orbit.argumentOfPeriapsis);
        }

        /// <summary>Writes the three orientation elements back.</summary>
        public static void Write(Orbit orbit, TiltEmFrames.OrbitElements elements)
        {
            orbit.inclination = elements.Inclination;
            orbit.LAN = elements.LongitudeOfAscendingNode;
            orbit.argumentOfPeriapsis = elements.ArgumentOfPeriapsis;
        }

        /// <summary>
        /// The parent's tilt. Returns false when the orbit has no parent, no tilt, or a tilt
        /// that is the identity.
        /// </summary>
        private static bool TryGetParentTilt(Orbit orbit, out BodyTilt tilt)
        {
            tilt = TiltEmFrames.Untilted;

            if (orbit == null || orbit.referenceBody == null) return false;
            if (!TiltEm.TryGetTilt(orbit.referenceBody.bodyName, out tilt)) return false;

            return !tilt.IsIdentity;
        }
    }
}
