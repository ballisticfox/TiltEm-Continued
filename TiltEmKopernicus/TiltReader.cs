using Kopernicus;
using Kopernicus.ConfigParser.Attributes;
using Kopernicus.ConfigParser.BuiltinTypeParsers;
using Kopernicus.ConfigParser.Enumerations;
using Kopernicus.Configuration.Parsing;

namespace TiltEmKopernicus
{
    [RequireConfigType(ConfigType.Node)]
    [ParserTargetExternal("Body", "Properties", "Kopernicus")]
    public class TiltReader : BaseLoader
    {
        // Preferred form: the body's north pole as a direction, the way the IAU publishes
        // real obliquities. poleDec = 90 means no tilt.

        [ParserTarget("poleRA")]
        public NumericParser<double> PoleRa
        {
            get => generatedBody.Get("poleRA", 0d);
            set => generatedBody.Set("poleRA", value.Value);
        }

        [ParserTarget("poleDec")]
        public NumericParser<double> PoleDec
        {
            get => generatedBody.Get("poleDec", 90d);
            set => generatedBody.Set("poleDec", value.Value);
        }

        // Legacy form: Unity Euler degrees. Kept so existing configs keep working; converted
        // to an equivalent pole (plus prime meridian) on load.

        [ParserTarget("tiltx")]
        public NumericParser<double> TiltX
        {
            get => generatedBody.Get("tiltx", 0d);
            set => generatedBody.Set("tiltx", value.Value);
        }

        [ParserTarget("tiltz")]
        public NumericParser<double> TiltZ
        {
            get => generatedBody.Get("tiltz", 0d);
            set => generatedBody.Set("tiltz", value.Value);
        }

        /// <summary>
        /// Interpret this body's tilt as an obliquity from its OWN orbital plane rather than from
        /// the celestial equator.
        ///
        /// With no tilt set alongside it, the body's equator ends up in its orbital plane - which
        /// for a moon means its parent stays over the equator. With a tilt set, that tilt is the
        /// obliquity in the astronomical sense: the lean away from the orbit normal.
        ///
        /// Off by default. The celestial frame is what KSP means and what a real-system pack
        /// wants, since the IAU publishes poles that way.
        /// </summary>
        [ParserTarget("tiltRelativeToParent")]
        public NumericParser<bool> TiltRelativeToParent
        {
            get => generatedBody.Get("tiltRelativeToParent", false);
            set => generatedBody.Set("tiltRelativeToParent", value.Value);
        }

        // The system's orbital plane, set on a star. Read by the map camera's "System up"
        // rotation mode for every body that ultimately orbits this one. Both forms mirror the
        // tilt forms above: a pole is the normal of the plane, and the legacy Euler pair is
        // converted to the equivalent normal on load.

        [ParserTarget("orbitalPlaneRA")]
        public NumericParser<double> OrbitalPlaneRa
        {
            get => generatedBody.Get("orbitalPlaneRA", 0d);
            set => generatedBody.Set("orbitalPlaneRA", value.Value);
        }

        [ParserTarget("orbitalPlaneDec")]
        public NumericParser<double> OrbitalPlaneDec
        {
            get => generatedBody.Get("orbitalPlaneDec", 90d);
            set => generatedBody.Set("orbitalPlaneDec", value.Value);
        }

        [ParserTarget("orbitalPlaneX")]
        public NumericParser<double> OrbitalPlaneX
        {
            get => generatedBody.Get("orbitalPlaneX", 0d);
            set => generatedBody.Set("orbitalPlaneX", value.Value);
        }

        [ParserTarget("orbitalPlaneZ")]
        public NumericParser<double> OrbitalPlaneZ
        {
            get => generatedBody.Get("orbitalPlaneZ", 0d);
            set => generatedBody.Set("orbitalPlaneZ", value.Value);
        }
    }
}
