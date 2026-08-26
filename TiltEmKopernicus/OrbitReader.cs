using Kopernicus;
using Kopernicus.ConfigParser.Attributes;
using Kopernicus.ConfigParser.BuiltinTypeParsers;
using Kopernicus.ConfigParser.Enumerations;
using Kopernicus.Configuration.Parsing;

namespace TiltEmKopernicus
{
    /// <summary>
    /// Adds Tilt'Em's field to Kopernicus's Body -> Orbit node.
    /// </summary>
    [RequireConfigType(ConfigType.Node)]
    [ParserTargetExternal("Body", "Orbit", "Kopernicus")]
    public class OrbitReader : BaseLoader
    {
        /// <summary>
        /// Read this body's orbital elements in its parent's equatorial frame rather than the
        /// celestial frame KSP stores them in.
        ///
        /// Off by default. The celestial frame is what KSP has always meant and what a real-system
        /// pack wants, since the IAU publishes orbits that way. Turn it on for a made-up system,
        /// where "in my planet's equatorial plane" is the thing you mean and the celestial numbers
        /// expressing it are neither round nor stable when the parent's pole moves.
        ///
        /// With it on, inclination 0 lies exactly in the parent's equatorial plane and any other
        /// inclination is measured from there. The conversion happens once while the system prefab
        /// is built, so the stored orbit, the map view and the tracking station all see ordinary
        /// celestial elements. An untilted parent makes it a no-op, so it is safe to set on every
        /// body. See section 8.2 of Docs/TILT_MATHEMATICS.pdf.
        /// </summary>
        [ParserTarget("relativeToParent")]
        public NumericParser<bool> RelativeToParent
        {
            get => generatedBody.Get("relativeToParent", false);
            set => generatedBody.Set("relativeToParent", value.Value);
        }
    }
}
