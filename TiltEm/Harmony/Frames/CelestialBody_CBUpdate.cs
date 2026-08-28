using HarmonyLib;
using System;
using UnityEngine;

// ReSharper disable All

namespace TiltEm.Harmony
{
    /// <summary>
    /// The main patch: rebuilds a tilted body's frames each tick.
    /// See sections 3 and 5 of Docs/TILT_MATHEMATICS.pdf.
    /// </summary>
    //Below inverseRotThresholdAltitude KSP freezes the planet and turns the universe instead.
    //Stock uses one generator for both sides of that switch; this patch replaces it with a
    //pole-based generator that carries the tilt through. The helpers below split stock's
    //arithmetic from the parts TiltEm changes.
    [HarmonyPatch(typeof(CelestialBody))]
    [HarmonyPatch("CBUpdate")]
    internal class CelestialBody_CBUpdate
    {
        /// <summary>Stock's literal, kept exact so mass and gravParameter round identically.</summary>
        private const double GravitationalConstant = 6.67408E-11;

        [HarmonyPrefix]
        private static bool PrefixCBUpdate(CelestialBody __instance)
        {
            using (TiltEmProfiler.CbUpdate.Sample())
            {
                //Every body comes through here: once a tilted body holds the rotating frame, stock's
                //Rz(rot - InverseRotAngle) diverges from transpose(Zup)*Rz(rot) for all bodies.
                if (!TiltEm.TryGetTilt(__instance.bodyName, out BodyTilt tilt))
                {
                    tilt = TiltEmFrames.Untilted;
                }

                CBUpdate(__instance, tilt);
                return false;
            }
        }

        private static void CBUpdate(CelestialBody body, BodyTilt tilt)
        {
            body.transformRight = body.transform.right;
            body.transformUp = body.transform.up;

            UpdateMassAndGravity(body);

            if (body.rotates && body.rotationPeriod != 0 &&
                (!body.tidallyLocked || body.orbit != null && body.orbit.period != 0))
            {
                using (TiltEmProfiler.CbUpdateRotation.Sample())
                {
                    UpdateRotation(body, tilt);
                }
            }

            if (body.orbitDriver)
            {
                using (TiltEmProfiler.CbUpdateOrbit.Sample())
                {
                    body.orbitDriver.UpdateOrbit(true);
                }
            }

            UpdateSolarDayLength(body);
        }

        /// <summary>Stock's arithmetic, unchanged.</summary>
        private static void UpdateMassAndGravity(CelestialBody body)
        {
            double surfaceGravity = body.GeeASL * PhysicsGlobals.GravitationalAcceleration;

            body.gMagnitudeAtCenter = surfaceGravity * body.Radius * body.Radius;
            body.Mass = body.Radius * body.Radius * surfaceGravity / GravitationalConstant;
            body.gravParameter = body.Mass * GravitationalConstant;
        }

        private static void UpdateRotation(CelestialBody body, BodyTilt tilt)
        {
            if (body.tidallyLocked)
            {
                body.rotationPeriod = body.orbit.period;
            }

            body.rotPeriodRecip = 1 / body.rotationPeriod;
            body.rotationAngle =
                (body.initialRotation + 360 * body.rotPeriodRecip * Planetarium.GetUniversalTime()) % 360;

            using (TiltEmProfiler.CbUpdatePlanetarium.Sample())
            {
                UpdatePlanetariumFrame(body, tilt);
            }

            //Same formula in both modes; for the rotating body transpose(Zup) cancels and the
            //frame freezes, matching what stock achieves by not touching it at all.
            TiltEmFrames.BodyFrame(tilt, body.rotationAngle, Planetarium.Zup, ref body.BodyFrame);
            body.rotation = body.BodyFrame.Rotation.swizzle;
            body.bodyTransform.rotation = body.rotation;

            UpdateAngularVelocity(body);
        }

        /// <summary>
        /// Turns the sky while this body holds the rotating frame, or lets it go when it does not.
        /// </summary>
        private static void UpdatePlanetariumFrame(CelestialBody body, BodyTilt tilt)
        {
            //Not redundant with the flag: stock can leave a body flagged after it stops being
            //dominant, and two flagged bodies would fight over Zup. See MayHoldRotatingFrame.
            if (body.inverseRotation && PlanetariumAnchor.MayHoldRotatingFrame(body))
            {
                //Driven by elapsed rotation from the anchor, not InverseRotAngle. On the first
                //tick after the switch the elapsed angle is zero and Zup is continuous.
                PlanetariumAnchor.EnsureZupAnchor(body, tilt);

                Planetarium.Zup = TiltEmFrames.Zup(PlanetariumAnchor.ZupAnchor, tilt,
                    body.rotationAngle - PlanetariumAnchor.ZupAnchorRotationAngle);
                Planetarium.Rotation = QuaternionD.Inverse(Planetarium.Zup.Rotation).swizzle;

                //Still maintained for anything that reads it, though nothing here builds a frame
                //from it any more.
                Planetarium.InverseRotAngle = (body.rotationAngle - body.directRotAngle) % 360;

                return;
            }

            //Anchor covers one continuous stretch; a body still flagged but no longer entitled
            //lands here too and should keep moving inertially.
            PlanetariumAnchor.ReleaseZupAnchor(body);

            body.directRotAngle = (body.rotationAngle - Planetarium.InverseRotAngle) % 360;
        }

        /// <summary>Derives the angular velocity from the body frame's pole.</summary>
        //Stock hardcodes Vector3d.down and .back, which only match the pole for untilted bodies.
        //Getting it wrong left the navball and velocity step on the untilted axis.
        private static void UpdateAngularVelocity(CelestialBody body)
        {
            double angularSpeed = Math.PI * 2 * body.rotPeriodRecip;

            body.zUpAngularVelocity = body.BodyFrame.Z * -angularSpeed;
            body.angularVelocity = body.zUpAngularVelocity.xzy;
            body.angularV = body.angularVelocity.magnitude;
        }

        /// <summary>Stock's arithmetic, unchanged.</summary>
        private static void UpdateSolarDayLength(CelestialBody body)
        {
            CelestialBody sun = Planetarium.fetch == null ? FlightGlobals.Bodies[0] : Planetarium.fetch.Sun;

            //Walk up to the ancestor that orbits the sun; its period is the year this body's
            //solar day is measured against.
            CelestialBody topmost = body;
            while (topmost.referenceBody != sun && topmost.referenceBody != null)
            {
                topmost = topmost.referenceBody;
            }

            if (topmost.orbit == null)
            {
                body.solarDayLength = 1;
                return;
            }

            double beat = topmost.orbit.period - body.rotationPeriod;
            body.solarDayLength = beat != 0
                ? topmost.orbit.period * body.rotationPeriod / beat
                : double.MaxValue;
        }
    }
}
