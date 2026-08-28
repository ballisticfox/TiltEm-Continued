using Unity.Profiling;

namespace TiltEm
{
    /// <summary>
    /// Every profiler marker the mod raises, in one table, so a capture can be read against a
    /// list rather than searched. Each name is prefixed "TiltEm." and matches the patch or the
    /// component it times.
    /// </summary>
    //Markers ship in every build rather than behind a define. One that is not being recorded
    //costs a native call that returns immediately, so there is nothing to switch on before
    //taking a capture, and no build that measures something a player's build would not.
    //See Docs/PROFILING.md.
    internal static class TiltEmProfiler
    {
        #region Per-tick frame work

        /// <summary>The CBUpdate prefix, which runs for every body on every physics tick.</summary>
        internal static readonly TiltEmMarker CbUpdate = Marker("CBUpdate");

        /// <summary>Building the body frame from its pole, inside CBUpdate.</summary>
        internal static readonly TiltEmMarker CbUpdateRotation = Marker("CBUpdate.Rotation");

        /// <summary>Turning the sky while this body holds the rotating frame, inside CBUpdate.</summary>
        internal static readonly TiltEmMarker CbUpdatePlanetarium = Marker("CBUpdate.Planetarium");

        /// <summary>Stock's orbit update, which the prefix took over responsibility for calling.</summary>
        internal static readonly TiltEmMarker CbUpdateOrbit = Marker("CBUpdate.Orbit");

        /// <summary>The planetarium frame at an arbitrary time.</summary>
        internal static readonly TiltEmMarker ZupAtT = Marker("ZupAtT");

        #endregion

        #region Cameras

        /// <summary>Orienting the map and tracking-station camera pivot, up to twice a frame.</summary>
        internal static readonly TiltEmMarker MapCameraPivot = Marker("MapCameraPivot");

        /// <summary>Re-basing the in-flight camera frame onto the body's pole.</summary>
        internal static readonly TiltEmMarker GetFoR = Marker("GetFoR");

        #endregion

        #region Frame handovers

        /// <summary>Ending the outgoing body's rotating frame at a dominant-body change.</summary>
        internal static readonly TiltEmMarker SetDominantBody = Marker("SetDominantBody");

        /// <summary>Latching the planetarium anchor as a body enters its rotating frame.</summary>
        internal static readonly TiltEmMarker SetRotatingFrame = Marker("SetRotatingFrame");

        #endregion

        #region Readouts, redrawn every frame they are on screen

        /// <summary>Rewriting the maneuver node editor's orientation elements.</summary>
        internal static readonly TiltEmMarker ManeuverNodeElements = Marker("ManeuverNodeElements");

        /// <summary>The debug menu's per-body table, rebuilt in full each frame.</summary>
        internal static readonly TiltEmMarker DebugUiBodies = Marker("DebugUi.Bodies");

        /// <summary>The debug menu's frames screen.</summary>
        internal static readonly TiltEmMarker DebugUiFrames = Marker("DebugUi.Frames");

        /// <summary>The debug menu's vessel screen.</summary>
        internal static readonly TiltEmMarker DebugUiVessel = Marker("DebugUi.Vessel");

        /// <summary>The tilt editor's tab.</summary>
        internal static readonly TiltEmMarker EditorTiltTab = Marker("Editor.TiltTab");

        /// <summary>The orbit editor's tab.</summary>
        internal static readonly TiltEmMarker EditorOrbitTab = Marker("Editor.OrbitTab");

        #endregion

        #region Drawing aids, per frame while they are shown

        /// <summary>Placing the focused body's drawn axes.</summary>
        internal static readonly TiltEmMarker AxisRenderer = Marker("AxisRenderer");

        /// <summary>Aiming the orbital plane normal arrow.</summary>
        internal static readonly TiltEmMarker PlaneNormalRenderer = Marker("PlaneNormalRenderer");

        /// <summary>Placing the editor's drag rings and testing the pointer against them.</summary>
        internal static readonly TiltEmMarker EditorHandles = Marker("EditorHandles");

        #endregion

        #region Off the frametime path

        /// <summary>Reading every body's configured tilt as the space centre loads.</summary>
        internal static readonly TiltEmMarker LoadTilts = Marker("Load.Tilts");

        /// <summary>Rebasing parent-relative orbits on Kopernicus's finished system prefab.</summary>
        internal static readonly TiltEmMarker LoadOrbitFrames = Marker("Load.OrbitFrames");

        /// <summary>Writing an edited body out as a Kopernicus patch.</summary>
        internal static readonly TiltEmMarker EditorExport = Marker("Editor.Export");

        #endregion

        private static TiltEmMarker Marker(string name)
        {
            return new TiltEmMarker("TiltEm." + name);
        }
    }

    /// <summary>One named region, timed for as long as a using block holds it.</summary>
    //Wrapped rather than used bare so that the choice of profiler API is made once. That choice
    //is load-bearing: Profiler.BeginSample, and ProfilerMarker's own Begin and End, all carry
    //[Conditional("ENABLE_PROFILER")], which is evaluated where the CALL is compiled. Unity
    //defines that symbol; this assembly is not built by Unity and does not, so every one of
    //those calls would compile away to nothing and a capture would come back empty. Auto()
    //carries no such attribute and is emitted.
    internal struct TiltEmMarker
    {
        private readonly ProfilerMarker _marker;

        internal TiltEmMarker(string name) => _marker = new ProfilerMarker(name);

        /// <summary>Times the enclosing using block.</summary>
        internal ProfilerMarker.AutoScope Sample()
        {
            return _marker.Auto();
        }
    }
}
