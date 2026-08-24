using Kopernicus;
using System;
using TiltEm;
using UnityEngine;

namespace TiltEmKopernicus
{
    /// <summary>
    /// Rewrites the orbits of bodies whose config asked for elements relative to their parent's
    /// pole, converting them once into the celestial-frame elements KSP stores.
    ///
    /// This runs on Kopernicus's OnPostLoad, which fires with the finished system prefab before
    /// the real system is spawned. That is the only point that works. Doing it while the Orbit
    /// node is parsed is too early - the parent's Properties node may not have been read yet, and
    /// bodies are not parsed parent-first - and doing it after the system is live means rewriting
    /// elements out from under an orbit that has already been Init'd.
    ///
    /// Working on the prefab means nothing downstream needs to know: Orbit.Init, the SOI and
    /// hill-sphere maths, the map view and every position derived from the orbit all see ordinary
    /// celestial-frame elements, exactly as if the config had been written that way by hand.
    /// </summary>
    [KSPAddon(KSPAddon.Startup.Instantly, true)]
    public class OrbitFrameLoader : MonoBehaviour
    {
        public void Awake()
        {
            DontDestroyOnLoad(this);
        }

        /// <summary>
        /// Start rather than Awake: Kopernicus's Events class is itself an Instantly addon, and
        /// two addons in the same startup phase have no defined Awake order - so OnPostLoad is
        /// only certain to exist once every Awake in that phase has run. Kopernicus fires it much
        /// later, at PSystemSpawn, so there is no risk of missing it by waiting.
        /// </summary>
        public void Start()
        {
            if (Events.OnPostLoad == null)
            {
                Debug.LogWarning("[TiltEm]: Kopernicus Events.OnPostLoad is unavailable; "
                                 + "Orbit { relativeToParent = true } will be ignored.");
                return;
            }

            Events.OnPostLoad.Add(RebaseParentRelativeOrbits);
        }

        private void RebaseParentRelativeOrbits(PSystem system)
        {
            Events.OnPostLoad.Remove(RebaseParentRelativeOrbits);

            if (system == null || system.rootBody == null) return;

            try
            {
                RebaseChildren(system.rootBody);
            }
            catch (Exception e)
            {
                //A throw here would leave Kopernicus's load half-finished, which is a far worse
                //outcome than some moons sitting in the wrong plane.
                Debug.LogError("[TiltEm]: failed to rebase parent-relative orbits: " + e);
            }
        }

        private static void RebaseChildren(PSystemBody parent)
        {
            for (var i = 0; i < parent.children.Count; i++)
            {
                var child = parent.children[i];

                Rebase(parent, child);
                RebaseChildren(child);
            }
        }

        private static void Rebase(PSystemBody parent, PSystemBody child)
        {
            if (child.celestialBody == null || !child.celestialBody.Get("relativeToParent", false)) return;

            var orbit = child.orbitDriver == null ? null : child.orbitDriver.orbit;
            if (orbit == null)
            {
                Debug.LogWarning("[TiltEm]: " + child.name + " asked for relativeToParent but has no orbit.");
                return;
            }

            if (!TiltConfig.TryReadEffective(parent.celestialBody, out var parentTilt) || parentTilt.IsIdentity)
            {
                //Not a warning: an untilted parent makes the flag a no-op by definition, which is
                //what lets a pack set it on every body without special-casing.
                Debug.Log("[TiltEm]: " + child.name + " is relativeToParent but " + parent.name
                          + " has no tilt; elements left as written.");
                return;
            }

            TiltEmFrames.OrbitElements local;
            local.Inclination = orbit.inclination;
            local.LongitudeOfAscendingNode = orbit.LAN;
            local.ArgumentOfPeriapsis = orbit.argumentOfPeriapsis;

            var celestial = TiltEmFrames.ToCelestialElements(parentTilt, local);

            orbit.inclination = celestial.Inclination;
            orbit.LAN = celestial.LongitudeOfAscendingNode;
            orbit.argumentOfPeriapsis = celestial.ArgumentOfPeriapsis;

            Debug.Log("[TiltEm]: " + child.name + " rebased onto " + parent.name + "'s pole: inc "
                      + local.Inclination.ToString("F4") + " -> " + celestial.Inclination.ToString("F4")
                      + ", LAN " + local.LongitudeOfAscendingNode.ToString("F4") + " -> "
                      + celestial.LongitudeOfAscendingNode.ToString("F4") + ", argPe "
                      + local.ArgumentOfPeriapsis.ToString("F4") + " -> "
                      + celestial.ArgumentOfPeriapsis.ToString("F4"));
        }
    }
}
