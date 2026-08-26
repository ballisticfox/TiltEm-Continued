using UnityEngine;

namespace TiltEm
{
    /// <summary>
    /// Which way is up for a body: its own pole, or the plane of the system it orbits in.
    /// Use celestial variants for sky-relative work and world variants for transforms.
    /// </summary>
    public static class BodyAxes
    {
        /// <summary>
        /// The body's north pole in the celestial frame, Unity-swizzled. It does not move while
        /// a body holds the rotating frame.
        /// </summary>
        public static Vector3 CelestialNorth(CelestialBody body)
        {
            if (body == null || !TiltEm.TryGetTilt(body.bodyName, out BodyTilt tilt)) return Vector3.up;

            return tilt.Tilt.Z.xzy;
        }

        /// <summary>
        /// The body's north pole in world space, Unity-swizzled.
        /// Reads BodyFrame rather than the tilt because BodyFrame already carries transpose(Zup).
        /// </summary>
        public static Vector3 WorldNorth(CelestialBody body)
        {
            //Before the first CBUpdate the frame is all zeros, so Z would be a zero vector.
            if (body == null || !TiltEmFrames.IsUsableRotation(body.BodyFrame)) return Vector3.up;

            return body.BodyFrame.Z.xzy;
        }

        /// <summary>
        /// The normal of the system's orbital plane, in the celestial frame, Unity-swizzled.
        /// Falls back to the star's own pole when no orbital plane is configured.
        /// </summary>
        public static Vector3 SystemNorth(CelestialBody body)
        {
            CelestialBody star = StarFor(body);

            if (star == null) return Vector3.up;

            if (TiltEm.TryGetOrbitalPlane(star.bodyName, out BodyTilt plane)) return plane.Tilt.Z.xzy;

            return CelestialNorth(star);
        }

        /// <summary>
        /// The nearest star at or above the body. Falls back to the root of the tree when no body
        /// on the way up sets isStar.
        /// </summary>
        private static CelestialBody StarFor(CelestialBody body)
        {
            CelestialBody current = body;

            //In stock a root star is its own referenceBody, which is how this usually ends. The
            //counter only stops a malformed tree from hanging the frame.
            for (int guard = 0; current != null && guard < 64; guard++)
            {
                if (current.isStar) return current;

                CelestialBody parent = current.referenceBody;
                if (parent == null || ReferenceEquals(parent, current)) return current;

                current = parent;
            }

            return current;
        }
    }
}
