using HarmonyLib;
using System;
using UnityEngine;

// ReSharper disable All

namespace TiltEm.Harmony
{
    /// <summary>
    /// Drops KSPCommunityFixes' cached camera projection matrix the moment a camera finishes
    /// moving, so orbit lines drawn later in the same frame are projected from where the camera
    /// actually is.
    ///
    /// The problem is not ours, but it is ours to trip over. KSPCF's OptimisedVectorLines
    /// transpiles Vectrosity's Camera.WorldToScreenPoint / WorldToViewportPoint /
    /// ScreenToWorldPoint into a managed reimplementation that caches
    ///
    ///     projectionMatrix * camera.worldToCameraMatrix
    ///
    /// once per frame. The cache is keyed on KSPCommunityFixes.UpdateCount, which is bumped in
    /// an ordinary Update, and it is filled LAZILY - by the first line projection of the frame,
    /// whoever that turns out to be.
    ///
    /// Every Vectrosity consumer in KSP draws from its own LateUpdate: OrbitRendererBase,
    /// OrbitTargeter, PatchRendering, CommNetUI, MapView. Unity does not define an order among
    /// components that share an execution order, and all of these do. So on any given frame the
    /// first of them to run pins the projection matrix for every line drawn afterwards - and if
    /// that happens before PlanetariumCamera.LateUpdate (map, tracking station) or
    /// ScaledCamera.LateUpdate (flight), the matrix it pins is the previous frame's camera pose.
    ///
    /// In the map view that is what happens. Something reaches VectorLine.Draw3D before the
    /// camera moves and fills the cache from the pose the camera is leaving; the orbit lines
    /// themselves come later, through VectorLine.Draw, and never refill it because the frame key
    /// is current by then. They are drawn from a camera pose up to a frame's worth of panning
    /// out of date.
    ///
    /// Stock Vectrosity re-reads the camera on every point, so a line drawn after the camera
    /// moved is projected correctly whatever ran before it. With the cache, one early consumer
    /// makes every later one stale. The bodies are rendered by the GPU from the real camera, so
    /// the orbit lines slide against the planets by however far the camera moved that frame:
    /// nothing while still, a visible wobble while moving.
    ///
    /// Tilt'Em is what makes it constant rather than occasional. The map camera's orientation is
    /// built from Planetarium.Rotation, which CBUpdate rewrites every FixedUpdate for as long as
    /// any body holds its rotating frame - so the camera pose changes on essentially every frame
    /// once a vessel is below a threshold, not only while the player is dragging.
    ///
    /// The fix is one store. Invalidating after the camera settles costs one extra matrix
    /// capture per frame and makes the lazy fill land after the pose is final instead of before
    /// it. Lines drawn earlier in the frame keep the previous pose, which is exactly what they
    /// would have got from stock Vectrosity reading the camera live at that same moment.
    ///
    /// Soft dependency throughout: if KSPCF is absent, or OptimisedVectorLines is turned off, or
    /// the internals are renamed, the accessor stays null and every path here is a no-op.
    /// </summary>
    internal static class VectorLineProjectionCache
    {
        private const string CacheType = "KSPCommunityFixes.Performance.VectorLineCameraProjection";
        private const string CacheField = "lastCachedFrame";

        /// <summary>
        /// Whether to drop the cache after a camera update.
        ///
        /// Redundant against a KSPCommunityFixes build whose camera projection cache is scoped to
        /// a draw call rather than to a frame: there the fill already lands after the cameras have
        /// moved and there is nothing to drop. This whole file goes with it once such a build is
        /// the one people have.
        /// </summary>
        internal const bool Enabled = true;

        private static AccessTools.FieldRef<long> _lastCachedFrame;
        private static bool _resolved;

        /// <summary>
        /// Marks the cache stale. Any value that cannot equal KSPCommunityFixes.UpdateCount does
        /// the job, and long.MinValue cannot: UpdateCount only ever counts up from zero.
        /// </summary>
        internal static void Invalidate()
        {
            if (!_resolved) Resolve();

            if (_lastCachedFrame == null) return;

            _lastCachedFrame() = long.MinValue;
        }

        private static void Resolve()
        {
            _resolved = true;

            var type = AccessTools.TypeByName(CacheType);
            if (type == null) return;

            var field = AccessTools.DeclaredField(type, CacheField);
            if (field == null || !field.IsStatic || field.FieldType != typeof(long))
            {
                Debug.Log("[TiltEm]: found " + CacheType + " but not a static long " + CacheField
                          + "; leaving its projection cache alone.");
                return;
            }

            try
            {
                _lastCachedFrame = AccessTools.StaticFieldRefAccess<long>(field);
                Debug.Log("[TiltEm]: KSPCommunityFixes' vector line projection cache will be "
                          + "invalidated after each camera update.");
            }
            catch (Exception e)
            {
                Debug.LogWarning("[TiltEm]: could not bind " + CacheType + "." + CacheField
                                 + "; orbit lines may lag the camera by a frame. " + e.Message);
            }
        }
    }

    /// <summary>
    /// The map and tracking station camera. This is the one that matters for orbit lines, since
    /// MapView.enterMapView disables ScaledCamera and PlanetariumCamera.Camera becomes the
    /// scaled-space camera itself.
    /// </summary>
    [HarmonyPatch(typeof(PlanetariumCamera))]
    [HarmonyPatch("LateUpdate")]
    internal class PlanetariumCamera_LateUpdate_CacheReset
    {
        [HarmonyPrepare]
        private static bool Prepare() => VectorLineProjectionCache.Enabled;

        //Ordered after the transpiler in PlanetariumCamera_LateUpdate by construction: a postfix
        //runs once the method body, transpiled or not, has finished.
        [HarmonyPostfix]
        private static void PostfixLateUpdate()
        {
            VectorLineProjectionCache.Invalidate();
        }
    }

    /// <summary>
    /// The flight scene's scaled-space camera, which follows FlightCamera rather than being
    /// driven directly. Enabled outside the map view, where orbit lines are still drawn while
    /// MapView.updateMap is set.
    /// </summary>
    [HarmonyPatch(typeof(ScaledCamera))]
    [HarmonyPatch("LateUpdate")]
    internal class ScaledCamera_LateUpdate_CacheReset
    {
        [HarmonyPrepare]
        private static bool Prepare() => VectorLineProjectionCache.Enabled;

        [HarmonyPostfix]
        private static void PostfixLateUpdate()
        {
            VectorLineProjectionCache.Invalidate();
        }
    }
}
