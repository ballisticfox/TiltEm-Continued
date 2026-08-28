# Tilt'Em — Profiler Markers

Every region of the mod that costs measurable time raises a Unity profiler
marker, so a capture attributes frametime to the work by name instead of
leaving it inside `BehaviourUpdate` or whichever stock method the patch hangs
off. The markers are declared in one table, `TiltEm/Profiling/TiltEmProfiler.cs`,
and every name is prefixed `TiltEm.` so a capture can be filtered to this mod.

Markers ship in every build. One that is not being recorded costs a native call
that returns immediately, so there is nothing to switch on before taking a
capture and no separate build that measures something a player's build would
not.

---

## What is instrumented

| Marker | Site | When it runs |
|---|---|---|
| `TiltEm.CBUpdate` | `CelestialBody_CBUpdate` | Every body, every physics tick |
| `TiltEm.CBUpdate.Rotation` | ↳ `UpdateRotation` | Body frame built from the pole |
| `TiltEm.CBUpdate.Planetarium` | ↳ `UpdatePlanetariumFrame` | Sky turned while this body holds the rotating frame |
| `TiltEm.CBUpdate.Orbit` | ↳ `orbitDriver.UpdateOrbit` | Stock's orbit update, which the prefix now calls |
| `TiltEm.ZupAtT` | `Planetarium_ZupAtT` | Any orbit evaluation at an arbitrary time |
| `TiltEm.MapCameraPivot` | `PlanetariumCamera_LateUpdate` | Up to twice a frame with a map camera up |
| `TiltEm.GetFoR` | `FlightGlobals_GetFoR` | Per frame in flight |
| `TiltEm.SetDominantBody` | `OrbitPhysicsManager_SetDominantBody` | Sphere-of-influence change |
| `TiltEm.SetRotatingFrame` | `OrbitPhysicsManager_SetRotatingFrame` | Threshold crossing |
| `TiltEm.ManeuverNodeElements` | `ManeuverNodeEditorTabOrbitAdv_UpdateUIElements` | While the tab is open, throttled by stock's `updateCooldownSeconds` |
| `TiltEm.DebugUi.Bodies` | `BodiesScreen` | Per frame while the screen is up |
| `TiltEm.DebugUi.Frames` | `FramesScreen` | Per frame while the screen is up |
| `TiltEm.DebugUi.Vessel` | `VesselScreen` | Per frame while the screen is up |
| `TiltEm.Editor.TiltTab` | `TiltEditorScreen` | Per frame while the tab is up |
| `TiltEm.Editor.OrbitTab` | `OrbitEditorScreen` | Per frame while the tab is up |
| `TiltEm.AxisRenderer` | `TiltAxisRenderer` | Per frame while the axes are drawn |
| `TiltEm.PlaneNormalRenderer` | `PlaneNormalRenderer` | Per frame while the arrow is drawn |
| `TiltEm.EditorHandles` | `EditorHandles` | Per frame while the editor is open |
| `TiltEm.Load.Tilts` | `TiltLoader` | Once, as the space centre loads |
| `TiltEm.Load.OrbitFrames` | `OrbitFrameLoader` | Once, on Kopernicus's finished prefab |
| `TiltEm.Editor.Export` | `EditExporter` | When the player exports a config |

`TiltEm.ZupAtT` is the highest-frequency marker by a wide margin, and the only
one that appears under several different parents. Stock reaches it from exactly
one place, `Orbit.GetOrbitalStateVectorsAtTrueAnomaly` with `worldToLocal` set,
but that sits under `Orbit.UpdateFromUT` and under
`GetOrbitalStateVectorsAtUT`, which between them are called by every orbit
update in the game and by the patched conic solver. Expect thousands of samples
in a frame where the solver is working.

## Where they appear in a capture

They do not all hang off one node, so searching `TiltEm` in the Profiler's
Hierarchy view is a better way to collect them than opening any single parent.
Every entry below was traced through the decompiled 1.12.5 sources.

**`ScriptRunBehaviourFixedUpdate`**

- `Planetarium.FixedUpdate` → `UpdateCBsRecursive` → `CelestialBody.CBUpdate`.
  This is where `TiltEm.CBUpdate` and its three children live, once per body per
  tick, and with them the share of `TiltEm.ZupAtT` that `CBUpdate.Orbit` reaches
  through `OrbitDriver.UpdateOrbit` → `Orbit.UpdateFromUT`.
- `OrbitPhysicsManager.FixedUpdate` → `checkReferenceFrame` →
  `TiltEm.SetDominantBody` and `TiltEm.SetRotatingFrame`. A different component
  from Planetarium, so a different line in the capture.
- `OrbitDriver.FixedUpdate` and `VesselPrecalculate.FixedUpdate` →
  `MainPhysics`, both of which call `UpdateOrbit` for *vessels* rather than
  bodies, and `CalculateGravity`'s drift compensation, which calls
  `GetOrbitalStateVectorsAtUT`. All three raise `TiltEm.ZupAtT`.

**`ScriptRunBehaviourUpdate`**

- `TiltEm.DebugUi.Bodies`, `.Frames`, `.Vessel`, `TiltEm.Editor.TiltTab` and
  `TiltEm.Editor.OrbitTab`, each under its own screen component.
- `ManeuverNodeEditorTab.Update` → `WrapperUpdateUIElements` →
  `TiltEm.ManeuverNodeElements`.
- `TiltEm.Editor.Export`, which a button click reaches through the event system.
- `TiltEm.ZupAtT` again, wherever the patched conic solver runs.

**`ScriptRunBehaviourLateUpdate`**

- `PlanetariumCamera.LateUpdate` → `TiltEm.MapCameraPivot`.
- `FlightCamera.LateUpdate` → `GetCameraFoR` → `TiltEm.GetFoR`.
- `TiltEm.AxisRenderer`, `TiltEm.PlaneNormalRenderer` and
  `TiltEm.EditorHandles`, each under its own component.

**Scene load**, not a steady-state frame: `TiltEm.Load.Tilts` under
`TiltLoader.Awake` as the space centre loads, and `TiltEm.Load.OrbitFrames`
under Kopernicus's `OnPostLoad`.

`CelestialBody.CBUpdate` has callers outside `Planetarium.FixedUpdate` as well;
`Planetarium.UpdateCBs`, `FlightDriver`, `PSystemSetup` and the Making History
mission systems all call it directly. Those are one-off, so `TiltEm.CBUpdate`
can appear on a load or a scene change under something other than Planetarium.

## What is deliberately not instrumented

A marker costs about as much as a handful of arithmetic, so timing a handful of
arithmetic mostly measures the marker. Left bare for that reason:

- `UpdateMassAndGravity` and `UpdateSolarDayLength` inside CBUpdate.
- `VectorLineProjectionCache.Invalidate`, which writes one field.
- `FlightGlobals_SetShipOrbit` and the knowledge base row, both of which run
  once when a player opens something rather than per frame.
- `TiltEmFrames`, `EditExport` and `HandleAxes`. These three compile into the
  test project as well as the mod, and the shim build has no Unity assemblies
  to resolve `Unity.Profiling` against; their cost shows up under whichever
  caller's marker reached them.

## Taking a capture

The markers are raised through `ProfilerMarker.Auto()`, not through
`Profiler.BeginSample` or `ProfilerMarker.Begin`/`End`. Those alternatives
carry `[Conditional("ENABLE_PROFILER")]`, evaluated where the *call* is
compiled. Unity defines that symbol; this assembly does not, so those calls
compile away to nothing. `Auto()` has no such attribute.

Stock KSP 1.12.5 records them. `UnityPlayer.dll` has the native profiler
machinery (`ProfilerMarker`'s bindings, `PlayerConnection`, the
`profiler-log-file` and `profiler-enable-deep-profiling-support` argument
keys), so no development build or launch flag is needed.

## A baseline

From a 299-frame flight capture on the stock system, in milliseconds per frame
(the total across all calls in that frame, not per call):

| Marker | Median | Mean | Max | Calls/frame |
|---|---|---|---|---|
| `TiltEm.CBUpdate` | 0.19 | 0.21 | 0.45 | 17 |
| ↳ `CBUpdate.Orbit` | 0.07 | 0.08 | 0.26 | 16 |
| ↳ `CBUpdate.Rotation` | 0.06 | 0.07 | 0.26 | 17 |
| ↳ `CBUpdate.Planetarium` | 0.01 | 0.01 | 0.01 | 17 |
| `TiltEm.ZupAtT` | 0.00 | 0.01 | 0.13 | 29 |
| `TiltEm.GetFoR` | 0.00 | 0.00 | 0.00 | 1 |

Seventeen calls a frame is the stock system's seventeen bodies, once each per
physics tick. The whole mod costs about 0.2 ms a frame, near enough 1% of a
60 fps budget; roughly a third of that is stock's orbit update that the prefix
now calls on its behalf.

That capture does *not* cover the map view with a maneuver node, where the
patched conic solver drives `ZupAtT` far harder than flight does, or the
tracking station with the body editor open. Those are where a regression would
most likely show.
