using Kopernicus;
using Kopernicus.ConfigParser.Attributes;
using Kopernicus.ConfigParser.BuiltinTypeParsers;
using Kopernicus.ConfigParser.Enumerations;
using Kopernicus.Configuration.Parsing;

namespace TiltEm
{
    /// <summary>
    /// Adds Tilt'Em's field to Kopernicus's Body -> Orbit node.
    /// </summary>
    [RequireConfigType(ConfigType.Node)]
    [ParserTargetExternal("Body", "Orbit", "Kopernicus")]
    public class OrbitReader : BaseLoader
    {
        /// <summary>
        /// When true, this body's orbital elements are read in its parent's equatorial frame
        /// and converted once to celestial-frame elements during the system prefab build.
        /// Inclination 0 lies in the parent's equatorial plane. Off by default; no-op if the
        /// parent is untilted. See section 8.2 of Docs/TILT_MATHEMATICS.pdf.
        /// </summary>
        [ParserTarget("relativeToParent")]
        public NumericParser<bool> RelativeToParent
        {
            get => generatedBody.Get("relativeToParent", false);
            set => generatedBody.Set("relativeToParent", value.Value);
        }
    }
}
