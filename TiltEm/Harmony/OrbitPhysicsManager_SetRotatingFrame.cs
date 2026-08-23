using HarmonyLib;
using TiltEm.Event;

// ReSharper disable All

namespace TiltEm.Harmony
{
    /// <summary>
    /// Latches the planetarium anchor as a body enters its rotating frame, and fires the
    /// mod's own notification event.
    ///
    /// Anchoring here rather than in CBUpdate means it happens before stock touches any
    /// rigidbody velocities, so the frame Zup will be rebuilt around is the one that was in
    /// effect for the whole preceding inertial stretch. CBUpdate re-checks anyway
    /// (see TiltEm.EnsureZupAnchor) because Kopernicus can flip inverseRotation without
    /// routing through setRotatingFrame.
    ///
    /// Note there is deliberately nothing here that moves vessels. The frames are continuous
    /// across the switch by construction now, so there is nothing to compensate for; the
    /// previous version's GoOnRails/SetPosition/OrbitFrame fix-up was compensating for a
    /// discontinuity that no longer exists.
    /// </summary>
    [HarmonyPatch(typeof(OrbitPhysicsManager))]
    [HarmonyPatch("setRotatingFrame")]
    internal class OrbitPhysicsManager_SetRotatingFrame
    {
        [HarmonyPrefix]
        private static void PrefixSetRotatingFrame(OrbitPhysicsManager __instance, bool rotatingFrameState)
        {
            var dominantBody = __instance.dominantBody;

            if (rotatingFrameState && dominantBody != null)
            {
                if (!TiltEm.TryGetTilt(dominantBody.bodyName, out var tilt))
                {
                    tilt = TiltEmFrames.Untilted;
                }

                TiltEm.EnsureZupAnchor(dominantBody, tilt);
            }

            RotatingFrameEvents.beforeRotatingFrameChange.Fire(
                new GameEvents.HostTargetAction<CelestialBody, bool>(dominantBody, rotatingFrameState));
        }
    }
}
