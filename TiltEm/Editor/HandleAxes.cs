using System;
using UnityEngine;
using OrbitElements = TiltEm.TiltEmFrames.OrbitElements;

namespace TiltEm
{
    /// <summary>Which number a drag handle turns.</summary>
    public enum EditHandle
    {
        /// <summary>Right ascension of the pole, in Pole mode.</summary>
        PoleRa,

        /// <summary>Declination of the pole, in Pole mode.</summary>
        PoleDec,

        /// <summary>The legacy tiltx value, in Tilt mode.</summary>
        TiltX,

        /// <summary>The legacy tiltz value, in Tilt mode.</summary>
        TiltZ,

        /// <summary>The body's own spin, its initialRotation.</summary>
        Spin,

        /// <summary>The orbit's inclination.</summary>
        Inclination,

        /// <summary>The orbit's longitude of the ascending node.</summary>
        LongitudeOfAscendingNode,

        /// <summary>The orbit's argument of periapsis.</summary>
        ArgumentOfPeriapsis,
    }

    /// <summary>
    /// The axis each handle turns its body about, in the frame the numbers are written in.
    /// </summary>
    //Every one of these numbers is an angle in an Euler chain, so each is exactly a turn about
    //some fixed axis - the outer factor's axis is a fixed axis of the frame, and an inner one's
    //is carried there by the factors outside it. Reading them off the chain rather than
    //differentiating means a handle turns its number degree for degree, with no drift and no
    //place where the parameterisation runs out.
    public static class HandleAxes
    {
        private const double Deg2Rad = Math.PI / 180.0;

        /// <summary>
        /// The axis a tilt handle turns the body about, in the frame its numbers are written in.
        /// A positive turn about it raises the handle's number by the same number of degrees.
        /// </summary>
        //PlanetaryFrame(ra, dec, rot) is Rz(ra - 90) * Rx(dec - 90) * Rz(rot + 90), so the three
        //poles fall straight out of it. The legacy pair is Rx(x) * Rz(z) in Unity space, whose
        //axes reach the celestial frame through the same conjugation the quaternion swizzle is.
        //
        //Takes the editor's own numbers rather than reading them back off the pole. A pole does
        //not carry a right ascension once its declination reaches 90, and reading tiltx back out
        //of one picks a branch, either of which would move a ring out from under the pointer
        //halfway through a drag.
        public static Vector3d Tilt(EditHandle handle, double poleRa, double tiltX, Vector3d pole)
        {
            switch (handle)
            {
                //The outermost factor of the planetary frame, so a celestial axis outright.
                case EditHandle.PoleRa:
                    return new Vector3d(0.0, 0.0, 1.0);

                //The middle factor, about the frame's X axis after the right ascension has
                //turned it. Well defined even at the pole, where the right ascension is not.
                case EditHandle.PoleDec:
                    return new Vector3d(Math.Sin(poleRa * Deg2Rad),
                        -Math.Cos(poleRa * Deg2Rad), 0.0);

                //The innermost factor, which is the pole itself: spinning a body is a turn about
                //its own axis.
                case EditHandle.Spin:
                    return pole;

                //Unity's X axis, negated: the swizzle that carries a Unity rotation into the
                //celestial frame flips the sense of its axis.
                case EditHandle.TiltX:
                    return new Vector3d(-1.0, 0.0, 0.0);

                //Unity's Z axis under the same flip, carried out to where tiltx has put it.
                case EditHandle.TiltZ:
                    double x = tiltX * Deg2Rad;
                    return new Vector3d(0.0, -Math.Cos(x), Math.Sin(x));

                default:
                    return Vector3d.zero;
            }
        }

        /// <summary>
        /// The axis an orbit handle turns the orbit about, in the frame the elements are written
        /// in. Parent-relative elements give a parent-relative axis.
        /// </summary>
        //OrbitalFrame(lan, inc, argPe) is Rz(lan) * Rx(inc) * Rz(argPe): the node, the line of
        //nodes, and the orbit normal, outermost first.
        public static Vector3d Orbit(EditHandle handle, OrbitElements elements)
        {
            switch (handle)
            {
                case EditHandle.LongitudeOfAscendingNode:
                    return new Vector3d(0.0, 0.0, 1.0);

                //The line of nodes, which is where an inclination is measured from.
                case EditHandle.Inclination:
                    double lan = elements.LongitudeOfAscendingNode * Deg2Rad;
                    return new Vector3d(Math.Cos(lan), Math.Sin(lan), 0.0);

                case EditHandle.ArgumentOfPeriapsis:
                    return TiltEmFrames.OrbitalFrame(elements).Z;

                default:
                    return Vector3d.zero;
            }
        }
    }
}
