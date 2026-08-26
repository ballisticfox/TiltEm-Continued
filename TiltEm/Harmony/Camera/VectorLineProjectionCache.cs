using HarmonyLib;
using System;
using UnityEngine;

// ReSharper disable All

namespace TiltEm.Harmony
{
    /// <summary>
    /// Invalidates KSPCommunityFixes' cached camera projection matrix after each camera update,
    /// so orbit lines are projected from the camera's final pose.
    /// </summary>
    //KSPCF's OptimisedVectorLines caches projectionMatrix * worldToCameraMatrix once per frame,
    //filled lazily by the first VectorLine draw call. If that happens before
    //PlanetariumCamera.LateUpdate moves the camera, every later draw uses the previous frame's
    //pose. Bodies are rendered from the real camera, so orbit lines slide against the planets.
    //TiltEm makes this constant rather than occasional: CBUpdate rewrites Planetarium.Rotation
    //every FixedUpdate while a body holds the rotating frame, so the camera pose changes on
    //essentially every frame, not only while the player is dragging.
    //
    //Soft dependency: if KSPCF is absent, OptimisedVectorLines is off, or the internals are
    //renamed, the accessor stays null and every path here is a no-op.
    internal static class VectorLineProjectionCache
    {
        private const string CacheType = "KSPCommunityFixes.Performance.VectorLineCameraProjection";
        private const string CacheField = "lastCachedFrame";

        /// <summary>Whether to drop the cache after a camera update.</summary>
        //Redundant once KSPCF scopes its cache to a draw call rather than a frame; this file
        //goes away once such a build is the one people have.
        internal const bool Enabled = true;

        private static AccessTools.FieldRef<long> _lastCachedFrame;
        private static bool _resolved;

        /// <summary>Marks the cache stale; long.MinValue can never equal UpdateCount.</summary>
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

    /// <summary>Invalidates the cache after the map and tracking-station camera settles.</summary>
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

    /// <summary>Invalidates the cache after the flight scene's scaled-space camera settles.</summary>
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
