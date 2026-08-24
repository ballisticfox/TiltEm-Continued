namespace TiltEm
{
    /// <summary>
    /// Converts orbital elements between the celestial frame KSP stores them in and the parent
    /// body's equatorial frame, for the two places the game shows or asks for them.
    ///
    /// KSP measures every orbit against the celestial equator. That is the right choice for the
    /// stored value - it is one frame for the whole system, so elements stay comparable across
    /// bodies and survive a change of parent - and the wrong one to put in front of a player.
    /// "Inclination 26.4 degrees" tells you nothing useful about an orbit around a planet whose
    /// own axis leans 26.4 degrees; what the player wants to know is that it is equatorial. On an
    /// untilted body the two frames coincide, which is why stock can show the stored number
    /// directly and why this whole problem is invisible in an unmodded game.
    ///
    /// The same reasoning already drives Orbit { relativeToParent = true } on the config side,
    /// and this deliberately shares <see cref="TiltEmFrames.ToCelestialElements"/> with it: a
    /// config written that way and a readout showing it back have to agree, or the number a pack
    /// author typed is not the number the game shows them.
    ///
    /// Everything here is a no-op for an untilted parent, down to bit-identical elements, so a
    /// stock install is untouched.
    /// </summary>
    public static class ParentRelativeOrbit
    {
        /// <summary>
        /// The parent's tilt, or false when the orbit has no parent, no tilt, or a tilt that is
        /// the identity anyway. Callers use the false case to leave stock's numbers completely
        /// alone rather than round-tripping them through a decomposition.
        /// </summary>
        private static bool TryGetParentTilt(Orbit orbit, out BodyTilt tilt)
        {
            tilt = TiltEmFrames.Untilted;

            if (orbit == null || orbit.referenceBody == null) return false;
            if (!TiltEm.TryGetTilt(orbit.referenceBody.bodyName, out tilt)) return false;

            return !tilt.IsIdentity;
        }

        /// <summary>
        /// An orbit's elements as measured from its parent's equator. Returns false when that is
        /// the same thing as the stored elements.
        /// </summary>
        public static bool TryGetLocalElements(Orbit orbit, out TiltEmFrames.OrbitElements local)
        {
            local = default;

            if (!TryGetParentTilt(orbit, out var tilt)) return false;

            TiltEmFrames.OrbitElements celestial;
            celestial.Inclination = orbit.inclination;
            celestial.LongitudeOfAscendingNode = orbit.LAN;
            celestial.ArgumentOfPeriapsis = orbit.argumentOfPeriapsis;

            local = TiltEmFrames.ToLocalElements(tilt, celestial);
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

            if (body == null || !TiltEm.TryGetTilt(body.bodyName, out var tilt) || tilt.IsIdentity) return false;

            celestial = TiltEmFrames.ToCelestialElements(tilt, local);
            return true;
        }

        /// <summary>
        /// Formats an angle the way the maneuver node editor does, so a corrected readout is
        /// indistinguishable from an untouched one. U+00B0, matching stock's own literal.
        /// </summary>
        public static string FormatDegrees(double value)
        {
            return value.ToString("F1") + " \u00B0";
        }
    }
}
