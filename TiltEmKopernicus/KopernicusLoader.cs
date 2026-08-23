using Kopernicus;
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
                //poleRA/poleDec is the preferred form and wins if present. tiltx/tiltz is the
                //legacy Unity Euler form, converted to the equivalent pole on load.
                if (body.Has("poleRA") || body.Has("poleDec"))
                {
                    TiltEm.TiltEm.AddTiltData(body, body.Get("poleRA", 0d), body.Get("poleDec", 90d));
                }
                else if (body.Has("tiltx") || body.Has("tiltz"))
                {
                    TiltEm.TiltEm.AddTiltData(body, new Vector3d(body.Get("tiltx", 0d), 0, body.Get("tiltz", 0d)));
                }
            }
        }
    }
}
