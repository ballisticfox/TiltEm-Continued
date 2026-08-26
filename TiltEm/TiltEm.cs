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

#if DEBUG
        public static bool[] DebugSwitches { get; } = new bool[10];
        public static Action[] DebugActions { get; } = new Action[10];
#endif

        /// <summary>
        /// Default tilts, used when you don't run Kopernicus. These are in the legacy Euler
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

        /// <summary>
        /// Per-star orbital plane normals, as poles, used by the map camera's "system up" mode.
        ///
        /// A star with no entry here and none in config falls back to its own tilt, which is the
        /// closest thing to a system plane that is already known. That fallback is wrong for the
        /// stock system, which is why Kerbol is seeded below.
        /// </summary>
        private static readonly Dictionary<string, BodyTilt> OrbitalPlaneDictionary = BuildDefaultOrbitalPlanes();

        private static Dictionary<string, BodyTilt> BuildDefaultOrbitalPlanes()
        {
            return new Dictionary<string, BodyTilt>
            {
                //Stock Kerbol. Every planet orbits within about two degrees of the celestial
                //equator and Kerbin's inclination is exactly zero there, so the system plane IS
                //the celestial equator and its normal is the celestial pole.
                //
                //Kerbol's own axis is not that plane's normal - DefaultLegacyTilts leans it 7.57
                //degrees - so without this entry the fallback would tip the whole map by that
                //much in system-up mode and draw Kerbin visibly inclined when its inclination is
                //zero. The two coincide only for a star whose equator happens to be its system's
                //plane, which is true of no real system and not of this one.
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

        #region Planetarium anchor

        /// <summary>
        /// The planetarium frame captured when the current body entered its rotating frame,
        /// together with the inverse rotation angle at that moment.
        ///
        /// <see cref="TiltEmFrames.Zup"/> rotates away from this anchor rather than rebuilding
        /// the frame from scratch, which is what makes the threshold crossing continuous: at the
        /// instant of the switch the elapsed angle is zero, so Zup is exactly the value it
        /// already had. Re-anchoring on each entry also keeps Zup continuous when the dominant
        /// body changes to one with a different pole.
        /// </summary>
        //Initialised to identity rather than left as default(CelestialFrame), which is a zero
        //matrix and would poison every frame built from it before the first latch.
        public static Planetarium.CelestialFrame ZupAnchor { get; private set; } = TiltEmFrames.Identity;

        /// <summary>The anchoring body's spin angle when it took the rotating frame.</summary>
        public static double ZupAnchorRotationAngle { get; private set; }

        public static CelestialBody ZupAnchorBody { get; private set; }

        /// <summary>
        /// The body's absolute spin phase at <paramref name="ut"/>, matching stock's formula.
        /// Computed from rotationPeriod rather than rotPeriodRecip so it is valid even before
        /// the body's first CBUpdate has run.
        /// </summary>
        public static double RotationAngleAt(CelestialBody body, double ut)
        {
            if (body.rotationPeriod == 0) return body.initialRotation % 360;
            return (body.initialRotation + 360 * (1 / body.rotationPeriod) * ut) % 360;
        }

        /// <summary>
        /// Latches the anchor to the body now entering its rotating frame. Idempotent, so it is
        /// safe to call both from the setRotatingFrame prefix and defensively from CBUpdate.
        ///
        /// The defensive call matters: Kopernicus clears inverseRotation directly in its
        /// setDominantBody patch without going through setRotatingFrame, so the prefix does not
        /// always fire.
        ///
        /// The anchor is derived from the body's current world frame rather than from
        /// Planetarium.Zup, so that taking the rotating frame cannot move the body - see
        /// <see cref="TiltEmFrames.AnchorFor"/>. Anything that has already measured the body's
        /// orientation, FloatingOrigin.SetOffset above all, stays valid.
        /// </summary>
        public static void EnsureZupAnchor(CelestialBody body, BodyTilt tilt)
        {
            if (ReferenceEquals(ZupAnchorBody, body)) return;

            //body.rotationAngle, not the angle at the current UT: the anchor has to describe the
            //frame the body is actually in, and BodyFrame was last written at that angle. In
            //CBUpdate the two differ by one tick, and pairing the new angle with the old frame is
            //what pins the body in place while the sky takes up the tick - which is exactly what
            //stock does by leaving BodyFrame alone and letting InverseRotAngle advance.
            ZupAnchor = TiltEmFrames.AnchorFor(tilt, body.rotationAngle, body.BodyFrame, Planetarium.Zup);
            ZupAnchorRotationAngle = body.rotationAngle;
            ZupAnchorBody = body;
        }

        /// <summary>
        /// Drops the anchor when a body leaves its rotating frame, so the next entry latches
        /// afresh rather than resuming an anchor from a previous stretch.
        ///
        /// Without this the anchor survives the whole inertial arc while Zup sits frozen - no
        /// body is rotating, so nothing writes it - and the body keeps turning. Re-entry then
        /// evaluates Zup at elapsed = rotationAngle - ZupAnchorRotationAngle, which has grown by
        /// the body's entire rotation during the arc, and Zup snaps forward by that whole amount
        /// in one tick. Everything positioned through Zup - which is every vessel on rails, via
        /// Orbit.getRelativePositionAtUT - jumps with it. On Kerbin, twenty minutes above the
        /// threshold is 20 degrees of rotation, so a craft at 700 km moves about 240 km.
        ///
        /// It is one-directional, which is why the symptom was: inertial to rotating jumps,
        /// rotating to inertial is clean. Leaving the rotating frame only freezes Zup, and
        /// freezing something is always continuous.
        ///
        /// Called from CBUpdate rather than from the setRotatingFrame postfix because that is
        /// the one path every route goes through: Kopernicus clears inverseRotation directly in
        /// its setDominantBody patch (E1), and PSystemSetup clears it on every scene change.
        /// The ReferenceEquals guard makes it idempotent and order-independent, so a
        /// dominant-body handover cannot release the incoming body's fresh anchor.
        /// </summary>
        public static void ReleaseZupAnchor(CelestialBody body)
        {
            if (!ReferenceEquals(ZupAnchorBody, body)) return;

            ZupAnchorBody = null;
        }

        /// <summary>
        /// Whether <paramref name="body"/> is entitled to hold the rotating frame this tick.
        ///
        /// There is one Planetarium.Zup and one anchor, so exactly one body may drive them.
        /// Stock does not enforce that. OrbitPhysicsManager.setDominantBody reassigns
        /// dominantBody without clearing the outgoing body's inverseRotation, and
        /// checkReferenceFrame only ever tests whichever body is dominant now. For most bodies
        /// it never shows, because you cross the threshold on the way out and the flag is
        /// cleared before the sphere of influence changes. It shows for a body whose
        /// inverseRotThresholdAltitude reaches past its own SOI - Mimas, in the real-scale
        /// packs - because then there is no altitude inside the SOI at which the flag can be
        /// cleared, and leaving carries it away still set.
        ///
        /// With two bodies flagged, both take the rotating branch of CBUpdate, both write Zup,
        /// and both call EnsureZupAnchor, which re-latches every time the body differs - from a
        /// BodyFrame that was written against the other body's Zup moments earlier. That is a
        /// feedback loop, and everything positioned through Zup rides it.
        ///
        /// The dominant body is the authority whenever there is one: the rotating frame exists
        /// for the active vessel's physics, and that physics is referenced to the dominant body
        /// and to nothing else. Before the physics manager exists - system construction,
        /// PSystemSetup.SetSpaceCentre - nothing can arbitrate, so whichever body got here
        /// first keeps it, which is also what preserves the space centre's frame at load.
        /// </summary>
        public static bool MayHoldRotatingFrame(CelestialBody body)
        {
            if (body == null) return false;

            //Null whenever the physics manager is absent; its own getter guards fetch.
            var dominant = OrbitPhysicsManager.DominantBody;
            if (dominant != null) return ReferenceEquals(body, dominant);

            return ZupAnchorBody == null || ReferenceEquals(ZupAnchorBody, body);
        }

        /// <summary>
        /// Drops the anchor so the next rotating body re-latches. Required on scene changes,
        /// because Planetarium.Awake rebuilds Zup - an anchor captured against the previous
        /// scene's frame would no longer mean anything.
        /// </summary>
        public static void ResetZupAnchor()
        {
            ZupAnchorBody = null;
            ZupAnchorRotationAngle = 0;
            ZupAnchor = TiltEmFrames.OrIdentity(Planetarium.Zup);
        }

        #endregion

        #region Map camera rotation

        /// <summary>
        /// Which axis the map camera treats as up. Session state, deliberately not saved: it is
        /// a viewing preference, like the camera distance, not part of the game.
        /// </summary>
        public static MapCameraRotation MapRotation { get; private set; } = MapCameraRotation.PoleUp;

        public static void ToggleMapRotation()
        {
            MapRotation = MapRotation == MapCameraRotation.PoleUp
                ? MapCameraRotation.SystemUp
                : MapCameraRotation.PoleUp;

            ScreenMessages.PostScreenMessage("Rotation: " + MapRotationName(MapRotation), 3f,
                ScreenMessageStyle.UPPER_CENTER);
        }

        public static string MapRotationName(MapCameraRotation rotation)
        {
            return rotation == MapCameraRotation.SystemUp ? "System up" : "Pole up";
        }

        /// <summary>
        /// How fast the camera's up axis chases a new one, in reciprocal seconds. The remaining
        /// angle decays by 1/e every 1/this seconds, so ~0.1 s to close most of the gap and a
        /// little over 0.3 s to settle - a few frames of ease rather than a cut.
        /// </summary>
        private const float MapNorthSharpness = 10f;

        /// <summary>Below this the remainder is not worth animating, so snap and be exact.</summary>
        private const float MapNorthSnapDegrees = 0.01f;

        //Vector3.zero is the "nothing established yet" sentinel: a real up axis is always a unit
        //vector, so it can never collide with one.
        private static Vector3 _mapNorth = Vector3.zero;

        /// <summary>
        /// Eases the map camera's up axis toward <paramref name="target"/>, so switching rotation
        /// mode - or focusing a body with a different pole - swings rather than cuts.
        ///
        /// The step is 1 - exp(-k dt) rather than stock's k * dt. Both ease, but only this one is
        /// frame-rate independent: the remaining angle after a step is (1 - t) times what it was,
        /// so over a fixed wall-clock interval the total is exp(-k T) however the interval is
        /// divided. Stock's form gives a different result at 30 fps than at 144.
        ///
        /// Slerped, not lerped, because the axis is a direction: lerping two unit vectors dips
        /// through the inside of the sphere and moves at the wrong angular rate on the way.
        /// </summary>
        public static Vector3 SmoothMapNorth(Vector3 target)
        {
            //First frame, or the first after a scene change: adopt it outright. Easing here would
            //swing the camera up from wherever the sentinel left it every time the map opens.
            if (_mapNorth == Vector3.zero)
            {
                _mapNorth = target;
                return _mapNorth;
            }

            if (Vector3.Angle(_mapNorth, target) < MapNorthSnapDegrees)
            {
                _mapNorth = target;
                return _mapNorth;
            }

            var step = 1f - Mathf.Exp(-MapNorthSharpness * Time.unscaledDeltaTime);
            _mapNorth = Vector3.Slerp(_mapNorth, target, step).normalized;

            return _mapNorth;
        }

        /// <summary>
        /// Drops the eased axis so the next frame adopts its target outright. The planetarium is
        /// rebuilt across scene loads and the camera reframes anyway, so animating across that
        /// boundary would only produce a swing on arrival.
        /// </summary>
        public static void ResetMapNorth()
        {
            _mapNorth = Vector3.zero;
        }

        #endregion

        #region Body axes

        /// <summary>
        /// The body's north pole as a direction in the <em>celestial</em> frame, Unity-swizzled.
        ///
        /// This is the pole as the sky sees it: constant, unaffected by which body currently
        /// holds the rotating frame. Use it wherever the sky has been cancelled separately -
        /// the map camera composes Planetarium.Rotation itself, so feeding it a world pole
        /// would apply Zup twice.
        /// </summary>
        public static Vector3 CelestialNorth(CelestialBody body)
        {

            if (body == null || !TryGetTilt(body.bodyName, out var tilt)) return Vector3.up;

            return tilt.Tilt.Z.xzy;
        }

        /// <summary>
        /// The body's north pole as a direction in <em>world</em> space, Unity-swizzled.
        ///
        /// This is the pole where things actually are, which is what anything positioning a
        /// transform wants. It is deliberately taken from BodyFrame rather than from the tilt,
        /// because BodyFrame already carries transpose(Zup): while some body holds the rotating
        /// frame the whole sky is turned, and even an untilted body's pole is then not world up.
        ///
        /// Equivalent to -angularVelocity.normalized for a body that rotates, but defined for
        /// one that does not.
        /// </summary>
        public static Vector3 WorldNorth(CelestialBody body)
        {
            //Before the body's first CBUpdate its frame is still all zeros; Z would be a zero
            //vector and every FromToRotation built from it garbage.
            if (body == null || !TiltEmFrames.IsUsableRotation(body.BodyFrame)) return Vector3.up;

            return body.BodyFrame.Z.xzy;
        }

        /// <summary>
        /// The normal of the system's orbital plane, in the celestial frame, Unity-swizzled.
        ///
        /// Walks up to the star the body ultimately orbits and takes that star's configured
        /// orbital plane. A star with no plane configured falls back to its own pole, which for
        /// most systems is close enough to the invariable plane to be a sensible default and
        /// costs a pack nothing to leave unset.
        ///
        /// Celestial frame, not world, for the same reason as <see cref="CelestialNorth"/>: the
        /// map camera cancels the sky itself.
        /// </summary>
        public static Vector3 SystemNorth(CelestialBody body)
        {
            var star = StarFor(body);

            if (star == null) return Vector3.up;

            if (TryGetOrbitalPlane(star.bodyName, out var plane)) return plane.Tilt.Z.xzy;

            return CelestialNorth(star);
        }

        /// <summary>
        /// The nearest star at or above the body, or the root of its tree if nothing on the way
        /// up is flagged as one - which is the right answer for a pack that never sets isStar.
        /// </summary>
        private static CelestialBody StarFor(CelestialBody body)
        {
            var current = body;

            //The Sun is its own referenceBody in stock, so "parent is me" is the normal way this
            //terminates. The counter is only there so a malformed tree cannot hang the frame.
            for (var guard = 0; current != null && guard < 64; guard++)
            {
                if (current.isStar) return current;

                var parent = current.referenceBody;
                if (parent == null || ReferenceEquals(parent, current)) return current;

                current = parent;
            }

            return current;
        }

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

#if DEBUG
            GameEvents.onGUIApplicationLauncherReady.Add(EnableToolBar);
            DefineDebugActions();
#endif
        }

        /// <summary>
        /// The map camera rotation toggle.
        ///
        /// V is stock's flight camera-mode key, so this is gated on the map actually being up -
        /// where that binding does nothing - and on CAMERACONTROLS being unlocked, which is what
        /// stock tests before reading any of its own camera keys. Together those keep it off
        /// dialogs, text fields and the loading screen.
        /// </summary>
        // ReSharper disable once UnusedMember.Global
        public void Update()
        {
            if (!Input.GetKeyDown(KeyCode.V)) return;
            if (!MapViewIsUp()) return;
            if (!InputLockManager.IsUnlocked(ControlTypes.CAMERACONTROLS)) return;

            ToggleMapRotation();
        }

        private static bool MapViewIsUp()
        {
            return HighLogic.LoadedScene == GameScenes.TRACKSTATION
                   || (HighLogic.LoadedSceneIsFlight && MapView.MapIsEnabled);
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
        /// The planetarium is rebuilt across scene loads, so the anchor it was captured against
        /// no longer applies. Nothing else needs doing here: the tilt is baked into the frames
        /// themselves now, so there is no per-scene tilt shuffling to perform.
        /// </summary>
        // ReSharper disable once MemberCanBeMadeStatic.Local
        private void SceneRequested(GameEvents.FromToAction<GameScenes, GameScenes> data)
        {
            //Unconditional: the camera reframes on every scene load, so the eased up axis has
            //to start from the new scene's value rather than swing to it.
            ResetMapNorth();

            //Only the destination matters. The old guard also required the *source* to be a
            //real scene, which silently skipped the main-menu to space-centre transition - so on
            //the very first load the anchor was never established.
            if (data.to < GameScenes.SPACECENTER) return;

            ResetZupAnchor();
        }

        #endregion

        #region Public accessors

        /// <summary>
        /// Adds a tilt in the legacy Unity Euler format.
        /// Feel free to call this method from another mod.
        /// </summary>
        public static void AddTiltData(CelestialBody body, Vector3d tilt)
        {
            AddTiltData(body, TiltEmFrames.FromLegacyEuler(tilt));
        }

        /// <summary>
        /// Adds a tilt as an IAU-style pole direction, which is the preferred form.
        /// Feel free to call this method from another mod.
        /// </summary>
        public static void AddTiltData(CelestialBody body, double poleRa, double poleDec)
        {
            AddTiltData(body, TiltEmFrames.FromPole(poleRa, poleDec));
        }

        /// <summary>
        /// Adds an already-built tilt.
        /// </summary>
        public static void AddTiltData(CelestialBody body, BodyTilt tilt)
        {
            if (body == null)
            {
                Debug.LogError("[TiltEm]: AddTiltData parameter 'body' cannot be null!");
                return;
            }

            TiltDictionary[body.bodyName] = tilt;
        }

        /// <summary>
        /// Records a star's orbital plane, as an IAU-style pole direction - the normal of the
        /// plane rather than a direction lying in it.
        /// Feel free to call this method from another mod.
        /// </summary>
        public static void AddOrbitalPlane(CelestialBody body, BodyTilt plane)
        {
            if (body == null)
            {
                Debug.LogError("[TiltEm]: AddOrbitalPlane parameter 'body' cannot be null!");
                return;
            }

            OrbitalPlaneDictionary[body.bodyName] = plane;
        }

        /// <summary>
        /// Returns the given star's orbital plane if one was configured.
        /// </summary>
        public static bool TryGetOrbitalPlane(string bodyName, out BodyTilt plane)
        {
            return OrbitalPlaneDictionary.TryGetValue(bodyName, out plane);
        }

        /// <summary>
        /// Gets the obliquity to display it in a UI for a given body name
        /// </summary>
        public static string GetTiltForDisplay(string bodyName)
        {
            return !TiltDictionary.TryGetValue(bodyName, out var tilt)
                ? "0"
                : KSPUtil.LocalizeNumber(tilt.Obliquity, "F2");
        }

        /// <summary>
        /// Returns the given tilt if found in the storage
        /// </summary>
        public static bool TryGetTilt(string bodyName, out BodyTilt tilt)
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
