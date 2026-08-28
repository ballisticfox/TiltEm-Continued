using HarmonyLib;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using UnityEngine;

// ReSharper disable All

namespace TiltEm.Harmony
{
    /// <summary>
    /// Orients the map and tracking-station camera pivot around the focused body's pole or
    /// its star's orbital plane normal, depending on the player's chosen mode.
    /// See section 3.4 of Docs/TILT_MATHEMATICS.pdf.
    /// </summary>
    //Stock orients the pivot with AngleAxis(camHdg + InverseRotAngle, Vector3.up), which only
    //works because stock's Zup is a plain spin about celestial +Z. A tilted body turns the sky
    //about its own pole, so the scalar is the wrong axis. This substitutes Planetarium.Rotation,
    //which cancels the sky whatever shape Zup has, and adds a FromToRotation to point the
    //camera's up at the focused body's pole.
    //
    //Transpiled rather than postfixed: stock does four more things with the pivot after setting
    //it (pitch sampling, distance lerp, localRotation rebuild, smooth-damp), and a postfix
    //would sample the pitch axis a step late and move the camera after LateUpdate finished.
    [HarmonyPatch(typeof(PlanetariumCamera))]
    [HarmonyPatch("LateUpdate")]
    internal class PlanetariumCamera_LateUpdate
    {
        /// <summary>Stock assigns endRot via AngleAxis in exactly two branches.</summary>
        private const int ExpectedSites = 2;

        private static int _northFrame = -1;
        private static Vector3 _north;

        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> TranspileLateUpdate(IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo stock = AccessTools.Method(typeof(Quaternion), nameof(Quaternion.AngleAxis),
                new[] { typeof(float), typeof(Vector3) });
            MethodInfo tilted = AccessTools.Method(typeof(PlanetariumCamera_LateUpdate), nameof(PivotRotation),
                new[] { typeof(float), typeof(Vector3), typeof(PlanetariumCamera) });

            var code = new List<CodeInstruction>(instructions);

            if (stock == null || tilted == null)
            {
                Debug.LogError("[TiltEm]: could not resolve the map camera transpiler targets; "
                               + "the map camera will use stock's untilted framing.");
                return code;
            }

            int replaced = 0;

            for (int i = 0; i < code.Count; i++)
            {
                if (!code[i].Calls(stock)) continue;

                code.Insert(i, Redirect(code[i], tilted));
                i++;
                replaced++;
            }

            if (replaced != ExpectedSites)
            {
                Debug.LogWarning("[TiltEm]: patched " + replaced + " of " + ExpectedSites
                                 + " map camera orientation sites. The map camera may show tilted "
                                 + "bodies leaning, or drift while a vessel is below a rotating-frame "
                                 + "threshold.");
            }

            return code;
        }

        /// <summary>Repoints one stock call at the replacement, pushing the camera as a third argument.</summary>
        private static CodeInstruction Redirect(CodeInstruction call, MethodInfo replacement)
        {
            var loadCamera = new CodeInstruction(OpCodes.Ldarg_0);

            //A call mid-expression cannot be a branch target, but labels and exception blocks are
            //cheap to carry across and expensive to lose.
            loadCamera.labels.AddRange(call.labels);
            call.labels.Clear();
            loadCamera.blocks.AddRange(call.blocks);
            call.blocks.Clear();

            call.opcode = OpCodes.Call;
            call.operand = replacement;

            return loadCamera;
        }

        /// <summary>Stands in for stock's AngleAxis at both endRot sites.</summary>
        //The angle handed in is camHdg * Rad2Deg + InverseRotAngle. Only the heading half is
        //kept; Planetarium.Rotation supersedes InverseRotAngle. camHdg is read from the camera
        //directly to avoid depending on stock's two terms staying in that order.
        private static Quaternion PivotRotation(float stockAngle, Vector3 stockAxis, PlanetariumCamera camera)
        {
            using (TiltEmProfiler.MapCameraPivot.Sample())
            {
                if (camera == null || camera.target == null) return Quaternion.AngleAxis(stockAngle, stockAxis);

                //Exactly what stock builds, with the two substitutions above.
                return (Quaternion)Planetarium.Rotation
                       * Quaternion.FromToRotation(Vector3.up, SmoothedNorth(camera))
                       * Quaternion.AngleAxis(camera.camHdg * Mathf.Rad2Deg, stockAxis);
            }
        }

        /// <summary>The eased up axis, so switching mode or focus swings instead of cutting.</summary>
        //Memoised per frame: stock's two branches are not quite exclusive (the alarm clock can
        //hold focus while controls are unlocked), and easing twice would be frame-rate dependent.
        private static Vector3 SmoothedNorth(PlanetariumCamera camera)
        {
            if (_northFrame == Time.frameCount) return _north;

            _north = MapCamera.SmoothMapNorth(GetNorth(camera.target));
            _northFrame = Time.frameCount;

            return _north;
        }

        /// <summary>The up axis in the celestial frame, not the world frame.</summary>
        //This patch cancels the sky with Planetarium.Rotation, so a world pole would apply Zup
        //twice. The in-flight camera wants the world pole instead; see BodyAxes.CelestialNorth.
        //PoleUp uses the focused body's pole, SystemUp uses its star's orbital plane normal.
        private static Vector3 GetNorth(MapObject target)
        {
            CelestialBody body = target.celestialBody;

            if (body == null && target.vessel != null)
            {
                body = target.vessel.mainBody;
            }

            return MapCamera.MapRotation == MapCameraRotation.SystemUp
                ? BodyAxes.SystemNorth(body)
                : BodyAxes.CelestialNorth(body);
        }
    }
}
