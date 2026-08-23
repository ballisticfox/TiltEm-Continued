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
    }
}
