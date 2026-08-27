using Kopernicus;
using System;
using UnityEngine;
using OrbitElements = TiltEm.TiltEmFrames.OrbitElements;

namespace TiltEm
{
    /// <summary>
    /// Rewrites the orbits of bodies whose config asked for elements relative to their parent's
    /// pole, converting them once into the celestial-frame elements KSP stores.
    ///
    /// Runs against Kopernicus's finished system prefab, where every body has been parsed and no
    /// orbit has been initialized yet.
    /// </summary>
    [KSPAddon(KSPAddon.Startup.Instantly, true)]
    public class OrbitFrameLoader : MonoBehaviour
    {
        public void Awake()
        {
            //Destroyed rather than flagged: this one rewrites the system prefab, so it must not
            //reach Start and register the callback at all.
            if (PrincipiaCheck.Installed)
            {
                Destroy(gameObject);
                return;
            }

            //Unlike the other loaders this one has to outlive its own Awake, since the work
            //happens later on Kopernicus's callback.
            DontDestroyOnLoad(this);
        }

        /// <summary>
        /// Start, not Awake: Kopernicus's Events is an Instantly addon too, so OnPostLoad is only
        /// certain to exist once every Awake in that phase has run.
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
                //Caught rather than thrown: a half-finished Kopernicus load is worse than some
                //moons sitting in the wrong plane.
                Debug.LogError("[TiltEm]: failed to rebase parent-relative orbits: " + e);
                DisplayWarning();
            }
        }

        private static void RebaseChildren(PSystemBody parent)
        {
            foreach (PSystemBody child in parent.children)
            {
                Rebase(parent, child);
                RebaseChildren(child);
            }
        }

        private static void Rebase(PSystemBody parent, PSystemBody child)
        {
            if (child.celestialBody == null || !child.celestialBody.Get("relativeToParent", false)) return;

            Orbit orbit = child.orbitDriver?.orbit;
            if (orbit == null)
            {
                Debug.LogWarning("[TiltEm]: " + child.name + " asked for relativeToParent but has no orbit.");
                return;
            }

            //An untilted parent makes the flag a no-op, so a pack can set it on every body.
            if (!TiltConfig.TryReadEffective(parent.celestialBody, out BodyTilt parentTilt) || parentTilt.IsIdentity)
            {
                return;
            }

            OrbitElements local = ParentRelativeOrbit.Read(orbit);

            ParentRelativeOrbit.Write(orbit, TiltEmFrames.ToCelestialElements(parentTilt, local));
        }

        /// <summary>
        /// Warns that parent-relative orbit conversion failed and loading a save is unsafe.
        /// </summary>
        private static void DisplayWarning()
        {
            PopupDialog.SpawnPopupDialog(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), "TiltEmFail", "Warning",
                "Tilt'Em was not able to convert one or more parent-relative orbits due to an exception in the "
                + "loading process.\n"
                + "One or more bodies are left in the wrong orbital plane. Loading a saved game is not "
                + "recommended, because the bodies could move once the issue is fixed.\n\n"
                + "Please report this to the mod author, or the Tilt'Em team, including your KSP.log and your "
                + "ModuleManager.ConfigCache file.\n\n",
                "OK", true, UISkinManager.GetSkin("MainMenuSkin"));
        }
    }
}
