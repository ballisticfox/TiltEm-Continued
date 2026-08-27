using System;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace TiltEm
{
    /// <summary>What one body's export writes, as plain numbers.</summary>
    //A struct of doubles rather than the BodyEdit itself, so building the file is separable from
    //the game and the format can be pinned by a test.
    public struct BodyExport
    {
        public string BodyName;

        /// <summary>Whether to write the Properties block.</summary>
        public bool HasTilt;

        /// <summary>Write the pole as the legacy tiltx/tiltz pair rather than poleRA/poleDec.</summary>
        public bool LegacyTiltForm;

        /// <summary>Whether the pole is measured from the parent's equator.</summary>
        public bool TiltRelativeToParent;

        public double PoleRa;
        public double PoleDec;

        /// <summary>The body's spin angle at universal time zero, degrees.</summary>
        public double InitialRotation;

        /// <summary>Whether to write the Orbit block.</summary>
        public bool HasOrbit;

        /// <summary>
        /// Whether the three orientation elements are measured from the parent's equator, which
        /// is Tilt'Em's own relativeToParent flag rather than anything Kopernicus knows about.
        /// </summary>
        public bool OrbitRelativeToParent;

        public double Inclination;
        public double LongitudeOfAscendingNode;
        public double ArgumentOfPeriapsis;
        public double Eccentricity;
        public double SemiMajorAxis;

        /// <summary>Mean anomaly at epoch, degrees.</summary>
        public double MeanAnomalyAtEpochD;
    }

    /// <summary>
    /// Turns an edited body into a Kopernicus patch. Text only; see
    /// <see cref="EditExporter"/> for where it lands.
    /// </summary>
    public static class EditExport
    {
        /// <summary>Angles and eccentricity, which never need more than this to be reproduced.</summary>
        private const string AngleFormat = "F6";

        /// <summary>Distances, in metres.</summary>
        private const string DistanceFormat = "F3";

        /// <summary>Body_TimeStamp.cfg, with anything a file name cannot hold replaced.</summary>
        public static string FileName(string bodyName, DateTime timestampUtc)
        {
            return Sanitize(bodyName) + "_"
                   + timestampUtc.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".cfg";
        }

        public static string Build(BodyExport body, DateTime timestampUtc)
        {
            StringBuilder text = new StringBuilder();

            text.AppendLine("// Tilt'Em body edit for " + body.BodyName + ", exported "
                            + timestampUtc.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + " UTC.");
            text.AppendLine("//");
            text.AppendLine("// PluginData is not read as configuration, which is why exports land here rather");
            text.AppendLine("// than taking effect on their own. Move this file into a GameData folder of your");
            text.AppendLine("// own to apply it, and delete whatever you did not mean to keep.");
            text.AppendLine();

            //AFTER[TiltEm], so the values land on a body Kopernicus has already built and cannot
            //be read before Tilt'Em's own fields exist.
            text.AppendLine("@Kopernicus:AFTER[TiltEm]");
            text.AppendLine("{");
            text.AppendLine("    @Body[" + body.BodyName + "]");
            text.AppendLine("    {");

            if (body.HasTilt) AppendProperties(text, body);
            if (body.HasTilt && body.HasOrbit) text.AppendLine();
            if (body.HasOrbit) AppendOrbit(text, body);

            text.AppendLine("    }");
            text.AppendLine("}");

            return text.ToString();
        }

        //Every form of the pole is written the same way: put down the numbers, work out what the
        //game will rebuild from them, and take the difference between that and the pole the
        //editor actually has off initialRotation. Both forms lose a spin - the legacy pair gains
        //a prime meridian of its own, and poleRA is dropped outright at the pole - and neither
        //loss is visible in the file that caused it.
        private static void AppendProperties(StringBuilder text, BodyExport body)
        {
            BodyTilt edited = TiltEmFrames.FromPoleContinuous(body.PoleRa, body.PoleDec);

            text.AppendLine("        @Properties");
            text.AppendLine("        {");

            if (body.TiltRelativeToParent)
            {
                text.AppendLine("            // Pole measured from the parent's equator, not the celestial one.");
                text.AppendLine("            %tiltRelativeToParent = true");
            }

            BodyTilt written = body.LegacyTiltForm
                ? AppendLegacyTilt(text, edited)
                : AppendPole(text, body);

            Value(text, "initialRotation",
                Wrap(body.InitialRotation + TiltEmFrames.SpinOffset(edited, written)), AngleFormat);

            text.AppendLine("        }");
        }

        /// <summary>The pole under the keys TiltConfig prefers, and what they will rebuild as.</summary>
        private static BodyTilt AppendPole(StringBuilder text, BodyExport body)
        {
            //Percent, not at: a body that never had a pole written for it has no key to edit.
            Value(text, "poleRA", body.PoleRa, AngleFormat);
            Value(text, "poleDec", body.PoleDec, AngleFormat);

            return TiltEmFrames.FromPole(body.PoleRa, body.PoleDec);
        }

        /// <summary>The same pole written as the legacy Unity-Euler pair.</summary>
        private static BodyTilt AppendLegacyTilt(StringBuilder text, BodyTilt edited)
        {
            Vector3d legacy = TiltEmFrames.ToLegacyEuler(edited);

            text.AppendLine("            // The legacy pair loses to poleRA/poleDec wherever a body has both,");
            text.AppendLine("            // so any pole already written for this body has to go first.");
            text.AppendLine("            !poleRA = delete");
            text.AppendLine("            !poleDec = delete");
            Value(text, "tiltx", legacy.x, AngleFormat);
            Value(text, "tiltz", legacy.z, AngleFormat);

            return TiltEmFrames.FromLegacyEuler(legacy);
        }

        private static void AppendOrbit(StringBuilder text, BodyExport body)
        {
            text.AppendLine("        @Orbit");
            text.AppendLine("        {");

            if (body.OrbitRelativeToParent)
            {
                text.AppendLine("            // Orientation measured from the parent's equator, not the celestial one.");
                text.AppendLine("            %relativeToParent = true");
            }

            Value(text, "inclination", body.Inclination, AngleFormat);
            Value(text, "longitudeOfAscendingNode", body.LongitudeOfAscendingNode, AngleFormat);
            Value(text, "argumentOfPeriapsis", body.ArgumentOfPeriapsis, AngleFormat);
            Value(text, "eccentricity", body.Eccentricity, AngleFormat);
            Value(text, "semiMajorAxis", body.SemiMajorAxis, DistanceFormat);
            Value(text, "meanAnomalyAtEpochD", body.MeanAnomalyAtEpochD, AngleFormat);

            text.AppendLine("        }");
        }

        private static void Value(StringBuilder text, string key, double value, string format)
        {
            text.AppendLine("            %" + key + " = "
                            + value.ToString(format, CultureInfo.InvariantCulture));
        }

        private static double Wrap(double degrees)
        {
            double wrapped = degrees % 360.0;
            return wrapped < 0.0 ? wrapped + 360.0 : wrapped;
        }

        private static string Sanitize(string name)
        {
            StringBuilder safe = new StringBuilder(name.Length);

            foreach (char c in name)
            {
                safe.Append(char.IsLetterOrDigit(c) || c == '-' || c == '_' ? c : '_');
            }

            return safe.Length == 0 ? "Body" : safe.ToString();
        }
    }
}
