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
