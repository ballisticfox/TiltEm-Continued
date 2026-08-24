using TiltEm;
using UnityEngine;

namespace TiltEmKopernicus
{
    [KSPAddon(KSPAddon.Startup.SpaceCentre, true)]
    public class KopernicusLoader : MonoBehaviour
    {
        public void Awake()
        {
            DontDestroyOnLoad(this);
            Debug.Log("[TiltEm]: TiltEmKopernicus started!");

            foreach (var body in FlightGlobals.Bodies)
            {
                //TryReadEffective, not TryRead: it also applies tiltRelativeToParent. Reading
                //through TiltConfig rather than inline is what keeps this and the parent-relative
                //orbit rebase from disagreeing about which pole a body actually has.
                BodyTilt tilt;
                if (TiltConfig.TryReadEffective(body, out tilt))
                {
                    TiltEm.TiltEm.AddTiltData(body, tilt);
                }

                //Only meaningful on a star, but registered for whatever body declares it - the
                //map camera looks it up on whichever body its walk up the tree lands on.
                BodyTilt plane;
                if (TiltConfig.TryReadOrbitalPlane(body, out plane))
                {
                    TiltEm.TiltEm.AddOrbitalPlane(body, plane);
                }
            }
        }
    }
}
