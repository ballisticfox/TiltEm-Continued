using HarmonyLib;
using System;
using UnityEngine;

// ReSharper disable All

namespace TiltEm.Harmony
{
    /// <summary>
    /// The main patch: it rebuilds a tilted body's frames each tick.
    ///
    /// Background. Below a body's inverseRotThresholdAltitude, KSP flips into "inverse
    /// rotation": the planet stops turning and the entire universe turns around it instead.
    /// That is not a rendering trick you can opt out of - you cannot land on a moving planet
    /// in a single-precision physics engine - so any tilt has to survive the switch.
    ///
    /// Stock survives it because both frames are built from the same generator and the switch
    /// only changes *which angle is frozen*: directRotAngle and InverseRotAngle always sum to
    /// rotationAngle, so at the instant of the switch both frames are numerically unchanged.
    /// That argument holds for any frame generator, which is exactly what this patch exploits:
    /// it uses one pole-based generator on both sides of the threshold instead of moving the
    /// tilt from the planet to the planetarium and back. See Docs/REFERENCE_FRAME_DEFECTS.md.
    /// </summary>
    [HarmonyPatch(typeof(CelestialBody))]
    [HarmonyPatch("CBUpdate")]
    internal class CelestialBody_CBUpdate
    {
        [HarmonyPrefix]
        private static bool PrefixCBUpdate(CelestialBody __instance)
        {

            // Every body goes through here, not just the tilted ones. As soon as a tilted body
            // holds the rotating frame, Zup stops being a plain spin about +Z, and stock's
            // Rz(rotationAngle - InverseRotAngle) is no longer equal to transpose(Zup) * Rz(rot)
            // for anybody. Untilted bodies still reduce to stock exactly, because with an
            // identity tilt the two expressions agree whenever Zup is a plain spin.
            if (!TiltEm.TryGetTilt(__instance.bodyName, out var tilt))
            {
                tilt = TiltEmFrames.Untilted;
            }

            CBUpdate(__instance, tilt);
            return false;
        }

        private static void CBUpdate(CelestialBody body, BodyTilt tilt)
        {
            body.transformRight = body.transform.right;
            body.transformUp = body.transform.up;

            body.gMagnitudeAtCenter = body.GeeASL * PhysicsGlobals.GravitationalAcceleration * body.Radius * body.Radius;
            body.Mass = body.Radius * body.Radius * (body.GeeASL * PhysicsGlobals.GravitationalAcceleration) / 6.67408E-11;
            body.gravParameter = body.Mass * 6.67408E-11;

            if (body.rotates && body.rotationPeriod != 0 && (!body.tidallyLocked || body.orbit != null && body.orbit.period != 0))
            {
                if (body.tidallyLocked)
                {
                    body.rotationPeriod = body.orbit.period;
                }

                body.rotPeriodRecip = 1 / body.rotationPeriod;
                body.rotationAngle = (body.initialRotation + 360 * body.rotPeriodRecip * Planetarium.GetUniversalTime()) % 360;

                //The entitlement check is not redundant with the flag: stock can leave a body
                //flagged as rotating after it stops being dominant, and two flagged bodies
                //would fight over one Zup and one anchor. See TiltEm.MayHoldRotatingFrame.
                if (body.inverseRotation && TiltEm.MayHoldRotatingFrame(body))
                {
                    //Anchored on the body's own elapsed rotation, so Zup does not depend on the
                    //InverseRotAngle bookkeeping at all. On the first tick after the switch the
                    //elapsed angle is zero and Zup comes out exactly as it already was.
                    TiltEm.EnsureZupAnchor(body, tilt);

                    Planetarium.Zup = TiltEmFrames.Zup(TiltEm.ZupAnchor, tilt,
                        body.rotationAngle - TiltEm.ZupAnchorRotationAngle);
                    Planetarium.Rotation = QuaternionD.Inverse(Planetarium.Zup.Rotation).swizzle;

                    //Stock's scalar bookkeeping is still maintained for anything that reads it,
                    //but nothing here builds a frame from it any more.
                    Planetarium.InverseRotAngle = (body.rotationAngle - body.directRotAngle) % 360;
                }
                else
                {
                    //The anchor describes one continuous stretch in the rotating frame, so it has
                    //to be dropped when that stretch ends - otherwise re-entry resumes an anchor
                    //the body has since rotated away from. See TiltEm.ReleaseZupAnchor.
                    //
                    //A body that is still flagged but no longer entitled lands here too, and
                    //that is deliberate: it should behave inertially and keep its frame moving,
                    //rather than sit frozen at the orientation it happened to stop at.
                    TiltEm.ReleaseZupAnchor(body);

                    body.directRotAngle = (body.rotationAngle - Planetarium.InverseRotAngle) % 360;
                }

                //Built identically in both modes, from the body's own rotation and pole, with
                //transpose(Zup) undoing whatever the rotating body has done to the sky. For the
                //rotating body itself the two cancel and the frame comes out frozen, which is
                //what stock achieves by simply not touching it.
                TiltEmFrames.BodyFrame(tilt, body.rotationAngle, Planetarium.Zup, ref body.BodyFrame);
                body.rotation = body.BodyFrame.Rotation.swizzle;
                body.bodyTransform.rotation = body.rotation;

                //The spin axis is the body frame's pole, not world up. Stock hardcodes
                //Vector3d.down / Vector3d.back, which is the same thing only for an untilted
                //body - this reduces to those exactly when BodyFrame.Z is +Z. Getting this
                //wrong is what left the navball and the rotating-frame velocity step on the
                //untilted axis above the threshold.
                var angularSpeed = Math.PI * 2 * body.rotPeriodRecip;
                body.zUpAngularVelocity = body.BodyFrame.Z * -angularSpeed;
                body.angularVelocity = body.zUpAngularVelocity.xzy;
                body.angularV = body.angularVelocity.magnitude;
            }

            if (body.orbitDriver)
            {
                body.orbitDriver.UpdateOrbit(true);
            }

            var celestialBody = body;
            var sun = (Planetarium.fetch == null ? FlightGlobals.Bodies[0] : Planetarium.fetch.Sun);
            while (celestialBody.referenceBody != sun && celestialBody.referenceBody != null)
            {
                celestialBody = celestialBody.referenceBody;
            }

            if (celestialBody.orbit == null)
            {
                body.solarDayLength = 1;
            }
            else
            {
                var num = celestialBody.orbit.period - body.rotationPeriod;
                body.solarDayLength = num != 0 ? celestialBody.orbit.period * body.rotationPeriod / num : double.MaxValue;
            }
        }
    }
}
