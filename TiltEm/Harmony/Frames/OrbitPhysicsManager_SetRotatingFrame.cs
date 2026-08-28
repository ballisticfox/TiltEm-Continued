using HarmonyLib;
using TiltEm.Event;

// ReSharper disable All

namespace TiltEm.Harmony
{
    /// <summary>
    /// Latches the planetarium anchor as a body enters its rotating frame, and raises the mod's
    /// own notification event. See section 5.4 of Docs/TILT_MATHEMATICS.pdf.
    /// </summary>
    //Anchoring here rather than in CBUpdate puts it before stock touches rigidbody velocities.
    //CBUpdate re-checks anyway (EnsureZupAnchor) because Kopernicus can set inverseRotation
    //without routing through setRotatingFrame.
    [HarmonyPatch(typeof(OrbitPhysicsManager))]
    [HarmonyPatch("setRotatingFrame")]
    internal class OrbitPhysicsManager_SetRotatingFrame
    {
        [HarmonyPrefix]
        private static void PrefixSetRotatingFrame(OrbitPhysicsManager __instance, bool rotatingFrameState)
        {
            using (TiltEmProfiler.SetRotatingFrame.Sample())
            {
                CelestialBody dominantBody = __instance.dominantBody;

                if (rotatingFrameState && dominantBody != null)
                {
                    if (!TiltEm.TryGetTilt(dominantBody.bodyName, out BodyTilt tilt))
                    {
                        tilt = TiltEmFrames.Untilted;
                    }

                    PlanetariumAnchor.EnsureZupAnchor(dominantBody, tilt);
                }

                RotatingFrameEvents.beforeRotatingFrameChange.Fire(
                    new GameEvents.HostTargetAction<CelestialBody, bool>(dominantBody, rotatingFrameState));
            }
        }
    }
}
