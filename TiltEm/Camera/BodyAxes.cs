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
            return CelestialNormal(body).xzy;
        }

        /// <summary>The body's north pole in the celestial frame, unswizzled.</summary>
        private static Vector3d CelestialNormal(CelestialBody body)
        {
            if (body == null || !TiltEm.TryGetTilt(body.bodyName, out BodyTilt tilt))
            {
                return new Vector3d(0.0, 0.0, 1.0);
            }

            return tilt.Tilt.Z;
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
        /// A celestial-frame direction in world space, Unity-swizzled. The same conversion the
        /// body frame carries, so a pole put through this comes out where
        /// <see cref="WorldNorth"/> has it.
        /// </summary>
        //Directions only. A rotation AXIS picks up a sign here as well, because swapping two
        //axes flips handedness and with it the sense of every turn; the drag rings undo that.
        public static Vector3 ToWorld(Vector3d celestial)
        {
            return TiltEmFrames.OrIdentity(Planetarium.Zup).WorldToLocal(celestial).xzy;
        }

        /// <summary>
        /// The normal of the system's orbital plane, in the celestial frame, Unity-swizzled.
        /// Falls back to the star's own pole when no orbital plane is configured.
        /// </summary>
        public static Vector3 SystemNorth(CelestialBody body)
        {
            return SystemNormal(body).xzy;
        }

        /// <summary>
        /// The same normal in world space, for the in-flight cameras. The map camera cancels the
        /// sky itself and wants the celestial form; see <see cref="SystemNorth"/>.
        /// </summary>
        public static Vector3 WorldSystemNorth(CelestialBody body)
        {
            return ToWorld(SystemNormal(body));
        }

        /// <summary>The normal of the system's orbital plane, in the celestial frame, unswizzled.</summary>
        private static Vector3d SystemNormal(CelestialBody body)
        {
            CelestialBody star = StarFor(body);

            if (star == null) return new Vector3d(0.0, 0.0, 1.0);

            //Falls back to the star's own pole, which is right for a system whose planets orbit
            //in their star's equator and wrong for one that says otherwise - hence the key.
            return TiltEm.TryGetOrbitalPlane(star.bodyName, out BodyTilt plane)
                ? plane.Tilt.Z
                : CelestialNormal(star);
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
