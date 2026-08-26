using Kopernicus;
using UnityEngine;

namespace TiltEm
{
    /// <summary>
    /// Reads a body's configured tilt and orbital plane out of Kopernicus's per-body storage.
    /// </summary>
    //Shared by the two callers that need it - registering tilts at startup, and rebasing
    //parent-relative orbits during the prefab load - so they cannot disagree about which pole a
    //body has. Both forms go through TryReadPole for the same reason.
    internal static class TiltConfig
    {
        /// <summary>
        /// The body's tilt, or <see cref="TiltEmFrames.Untilted"/> when none is configured.
        /// False in that case, so a caller can tell "no tilt" from "explicitly upright".
        /// </summary>
        public static bool TryRead(CelestialBody body, out BodyTilt tilt)
        {
            return TryReadPole(body, "poleRA", "poleDec", "tiltx", "tiltz", out tilt);
        }

        /// <summary>
        /// The star's orbital plane, as the normal of that plane. False when none is configured,
        /// which lets the map camera fall back to the star's own pole rather than guess.
        /// </summary>
        public static bool TryReadOrbitalPlane(CelestialBody body, out BodyTilt plane)
        {
            return TryReadPole(body, "orbitalPlaneRA", "orbitalPlaneDec",
                "orbitalPlaneX", "orbitalPlaneZ", out plane);
        }

        /// <summary>
        /// The configured tilt, composed onto the parent's when tiltRelativeToParent is set.
        /// True whenever the body ends up tilted, even if a bare flag inherits the parent's pole.
        /// </summary>
        //This, not TryRead. The flag changes what a given poleRA/poleDec pair means, so a caller
        //on the raw values would work from a different pole than the game does.
        public static bool TryReadEffective(CelestialBody body, out BodyTilt tilt)
        {
            return TryReadEffective(body, 0, out tilt);
        }

        /// <summary>
        /// Recurses up the tree: a moon relative to its gas giant is only right once the giant's
        /// own tilt is resolved, and that giant may itself be written relative to its star.
        /// </summary>
        //Only ancestors are consulted, so this cannot loop back on the orbit rebase that reads it.
        private static bool TryReadEffective(CelestialBody body, int depth, out BodyTilt tilt)
        {
            bool configured = TryRead(body, out tilt);

            if (body == null || !body.Get("tiltRelativeToParent", false)) return configured;

            CelestialBody parent = body.referenceBody;

            //A root star is its own referenceBody in stock, which is how this normally ends. The
            //depth cap only stops a malformed tree recursing forever.
            if (parent == null || ReferenceEquals(parent, body) || depth >= 32)
            {
                Debug.LogWarning("[TiltEm]: " + body.bodyName + " sets tiltRelativeToParent but has "
                                 + "no parent; its tilt is left in the celestial frame.");
                return configured;
            }

            TryReadEffective(parent, depth + 1, out BodyTilt parentTilt);

            //An untilted parent makes this exactly the identity, so a pack can set the flag on
            //every body rather than only those under a tilted parent.
            tilt = TiltEmFrames.ToCelestialTilt(parentTilt, tilt);

            return true;
        }

        /// <summary>
        /// Reads a pole from either supported form, preferring the IAU pair over the legacy Unity
        /// Euler pair.
        /// </summary>
        private static bool TryReadPole(CelestialBody body, string raKey, string decKey,
            string legacyXKey, string legacyZKey, out BodyTilt tilt)
        {
            tilt = TiltEmFrames.Untilted;

            if (body == null) return false;

            if (body.Has(raKey) || body.Has(decKey))
            {
                tilt = TiltEmFrames.FromPole(body.Get(raKey, 0d), body.Get(decKey, 90d));
                return true;
            }

            //The legacy form is Unity Euler degrees, converted to the equivalent pole on load.
            if (body.Has(legacyXKey) || body.Has(legacyZKey))
            {
                tilt = TiltEmFrames.FromLegacyEuler(
                    new Vector3d(body.Get(legacyXKey, 0d), 0, body.Get(legacyZKey, 0d)));
                return true;
            }

            return false;
        }
    }
}
