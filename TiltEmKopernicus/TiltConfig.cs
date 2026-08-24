using Kopernicus;
using TiltEm;
using UnityEngine;

namespace TiltEmKopernicus
{
    /// <summary>
    /// Reads a body's configured tilt out of Kopernicus's per-body storage.
    ///
    /// Shared by the two things that need it - registering tilts at startup and rebasing
    /// parent-relative orbits during the prefab load - because they have to agree on the
    /// poleRA/poleDec over tiltx/tiltz precedence. If they disagreed, a moon would be placed
    /// against one interpretation of its parent's pole and then lit by another.
    /// </summary>
    internal static class TiltConfig
    {
        /// <summary>
        /// The body's tilt, or <see cref="TiltEmFrames.Untilted"/> if it has none configured.
        /// Returns false in that case, so a caller that needs to tell "no tilt" from "explicitly
        /// upright" can.
        /// </summary>
        public static bool TryRead(CelestialBody body, out BodyTilt tilt)
        {
            tilt = TiltEmFrames.Untilted;

            if (body == null) return false;

            //poleRA/poleDec is the preferred form and wins if present. tiltx/tiltz is the legacy
            //Unity Euler form, converted to the equivalent pole on load.
            if (body.Has("poleRA") || body.Has("poleDec"))
            {
                tilt = TiltEmFrames.FromPole(body.Get("poleRA", 0d), body.Get("poleDec", 90d));
                return true;
            }

            if (body.Has("tiltx") || body.Has("tiltz"))
            {
                tilt = TiltEmFrames.FromLegacyEuler(new Vector3d(body.Get("tiltx", 0d), 0, body.Get("tiltz", 0d)));
                return true;
            }

            return false;
        }

        /// <summary>
        /// The tilt as everything downstream should see it: the configured tilt, composed onto the
        /// parent's when Properties { tiltRelativeToParent = true } asks for it.
        ///
        /// This, not <see cref="TryRead"/>, is what callers want. The flag changes what a given
        /// poleRA/poleDec pair means, so a caller reading the raw values would be working from a
        /// different pole than the game is - the same divergence hazard TiltConfig exists to
        /// prevent, one level up.
        ///
        /// Returns true when the body ends up with any tilt at all, including the case where the
        /// flag is set and no tilt is: that asks for "same pole as my parent", which is a real
        /// pole even though the config named no angles.
        /// </summary>
        public static bool TryReadEffective(CelestialBody body, out BodyTilt tilt)
        {
            return TryReadEffective(body, 0, out tilt);
        }

        /// <summary>
        /// Recurses up the tree, because a moon relative to its gas giant is only right if the
        /// gas giant's own tilt has already been resolved - and that giant may itself be written
        /// relative to its star. Only ancestors are consulted, so this cannot loop back on the
        /// orbit rebase that reads it.
        /// </summary>
        private static bool TryReadEffective(CelestialBody body, int depth, out BodyTilt tilt)
        {
            var configured = TryRead(body, out tilt);

            if (body == null || !body.Get("tiltRelativeToParent", false)) return configured;

            var parent = body.referenceBody;

            //A root star's parent is itself in stock, which is the normal way this ends. The depth
            //cap is only there so a malformed tree cannot recurse forever.
            if (parent == null || ReferenceEquals(parent, body) || depth >= 32)
            {
                Debug.LogWarning("[TiltEm]: " + body.bodyName + " sets tiltRelativeToParent but has "
                                 + "no parent; its tilt is left in the celestial frame.");
                return configured;
            }

            TryReadEffective(parent, depth + 1, out var parentTilt);

            //Captured before composing: afterwards this is the celestial obliquity, which is a
            //different number and the whole point of the flag.
            var ownObliquity = tilt.Obliquity;

            //An untilted parent makes this exactly the identity, so the flag is safe to set on
            //every body in a pack rather than only the ones under a tilted parent.
            tilt = TiltEmFrames.ToCelestialTilt(parentTilt, tilt);

            Debug.Log("[TiltEm]: " + body.bodyName + " tilt composed onto " + parent.bodyName + "'s: "
                      + ownObliquity.ToString("F4") + " deg from " + parent.bodyName + "'s equator ("
                      + parentTilt.Obliquity.ToString("F4") + " deg itself) becomes pole "
                      + tilt.PoleRa.ToString("F4") + " / " + tilt.PoleDec.ToString("F4")
                      + ", " + tilt.Obliquity.ToString("F4") + " deg in the celestial frame");

            return true;
        }

        /// <summary>
        /// The star's configured orbital plane, as the normal of that plane. Same two forms and
        /// the same precedence as the tilt, deliberately: a pack that writes poles for its tilts
        /// should not have to switch conventions to write a system plane.
        ///
        /// Returns false when nothing is configured, which is what lets the map camera fall back
        /// to the star's own pole rather than guessing at a plane.
        /// </summary>
        public static bool TryReadOrbitalPlane(CelestialBody body, out BodyTilt plane)
        {
            plane = TiltEmFrames.Untilted;

            if (body == null) return false;

            if (body.Has("orbitalPlaneRA") || body.Has("orbitalPlaneDec"))
            {
                plane = TiltEmFrames.FromPole(body.Get("orbitalPlaneRA", 0d), body.Get("orbitalPlaneDec", 90d));
                return true;
            }

            if (body.Has("orbitalPlaneX") || body.Has("orbitalPlaneZ"))
            {
                plane = TiltEmFrames.FromLegacyEuler(
                    new Vector3d(body.Get("orbitalPlaneX", 0d), 0, body.Get("orbitalPlaneZ", 0d)));
                return true;
            }

            return false;
        }
    }
}
