using Kopernicus;
using Kopernicus.ConfigParser.Attributes;
using Kopernicus.ConfigParser.BuiltinTypeParsers;
using Kopernicus.ConfigParser.Enumerations;
using Kopernicus.Configuration.Parsing;

namespace TiltEm
{
    /// <summary>
    /// Adds Tilt'Em's fields to Kopernicus's Body -> Properties node: a body's tilt, and the
    /// orbital plane of the system it heads.
    /// </summary>
    //Each key needs its own property for Kopernicus to bind to. TiltConfig decides which form
    //wins when a body sets more than one.
    [RequireConfigType(ConfigType.Node)]
    [ParserTargetExternal("Body", "Properties", "Kopernicus")]
    public class TiltReader : BaseLoader
    {
        /// <summary>Right ascension of the body's north pole, degrees. The preferred form, as
        /// the IAU publishes real obliquities.</summary>
        [ParserTarget("poleRA")]
        public NumericParser<double> PoleRa
        {
            get => generatedBody.Get("poleRA", 0d);
            set => generatedBody.Set("poleRA", value.Value);
        }

        /// <summary>Declination of the body's north pole, degrees. 90 means no tilt.</summary>
        [ParserTarget("poleDec")]
        public NumericParser<double> PoleDec
        {
            get => generatedBody.Get("poleDec", 90d);
            set => generatedBody.Set("poleDec", value.Value);
        }

        /// <summary>Legacy tilt about Unity's X axis, degrees. Converted to a pole plus a prime
        /// meridian on load, so existing configs keep working.</summary>
        [ParserTarget("tiltx")]
        public NumericParser<double> TiltX
        {
            get => generatedBody.Get("tiltx", 0d);
            set => generatedBody.Set("tiltx", value.Value);
        }

        /// <summary>Legacy tilt about Unity's Z axis, degrees. See <see cref="TiltX"/>.</summary>
        [ParserTarget("tiltz")]
        public NumericParser<double> TiltZ
        {
            get => generatedBody.Get("tiltz", 0d);
            set => generatedBody.Set("tiltz", value.Value);
        }

        /// <summary>
        /// When true, this body's tilt is read as obliquity from its orbital plane rather than
        /// the celestial equator. Alone, it aligns the equator to the orbit; with a tilt, that
        /// tilt becomes the lean from the orbit normal. Off by default. See section 8.2 of
        /// Docs/TILT_MATHEMATICS.pdf.
        /// </summary>
        [ParserTarget("tiltRelativeToParent")]
        public NumericParser<bool> TiltRelativeToParent
        {
            get => generatedBody.Get("tiltRelativeToParent", false);
            set => generatedBody.Set("tiltRelativeToParent", value.Value);
        }

        /// <summary>Right ascension of the normal to this star's system plane, degrees. Read by
        /// the map camera's System up mode for every body that ultimately orbits it.</summary>
        [ParserTarget("orbitalPlaneRA")]
        public NumericParser<double> OrbitalPlaneRa
        {
            get => generatedBody.Get("orbitalPlaneRA", 0d);
            set => generatedBody.Set("orbitalPlaneRA", value.Value);
        }

        /// <summary>Declination of the normal to this star's system plane, degrees. 90 is the
        /// celestial equator, which is the right answer for the stock system.</summary>
        [ParserTarget("orbitalPlaneDec")]
        public NumericParser<double> OrbitalPlaneDec
        {
            get => generatedBody.Get("orbitalPlaneDec", 90d);
            set => generatedBody.Set("orbitalPlaneDec", value.Value);
        }

        /// <summary>Legacy system plane about Unity's X axis, degrees. Converted to the
        /// equivalent normal on load.</summary>
        [ParserTarget("orbitalPlaneX")]
        public NumericParser<double> OrbitalPlaneX
        {
            get => generatedBody.Get("orbitalPlaneX", 0d);
            set => generatedBody.Set("orbitalPlaneX", value.Value);
        }

        /// <summary>Legacy system plane about Unity's Z axis, degrees. See
        /// <see cref="OrbitalPlaneX"/>.</summary>
        [ParserTarget("orbitalPlaneZ")]
        public NumericParser<double> OrbitalPlaneZ
        {
            get => generatedBody.Get("orbitalPlaneZ", 0d);
            set => generatedBody.Set("orbitalPlaneZ", value.Value);
        }
    }
}
