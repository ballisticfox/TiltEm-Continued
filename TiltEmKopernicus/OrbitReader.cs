using Kopernicus;
using Kopernicus.ConfigParser.Attributes;
using Kopernicus.ConfigParser.BuiltinTypeParsers;
using Kopernicus.ConfigParser.Enumerations;
using Kopernicus.Configuration.Parsing;

namespace TiltEmKopernicus
{
    [RequireConfigType(ConfigType.Node)]
    [ParserTargetExternal("Body", "Orbit", "Kopernicus")]
    public class OrbitReader : BaseLoader
    {
        /// <summary>
        /// Interpret this body's orbital elements in its parent's equatorial frame rather than in
        /// the celestial frame KSP stores them in.
        ///
        /// Off by default, because the celestial frame is what KSP has always meant and what a
        /// real-system pack wants - the IAU publishes orbits that way. It is for the other case:
        /// a made-up system where "in my planet's equatorial plane" is the thing you mean, and
        /// the celestial-frame numbers that express it are not round and change whenever you
        /// adjust the parent's pole.
        ///
        /// With it on, inclination 0 puts the orbit exactly in the parent's equatorial plane and
        /// any other inclination is measured from that plane. The elements are converted once
        /// while the system prefab is being built, so everything downstream - the orbit KSP
        /// stores, the map view, the tracking station - sees ordinary celestial-frame elements
        /// and nothing else in the game has to know.
        ///
        /// A parent with no tilt makes this exactly a no-op, so it is safe to set on every body.
        /// </summary>
        [ParserTarget("relativeToParent")]
        public NumericParser<bool> RelativeToParent
        {
            get => generatedBody.Get("relativeToParent", false);
            set => generatedBody.Set("relativeToParent", value.Value);
        }
    }
}
