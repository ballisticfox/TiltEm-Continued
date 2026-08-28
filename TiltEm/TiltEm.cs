using KSP.UI.Screens;
using System;
using System.Collections.Generic;
using System.Reflection;
using TiltEm.Event;
using UnityEngine;

namespace TiltEm
{
    [KSPAddon(KSPAddon.Startup.Instantly, true)]
    public class TiltEm : MonoBehaviour
    {
        #region Fields

        public static TiltEm Singleton;
        public static HarmonyLib.Harmony HarmonyInstance = new HarmonyLib.Harmony("TiltEm");

        /// <summary>
        /// Every body's tilt, keyed by name. A body with no tilt in any config has none, and
        /// CBUpdate treats it as upright.
        /// </summary>
        //Nothing is seeded here. The stock system's tilts ship as configuration in
        //GameData/TiltEm/TiltEm.cfg and are read by TiltLoader like any other pack's, so there is
        //one place to look up what a body is doing and one place to change it. A built-in table
        //alongside it could only ever be a second answer to the same question.
        private static readonly Dictionary<string, BodyTilt> TiltDictionary =
            new Dictionary<string, BodyTilt>();

        /// <summary>Per-star orbital plane normals, as poles, for the map camera's "system up" mode.</summary>
        //Unseeded for the same reason; the stock star's plane is in that same config.
        private static readonly Dictionary<string, BodyTilt> OrbitalPlaneDictionary =
            new Dictionary<string, BodyTilt>();

        #endregion


        #region Unity methods

        // ReSharper disable once UnusedMember.Global
        public void Awake()
        {
            //Before anything else: with Principia installed there is nothing to patch or hook,
            //and destroying the addon also stops Update watching for the map-rotation key.
            if (PrincipiaCheck.Installed)
            {
                Destroy(gameObject);
                return;
            }

            Singleton = this;
            DontDestroyOnLoad(this);
            Debug.Log("[TiltEm]: TiltEm started!");

            //Before PatchAll: a patch could fire an event that does not exist yet.
            RotatingFrameEvents.Init();
            HarmonyInstance.PatchAll(Assembly.GetExecutingAssembly());
            GameEvents.onGameSceneSwitchRequested.Add(SceneRequested);
        }

        //V is stock's flight camera-mode key; gated on map-up and CAMERACONTROLS so it stays
        //off dialogs, text fields and the loading screen.
        // ReSharper disable once UnusedMember.Global
        public void Update()
        {
            if (!Input.GetKeyDown(KeyCode.V)) return;
            if (!MapViewIsUp()) return;
            if (!InputLockManager.IsUnlocked(ControlTypes.CAMERACONTROLS)) return;

            MapCamera.ToggleMapRotation();
        }

        /// <summary>Whether a map camera is up, in flight or in the tracking station.</summary>
        internal static bool MapViewIsUp()
        {
            return HighLogic.LoadedScene == GameScenes.TRACKSTATION
                   || (HighLogic.LoadedSceneIsFlight && MapView.MapIsEnabled);
        }

        #endregion

        #region Game events

        // ReSharper disable once MemberCanBeMadeStatic.Local
        private void SceneRequested(GameEvents.FromToAction<GameScenes, GameScenes> data)
        {
            //Both unconditional: the camera reframes on every scene load, so the eased up axis
            //has to start from the new scene's value rather than swing to it, and an editor
            //session that survived the change would hold the camera in a scene it cannot see.
            BodyEditor.SceneChanged();
            FlightCameraFrame.Reset();
            MapCamera.ResetMapNorth();

            //Only the destination matters; gating on the source too would skip the main-menu to
            //space-centre transition.
            if (data.to < GameScenes.SPACECENTER) return;

            PlanetariumAnchor.ResetZupAnchor();
        }

        #endregion

        #region Public accessors

        /// <summary>Adds a tilt in the legacy Unity Euler format.</summary>
        public static void AddTiltData(CelestialBody body, Vector3d tilt)
        {
            AddTiltData(body, TiltEmFrames.FromLegacyEuler(tilt));
        }

        /// <summary>Adds a tilt as an IAU-style pole direction (preferred form).</summary>
        public static void AddTiltData(CelestialBody body, double poleRa, double poleDec)
        {
            AddTiltData(body, TiltEmFrames.FromPole(poleRa, poleDec));
        }

        /// <summary>Adds an already-built tilt.</summary>
        public static void AddTiltData(CelestialBody body, BodyTilt tilt)
        {
            if (body == null)
            {
                Debug.LogError("[TiltEm]: AddTiltData parameter 'body' cannot be null!");
                return;
            }

            TiltDictionary[body.bodyName] = tilt;
        }

        /// <summary>Records a star's orbital plane as a pole direction (the plane's normal).</summary>
        public static void AddOrbitalPlane(CelestialBody body, BodyTilt plane)
        {
            if (body == null)
            {
                Debug.LogError("[TiltEm]: AddOrbitalPlane parameter 'body' cannot be null!");
                return;
            }

            OrbitalPlaneDictionary[body.bodyName] = plane;
        }

        /// <summary>Returns the given star's orbital plane if one was configured.</summary>
        public static bool TryGetOrbitalPlane(string bodyName, out BodyTilt plane)
        {
            return OrbitalPlaneDictionary.TryGetValue(bodyName, out plane);
        }

        /// <summary>Formatted obliquity string for UI display.</summary>
        public static string GetTiltForDisplay(string bodyName)
        {
            return !TiltDictionary.TryGetValue(bodyName, out BodyTilt tilt)
                ? "0"
                : KSPUtil.LocalizeNumber(tilt.Obliquity, "F2");
        }

        /// <summary>Returns the given tilt if found.</summary>
        public static bool TryGetTilt(string bodyName, out BodyTilt tilt)
        {
            return TiltDictionary.TryGetValue(bodyName, out tilt);
        }

        #endregion

        #region Private methods


        #endregion
    }
}
