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
        /// Built-in tilts, used for any body no config gives a tilt. These are in the legacy Euler
        /// format the mod originally shipped with and are converted to poles on load, so the
        /// numbers stay comparable with older versions and with hand-written TiltEm.cfg files.
        /// </summary>
        private static readonly Dictionary<string, Vector3d> DefaultLegacyTilts = new Dictionary<string, Vector3d>
        {
            ["Sun"] = new Vector3d(7.57, 0, 2.12),
            ["Kerbin"] = new Vector3d(20, 0, 5),
            ["Mun"] = new Vector3d(15.45, 0, 10.61),
            ["Minmus"] = new Vector3d(5.87, 0, 12.63),
            ["Moho"] = new Vector3d(15.14, 0, 30.25),
            ["Eve"] = new Vector3d(120.4, 0, 35.82),
            ["Duna"] = new Vector3d(5.93, 0, 3.81),
            ["Ike"] = new Vector3d(15.41, 0, 4.22),
            ["Jool"] = new Vector3d(0.54, 0, 1.16),
            ["Laythe"] = new Vector3d(10.63, 0, 13.45),
            ["Vall"] = new Vector3d(5.5, 0, 6.12),
            ["Bop"] = new Vector3d(7.1, 0, 9.4),
            ["Tylo"] = new Vector3d(17.3, 0, 6),
            ["Gilly"] = new Vector3d(15.7, 0, 9.69),
            ["Pol"] = new Vector3d(15.4, 0, 3.12),
            ["Dres"] = new Vector3d(8.64, 0, 11.48),
            ["Eeloo"] = new Vector3d(80.63, 0, 12.34),
        };

        private static readonly Dictionary<string, BodyTilt> TiltDictionary = BuildDefaultTilts();

        /// <summary>Per-star orbital plane normals, as poles, for the map camera's "system up" mode.</summary>
        private static readonly Dictionary<string, BodyTilt> OrbitalPlaneDictionary = BuildDefaultOrbitalPlanes();

        private static Dictionary<string, BodyTilt> BuildDefaultOrbitalPlanes()
        {
            return new Dictionary<string, BodyTilt>
            {
                //Kerbol's axis is tilted 7.57 degrees from the celestial pole, but its planets
                //orbit in the celestial equator, so the system plane normal is the celestial pole.
                ["Sun"] = TiltEmFrames.Untilted,
            };
        }

        private static Dictionary<string, BodyTilt> BuildDefaultTilts()
        {
            var tilts = new Dictionary<string, BodyTilt>(DefaultLegacyTilts.Count);
            foreach (var entry in DefaultLegacyTilts)
            {
                tilts[entry.Key] = TiltEmFrames.FromLegacyEuler(entry.Value);
            }

            return tilts;
        }

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
