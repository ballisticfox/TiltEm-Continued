using UnityEngine;

namespace TiltEm
{
    /// <summary>
    /// Registers every body's configured tilt and orbital plane with Tilt'Em, once, as the space
    /// centre loads. By then Kopernicus has finished parsing, so each body carries the values its
    /// Properties node asked for.
    /// </summary>
    [KSPAddon(KSPAddon.Startup.SpaceCentre, true)]
    public class TiltLoader : MonoBehaviour
    {
        public void Awake()
        {
            if (PrincipiaCheck.Installed)
            {
                Destroy(gameObject);
                return;
            }

            DontDestroyOnLoad(this);

            int tilts = 0;
            int planes = 0;

            using (TiltEmProfiler.LoadTilts.Sample())
            {
                foreach (CelestialBody body in FlightGlobals.Bodies)
                {
                    //TryReadEffective, not TryRead: it also applies tiltRelativeToParent. Going
                    //through TiltConfig keeps this and the parent-relative orbit rebase from
                    //disagreeing about which pole a body actually has.
                    if (TiltConfig.TryReadEffective(body, out BodyTilt tilt))
                    {
                        TiltEm.AddTiltData(body, tilt);
                        tilts++;
                    }

                    //Only meaningful on a star, but registered for whatever body declares it: the map
                    //camera looks it up on whichever body its walk up the tree lands on.
                    if (TiltConfig.TryReadOrbitalPlane(body, out BodyTilt plane))
                    {
                        TiltEm.AddOrbitalPlane(body, plane);
                        planes++;
                    }
                }
            }

            //Counts rather than a bare banner: "my poleRA did nothing" is the usual report, and
            //a zero here separates a config that was not read from one that was read and ignored.
            Debug.Log("[TiltEm]: Kopernicus config supplied " + tilts + " tilts and "
                      + planes + " orbital planes.");
        }
    }
}
