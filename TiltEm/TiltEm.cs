using KSP.UI.Screens;
using System;
using System.Collections.Generic;
using System.Reflection;
using TiltEm.Event;
using UnityEngine;
using AccessTools = HarmonyLib.AccessTools;

namespace TiltEm
{
    [KSPAddon(KSPAddon.Startup.Instantly, true)]
    public class TiltEm : MonoBehaviour
    {
        #region Fields

        public static TiltEm Singleton;
        public static HarmonyLib.Harmony HarmonyInstance = new HarmonyLib.Harmony("TiltEm");

        private static readonly MethodInfo UpdateFromParameters = AccessTools.Method(typeof(OrbitDriver), "updateFromParameters", new[] { typeof(bool) });

        public static bool GoOnRailsOnRotatingFrameChange { get; set; } = true;

#if DEBUG
        public static bool[] DebugSwitches { get; } = new bool[10];
        public static Action[] DebugActions { get; } = new Action[10];
#endif

        /// <summary>
        /// Here we define the default tilts in case you don't use Kopernicus
        /// </summary>

        public static readonly Dictionary<string, Vector3d> TiltDictionary = new Dictionary<string, Vector3d>
        {
            ["Sun"] = new Vector3d(0, 0, 0),
            ["Kerbin"] = new Vector3d(0, 0, 0),
            ["Mun"] = new Vector3d(0, 0, 0),
            ["Minmus"] = new Vector3d(0, 0, 0),
            ["Moho"] = new Vector3d(0, 0, 0),
            ["Eve"] = new Vector3d(0, 0, 0),
            ["Duna"] = new Vector3d(0, 0, 0),
            ["Ike"] = new Vector3d(0, 0, 0),
            ["Jool"] = new Vector3d(0, 0, 0),
            ["Laythe"] = new Vector3d(0, 0, 0),
            ["Vall"] = new Vector3d(0, 0, 0),
            ["Bop"] = new Vector3d(0, 0, 0),
            ["Tylo"] = new Vector3d(0, 0, 0),
            ["Gilly"] = new Vector3d(0, 0, 0),
            ["Pol"] = new Vector3d(0, 0, 0),
            ["Dres"] = new Vector3d(0, 0, 0),
            ["Eeloo"] = new Vector3d(0, 0, 0),
        };

        #endregion

        #region Unity methods

        /// <summary>
        /// Called just when starting
        /// </summary>
        // ReSharper disable once UnusedMember.Global
        public void Awake()
        {
            Singleton = this;
            DontDestroyOnLoad(this);
            Debug.Log("[TiltEm]: TiltEm started!");

            TiltEmBaseEvent.Awake();
            HarmonyInstance.PatchAll(Assembly.GetExecutingAssembly());
            GameEvents.onGameSceneSwitchRequested.Add(SceneRequested);
            GameEvents.onVesselChange.Add(OnVesselChange);
            GameEvents.onRotatingFrameTransition.Add(RotatingFrameChanged);
            RotatingFrameEvents.beforeRotatingFrameChange.Add(BeforeRotatingFrameChanged);

#if DEBUG
            GameEvents.onGUIApplicationLauncherReady.Add(EnableToolBar);
            DefineDebugActions();
#endif
        }
        
#if DEBUG

        /// <summary>
        /// Called on every GUI frame
        /// </summary>
        // ReSharper disable once InconsistentNaming
        // ReSharper disable once UnusedMember.Global
        public void OnGUI()
        {
            TiltEmGui.SetStyles();
            TiltEmGui.CheckWindowLock();
            TiltEmGui.DrawGui();
        }

#endif

        #endregion

        #region Game events

#if DEBUG

        /// <summary>
        /// Enables the toolbar button
        /// </summary>
        // ReSharper disable once MemberCanBeMadeStatic.Local
        private void EnableToolBar()
        {
            var buttonTexture = GameDatabase.Instance.GetTexture("TiltEm/TiltEmButton", false);
            GameEvents.onGUIApplicationLauncherReady.Remove(EnableToolBar);

            ApplicationLauncher.Instance.AddModApplication(() => TiltEmGui.Display = true, () => TiltEmGui.Display = false,
                () => { }, () => { }, () => { }, () => { }, ApplicationLauncher.AppScenes.ALWAYS, buttonTexture);
        }

#endif

        /// <summary>
        /// When switching to inverse rotation (below 100K on Kerbin) we must restore the planet tilt to 0 as then the planetarium will be tilted in <see cref="Harmony.CelestialBody_CBUpdate"/>.
        /// When switching to NON inverse rotation (above 100K on Kerbin) we must restore the planetarium tilt and then the planet will be tilted in <see cref="Harmony.CelestialBody_CBUpdate"/>.
        ///
        /// Also we must adjust the orbits of the loaded vessels to match the new tilt only if they are going to inverse rotation.
        /// Somehow it's not needed when going from inverse rotation to normal rotation
        /// </summary>
        // ReSharper disable once MemberCanBeMadeStatic.Local
        private void BeforeRotatingFrameChanged(GameEvents.HostTargetAction<CelestialBody, bool> data)
        {
            if (data.host && data.target)
            {
                TiltEmUtil.RestorePlanetTilt(data.host);
            }
            else
            {
                TiltEmUtil.RestorePlanetariumTilt();
            }

            //Only fix the orbit frame when going to inverse rotation (below 100k in Kerbin and body.inverseRotation = true)
            if (data.target && TryGetTilt(data.host.bodyName, out var tilt))
            {
                foreach (var vessel in FlightGlobals.VesselsLoaded)
                {
                    if (vessel.mainBody == data.host && vessel.orbitDriver.updateMode == OrbitDriver.UpdateMode.TRACK_Phys)
                    {
                        TiltEmUtil.ApplyTiltToFrame(ref vessel.orbit.OrbitFrame, -tilt);
                    }
                }
            }
        }

        /// <summary>
        /// Here we adjust position and velocity of the vessels that are in track physics
        /// </summary>
        // ReSharper disable once MemberCanBeMadeStatic.Local
        private void RotatingFrameChanged(GameEvents.HostTargetAction<CelestialBody, bool> data)
        {
            if (TryGetTilt(data.host.bodyName, out _))
            {
                foreach (var vessel in FlightGlobals.VesselsLoaded)
                {
                    if (vessel.mainBody == data.host && vessel.orbitDriver.updateMode == OrbitDriver.UpdateMode.TRACK_Phys)
                    {
                        if (GoOnRailsOnRotatingFrameChange)
                        {
                            vessel.GoOnRails();
                            OrbitPhysicsManager.HoldVesselUnpack(1);
                            vessel.SetPosition(vessel.orbit.getPositionAtUT(Planetarium.GetUniversalTime()), false);
                            UpdateFromParameters.Invoke(vessel.orbitDriver, new object[] { false });
                        }
                        else
                        {
                            if (!data.target) //NOT rotating frame (vessel is now above 100k in Kerbin and body.inverseRotation = false)
                            {
                                vessel.IgnoreGForces(20);

                                vessel.orbit.UpdateFromUT(Planetarium.GetUniversalTime());

                                vessel.SetPosition(vessel.orbit.getPositionAtUT(Planetarium.GetUniversalTime()), false);
                                vessel.SetWorldVelocity(vessel.orbit.GetVel() - Krakensbane.GetFrameVelocity());
                            }
                            else //IN rotating frame (vessel is now below 100k in Kerbin and body.inverseRotation = true)
                            {
                                vessel.IgnoreGForces(20);

                                vessel.orbit.UpdateFromUT(Planetarium.GetUniversalTime());
                                vessel.SetPosition(vessel.orbit.getPositionAtUT(Planetarium.GetUniversalTime()), false);

                                //TODO: Find a way to adjust the velocity that works
                                vessel.SetWorldVelocity(vessel.orbit.GetWorldSpaceVel() - Krakensbane.GetFrameVelocity());
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// When loading a scene that doesn't have a main body we rotate the bodies.
        /// Otherwise if the scene has a main body and we are rotating, we rotate the planetarium instead
        /// </summary>
        // ReSharper disable once MemberCanBeMadeStatic.Local
        private void SceneRequested(GameEvents.FromToAction<GameScenes, GameScenes> data)
        {
            if (data.from < GameScenes.SPACECENTER || data.to < GameScenes.SPACECENTER) return;

            if (FlightGlobals.currentMainBody && FlightGlobals.currentMainBody.inverseRotation)
            {
                TiltEmUtil.RestorePlanetTilt(FlightGlobals.currentMainBody);
            }
            else
            {
                TiltEmUtil.RestorePlanetariumTilt();
            }
        }

        /// <summary>
        /// When loading a vessel that doesn't have a main body or that is not in inverse rotation we rotate the bodies.
        /// Otherwise if the vessel has a main body and is rotating we rotate the planetarium
        /// </summary>
        // ReSharper disable once MemberCanBeMadeStatic.Local
        private void OnVesselChange(Vessel vessel)
        {
            if (vessel.mainBody && vessel.mainBody.inverseRotation)
            {
                TiltEmUtil.RestorePlanetTilt(vessel.mainBody);
            }
            else
            {
                TiltEmUtil.RestorePlanetariumTilt();
            }
        }

        #endregion

        #region Public accessors

        /// <summary>
        /// Adds the tilt of the body into the system.
        /// Feel free to call this method from another mod.
        /// </summary>
        public static void AddTiltData(CelestialBody body, Vector3d tilt)
        {
            if (body == null)
            {
                Debug.LogError("[TiltEm]: AddTiltData parameter 'body' cannot be null!");
                return;
            }

            if (TiltDictionary.ContainsKey(body.bodyName))
            {
                TiltDictionary[body.bodyName] = tilt;
            }
            else
            {
                TiltDictionary.Add(body.bodyName, tilt);
            }
        }

        /// <summary>
        /// Gets the tilt magnitude to display it in a UI for a given body name
        /// </summary>
        public static string GetTiltForDisplay(string bodyName)
        {
            return !TiltDictionary.TryGetValue(bodyName, out var tilt) ? "0" : KSPUtil.LocalizeNumber(tilt.magnitude, "F2");
        }

        /// <summary>
        /// Returns the given tilt if found in the storage
        /// </summary>
        public static bool TryGetTilt(string bodyName, out Vector3d tilt)
        {
            return TiltDictionary.TryGetValue(bodyName, out tilt);
        }

        #endregion

        #region Private methods

#if DEBUG

        /// <summary>
        /// Define actions that you want to be executed when pressing the A0-A9 buttons
        /// </summary>
        public void DefineDebugActions()
        {
            DebugActions[0] = () => { };
            DebugActions[1] = () => { };
            DebugActions[2] = () => { };
            DebugActions[3] = () => { };
            DebugActions[4] = () => { };
            DebugActions[5] = () => { };
            DebugActions[6] = () => { };
            DebugActions[7] = () => { };
            DebugActions[8] = () => { };
            DebugActions[9] = () => { };
        }

#endif

        #endregion

    }
}
