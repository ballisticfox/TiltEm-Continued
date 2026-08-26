using HarmonyLib;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using UnityEngine;

// ReSharper disable All

namespace TiltEm.Harmony
{
    /// <summary>
    /// Fixes the map / tracking-station camera for tilted bodies.
    ///
    /// Stock orients the camera pivot with
    ///
    ///     endRot = Quaternion.AngleAxis(camHdg * Rad2Deg + (float)Planetarium.InverseRotAngle, Vector3.up)
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
    ///
    /// Why a transpiler rather than a postfix. Both of stock's assignments to endRot are
    /// followed by more work in the same method: the pitch rotate samples `transform.right`,
    /// then `transform.localPosition` is lerped toward the target distance,
    /// `transform.localRotation` is rebuilt from it, and finally `pivot.localPosition` is
    /// smooth-damped. A postfix runs after all of that, which cost two things.
    ///
    /// It sampled the pitch axis at the wrong moment. Stock reads `transform.right` *before*
    /// refreshing `transform.localRotation`; a postfix necessarily reads it after, so while the
    /// zoom lerp was in flight the two disagreed and the pitch axis was slightly off.
    ///
    /// And it moved the camera after the host method had finished. The camera transform is a
    /// child of the pivot, so writing the pivot late moves the camera's world pose at the very
    /// end of LateUpdate - after anything earlier in the frame has already read it. That is
    /// invisible on its own, because Unity evaluates a camera's matrices live. It stops being
    /// invisible next to KSPCommunityFixes' OptimisedVectorLines, which replaces Vectrosity's
    /// Camera.WorldToScreenPoint with a projection matrix cached once per frame, captured
    /// lazily on the first orbit-line projection. If that capture happens before this write,
    /// every orbit line that frame is projected from the pre-correction pose while the planet
    /// meshes render from the post-correction one, and the lines wobble against the bodies
    /// whenever the camera moves.
    ///
    /// Substituting the value where stock computes it removes both problems structurally: the
    /// pivot reaches its final value at exactly the point stock intended, and the four lines
    /// that depend on it run afterwards as written. It also removes the need to guess which of
    /// stock's two branches ran, which the postfix had to reconstruct from the input locks.
    /// </summary>
    [HarmonyPatch(typeof(PlanetariumCamera))]
    [HarmonyPatch("LateUpdate")]
    internal class PlanetariumCamera_LateUpdate
    {
        /// <summary>
        /// Stock assigns endRot twice - once in the ordinary camera-controls branch, once in the
        /// branch that runs while the input is fully locked or the alarm clock has focus - and
        /// calls Quaternion.AngleAxis nowhere else in the method.
        /// </summary>
        private const int ExpectedSites = 2;

        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> TranspileLateUpdate(IEnumerable<CodeInstruction> instructions)
        {
            var stock = AccessTools.Method(typeof(Quaternion), nameof(Quaternion.AngleAxis),
                new[] { typeof(float), typeof(Vector3) });
            var tilted = AccessTools.Method(typeof(PlanetariumCamera_LateUpdate), nameof(PivotRotation),
                new[] { typeof(float), typeof(Vector3), typeof(PlanetariumCamera) });

            var code = new List<CodeInstruction>(instructions);
            var replaced = 0;

            if (stock == null || tilted == null)
            {
                Debug.LogError("[TiltEm]: could not resolve the map camera transpiler targets; "
                               + "the map camera will use stock's untilted framing.");
                return code;
            }

            for (var i = 0; i < code.Count; i++)
            {
                if (!code[i].Calls(stock)) continue;

                //The replacement takes the camera as a third argument, so push it after the two
                //stock arguments are already on the stack.
                var call = code[i];
                var loadCamera = new CodeInstruction(OpCodes.Ldarg_0);

                //A call in the middle of an expression cannot be a branch target, but blocks and
                //labels are cheap to carry across and expensive to lose.
                loadCamera.labels.AddRange(call.labels);
                call.labels.Clear();
                loadCamera.blocks.AddRange(call.blocks);
                call.blocks.Clear();

                call.opcode = OpCodes.Call;
                call.operand = tilted;

                code.Insert(i, loadCamera);
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

        /// <summary>
        /// Stands in for stock's Quaternion.AngleAxis at both endRot sites.
        ///
        /// The angle handed in is <c>camHdg * Rad2Deg + InverseRotAngle</c>. The heading half is
        /// still wanted and is taken from the camera directly; the InverseRotAngle half is
        /// discarded, because Planetarium.Rotation supersedes it. Reading camHdg rather than
        /// subtracting InverseRotAngle back off avoids depending on stock's two terms staying in
        /// that order.
        /// </summary>
        private static Quaternion PivotRotation(float stockAngle, Vector3 stockAxis, PlanetariumCamera camera)
        {
            if (camera == null || camera.target == null) return Quaternion.AngleAxis(stockAngle, stockAxis);

            //Eased rather than taken raw, so switching rotation mode with V - or focusing a body
            //with a different pole - swings over a few frames instead of cutting. Driven from the
            //target every frame rather than kicked off by the toggle, so both causes of a change
            //are handled by the same path and neither can be missed.
            //
            //Memoised per frame because stock's two branches are not quite exclusive: the alarm
            //clock can hold focus while the camera controls are unlocked, and easing twice in one
            //frame would make the swing frame-rate dependent.
            var north = SmoothedNorth(camera);

            //Exactly what stock builds, with the two substitutions described above.
            return (Quaternion)Planetarium.Rotation
                   * Quaternion.FromToRotation(Vector3.up, north)
                   * Quaternion.AngleAxis(camera.camHdg * Mathf.Rad2Deg, stockAxis);
        }

        private static int _northFrame = -1;
        private static Vector3 _north;

        private static Vector3 SmoothedNorth(PlanetariumCamera camera)
        {
            if (_northFrame == Time.frameCount) return _north;

            _north = TiltEm.SmoothMapNorth(GetNorth(camera.target));
            _northFrame = Time.frameCount;

            return _north;
        }

        /// <summary>
        /// The up axis, in the celestial frame - not the world frame. The distinction matters
        /// here and is spelled out on <see cref="TiltEm.CelestialNorth"/>: this patch cancels the
        /// sky itself with Planetarium.Rotation, so a world pole would apply Zup a second time.
        /// The in-flight camera, which does no such cancellation, wants
        /// <see cref="TiltEm.WorldNorth"/> instead.
        ///
        /// Which axis depends on the mode the player picked with V - the focused body's own pole,
        /// or the plane of the star it ultimately orbits. Both are celestial-frame directions, so
        /// the choice is confined to this one call.
        /// </summary>
        private static Vector3 GetNorth(MapObject target)
        {
            var body = target.celestialBody;

            if (body == null && target.vessel != null)
            {
                body = target.vessel.mainBody;
            }

            return TiltEm.MapRotation == MapCameraRotation.SystemUp
                ? TiltEm.SystemNorth(body)
                : TiltEm.CelestialNorth(body);
        }
    }
}
