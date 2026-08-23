using HarmonyLib;
using System;
using KSP.UI.Screens;
using UnityEngine;

// ReSharper disable All

namespace TiltEm.Harmony
{
    /// <summary>
    /// Fixes the map / tracking-station camera for tilted bodies.
    ///
    /// Stock orients the camera pivot with
    ///
    ///     Quaternion.AngleAxis(camHdg * Rad2Deg + (float)Planetarium.InverseRotAngle, Vector3.up)
    ///
    /// The InverseRotAngle term is there to cancel the rotating frame: while a body holds it,
    /// Zup turns the whole sky, and without that term the camera would be dragged around with
    /// it. Adding a scalar about Vector3.up is only the right cancellation because stock's Zup
    /// is always a plain spin about the celestial +Z axis.
    ///
    /// With a tilted body the sky instead turns about that body's pole, so the scalar is both
    /// the wrong axis and, once the anchored form is in use, no longer even the right angle.
    /// The camera then drifts and jitters whenever any craft is below some body's
    /// inverseRotThresholdAltitude - including a body you are not looking at.
    ///
    /// Planetarium.Rotation already *is* the exact celestial-to-world map, so using it directly
    /// cancels the sky exactly, whatever shape Zup has taken. It reduces to stock identically:
    /// when Zup is Rz(IRA), Planetarium.Rotation is a rotation of +IRA about Vector3.up, so
    /// Planetarium.Rotation * AngleAxis(camHdg) == AngleAxis(camHdg + IRA).
    ///
    /// Second change: the camera's up is the focused body's north pole rather than the
    /// celestial +Z axis, so looking at a tilted planet shows it upright rather than leaning.
    /// FromToRotation is the identity for an untilted body, so stock framing is untouched there.
    /// </summary>
    [HarmonyPatch(typeof(PlanetariumCamera))]
    [HarmonyPatch("LateUpdate")]
    internal class PlanetariumCamera_LateUpdate
    {
        private static readonly AccessTools.FieldRef<PlanetariumCamera, bool> ExternalControl =
            AccessTools.FieldRefAccess<PlanetariumCamera, bool>("externalControl");

        /// <summary>
        /// AlarmClockApp.AppFrameHasLock is internal, so it is bound once as a delegate rather
        /// than reflected on every frame. Degrades to "no lock" if it ever disappears, which
        /// only costs the correction on the frames the alarm clock holds focus.
        /// </summary>
        private static readonly Func<bool> AlarmClockHasLock = BindAlarmClockLock();

        private static Func<bool> BindAlarmClockLock()
        {
            var method = AccessTools.Method(typeof(AlarmClockApp), "AppFrameHasLock");

            if (method == null)
            {
                Debug.LogWarning("[TiltEm]: AlarmClockApp.AppFrameHasLock not found; map camera "
                                 + "correction will skip alarm-clock-focused frames.");
                return () => false;
            }

            return (Func<bool>)Delegate.CreateDelegate(typeof(Func<bool>), method);
        }

        [HarmonyPostfix]
        private static void PostfixLateUpdate(PlanetariumCamera __instance)
        {
            if (__instance.target == null || FlightDriver.Pause) return;
            if (!StockWouldHaveSetOrientation(__instance)) return;

            var pivot = __instance.GetPivot();
            if (pivot == null) return;

            //Exactly what stock builds, with the two substitutions described above.
            var endRot = (Quaternion)Planetarium.Rotation
                         * Quaternion.FromToRotation(Vector3.up, GetNorth(__instance.target))
                         * Quaternion.AngleAxis(__instance.camHdg * Mathf.Rad2Deg, Vector3.up);

            pivot.rotation = endRot;
            pivot.Rotate(__instance.GetCameraTransform().right, __instance.camPitch * Mathf.Rad2Deg, Space.World);
        }

        /// <summary>
        /// Mirrors the two branches in LateUpdate that assign the pivot rotation. If neither ran
        /// - something else is driving the camera - the pivot is left alone.
        /// </summary>
        private static bool StockWouldHaveSetOrientation(PlanetariumCamera camera)
        {
            if (InputLockManager.IsAllLocked(ControlTypes.All) || AlarmClockHasLock())
            {
                return true;
            }

            return InputLockManager.IsUnlocked(ControlTypes.CAMERACONTROLS) && !ExternalControl(camera);
        }

        /// <summary>
        /// The focused body's north pole, in the celestial frame - not the world frame. The
        /// distinction matters here and is spelled out on <see cref="TiltEm.CelestialNorth"/>:
        /// this patch cancels the sky itself with Planetarium.Rotation, so a world pole would
        /// apply Zup a second time. The in-flight camera, which does no such cancellation, wants
        /// <see cref="TiltEm.WorldNorth"/> instead.
        /// </summary>
        private static Vector3 GetNorth(MapObject target)
        {
            var body = target.celestialBody;

            if (body == null && target.vessel != null)
            {
                body = target.vessel.mainBody;
            }

            return TiltEm.CelestialNorth(body);
        }
    }
}
