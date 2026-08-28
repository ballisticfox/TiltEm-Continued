using HarmonyLib;
using KSP.Localization;
using System;
using UnityEngine;

// ReSharper disable All

namespace TiltEm.Harmony
{
    /// <summary>
    /// Shared state for the two halves of the System camera mode.
    /// </summary>
    internal static class SystemCameraMode
    {
        /// <summary>True while this patch is driving setMode itself.</summary>
        //setMode is where a mode change gets noticed, and re-entering Orbital to take the system
        //frame is a mode change like any other. This is what tells the two apart.
        public static bool Switching;
    }

    /// <summary>
    /// Adds a System step to the flight camera's mode cycle, straight after Orbital. It frames on
    /// the plane the vessel's system orbits in rather than on its body's pole, and is otherwise
    /// the orbital camera.
    /// </summary>
    //FlightCamera.Modes cannot grow a sixth value, so the mode stays Orbital and
    //FlightCameraFrame.SystemUp is what distinguishes it. Only the missing step is
    //taken over; every other transition is left to stock.
    [HarmonyPatch(typeof(FlightCamera))]
    [HarmonyPatch("SetNextMode")]
    internal class FlightCamera_SetNextMode
    {
        [HarmonyPrefix]
        private static bool PrefixSetNextMode(FlightCamera __instance)
        {
            if (SystemCameraMode.Switching) return true;
            if (!InputLockManager.IsUnlocked(ControlTypes.CAMERAMODES)) return true;

            //Orbital and not already in System: the one place the extra step goes. Coming round
            //again from System falls through to stock, which advances to Chase.
            if (__instance.mode != FlightCamera.Modes.ORBITAL || FlightCameraFrame.SystemUp) return true;

            //Stock's own fixup, repeated because stock is not going to run.
            if (__instance.targetMode != FlightCamera.TargetMode.Vessel
                || __instance.vesselTarget != FlightGlobals.ActiveVessel)
            {
                __instance.SetTargetVessel(FlightGlobals.ActiveVessel);
            }

            SystemCameraMode.Switching = true;

            try
            {
                FlightCameraFrame.SystemUp = true;

                //Re-entered rather than left alone: setMode is what resets the frame lerp, so
                //going through it makes the camera swing to the new plane instead of cutting.
                __instance.setMode(FlightCamera.Modes.ORBITAL);
            }
            finally
            {
                SystemCameraMode.Switching = false;
            }

            return false;
        }
    }

    /// <summary>
    /// Drops the System frame whenever the player picks any other mode, and names it on screen
    /// when they pick it.
    /// </summary>
    [HarmonyPatch(typeof(FlightCamera))]
    [HarmonyPatch("setMode")]
    [HarmonyPatch(new Type[] { typeof(FlightCamera.Modes) })]
    internal class FlightCamera_setMode
    {
        /// <summary>The mode's name, in the caps the stock modes are written in.</summary>
        //Falls back to English if a localisation for the tag is missing, the way the obliquity
        //row does. Stock's own names come through #autoLOC_6003102 and friends, all uppercase.
        private static string Name =>
            Localizer.TryGetStringByTag("#autoLOC_TiltEm_CameraSystem", out string localised)
                ? localised
                : "SYSTEM";

        /// <summary>The message stock has just posted naming the mode.</summary>
        private static readonly AccessTools.FieldRef<FlightCamera, ScreenMessage> Readout =
            AccessTools.FieldRefAccess<FlightCamera, ScreenMessage>("cameraModeReadout");

        /// <summary>The mode in force before this call, which setMode overwrites immediately.</summary>
        [HarmonyPrefix]
        private static void PrefixSetMode(FlightCamera __instance, out FlightCamera.Modes __state)
        {
            __state = __instance.mode;
        }

        [HarmonyPostfix]
        private static void PostfixSetMode(FlightCamera __instance, FlightCamera.Modes m,
            FlightCamera.Modes __state)
        {
            if (SystemCameraMode.Switching)
            {
                Relabel(__instance);
                return;
            }

            //Same mode reasserted while System is active is a restore (e.g. returning from
            //map view), not a mode change; dropping the frame here would lose it silently.
            if (m == __state && FlightCameraFrame.SystemUp)
            {
                Relabel(__instance);
                return;
            }

            //Anything else is the player choosing a different mode: the camera key, Auto
            //resolving itself, or a vessel restoring a mode that is not this one.
            FlightCameraFrame.SystemUp = false;
        }

        /// <summary>Replaces stock's "Orbital" readout with the name of the mode actually taken.</summary>
        //Rewritten and re-posted, never removed first. Removing breaks PostMessage's
        //in-place rewrite of the existing ScreenMessage, causing a duplicate line.
        private static void Relabel(FlightCamera camera)
        {
            ScreenMessage readout = Readout(camera);

            if (readout == null) return;

            //#autoLOC_133776 is "Camera: <<1>>", the same wrapper stock puts every mode name in.
            readout.message = Localizer.Format("#autoLOC_133776", Name);

            ScreenMessages.PostScreenMessage(readout);
        }
    }
}
