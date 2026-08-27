using System;
using System.IO;
using UnityEngine;

namespace TiltEm
{
    /// <summary>
    /// Writes an edited body out as a Kopernicus patch, under GameData/TiltEm/PluginData.
    /// </summary>
    //PluginData on purpose: KSP does not read configuration from it, so an export sits there
    //inert until the player moves it somewhere it will be read. An editor that quietly started
    //applying its own files would be a different feature.
    public static class EditExporter
    {
        /// <summary>Where exports land.</summary>
        public static string Directory => Path.Combine(KSPUtil.ApplicationRootPath, "GameData/TiltEm/PluginData");


        /// <summary>
        /// Writes one body's edit. Returns the full path on success, or null when nothing was
        /// edited or the write failed.
        /// </summary>
        public static string Export(BodyEdit edit)
        {
            if (edit == null || !edit.Dirty) return null;

            DateTime timestamp = DateTime.UtcNow;
            string path = Path.Combine(Directory, EditExport.FileName(edit.Body.bodyName, timestamp));

            try
            {
                System.IO.Directory.CreateDirectory(Directory);
                File.WriteAllText(path, EditExport.Build(Describe(edit), timestamp));
            }
            catch (Exception e)
            {
                Debug.LogError("[TiltEm]: could not write the body edit for " + edit.Body.bodyName
                               + " to " + path + ": " + e);
                return null;
            }

            return path;
        }

        /// <summary>The values one body's export writes.</summary>
        //Only what actually moved. A body whose orbit was left alone should not have its elements
        //written back at full precision, where the next Kopernicus change would silently pin them.
        public static BodyExport Describe(BodyEdit edit)
        {
            BodyExport export = default;

            export.BodyName = edit.Body.bodyName;

            export.HasTilt = edit.Tilt.Dirty;
            export.LegacyTiltForm = edit.Tilt.Mode == TiltEditMode.Tilt;
            export.TiltRelativeToParent = edit.Tilt.RelativeToParent;
            export.PoleRa = edit.Tilt.PoleRa;
            export.PoleDec = edit.Tilt.PoleDec;
            export.InitialRotation = edit.Tilt.InitialRotation;

            if (edit.Orbit == null || !edit.Orbit.Dirty) return export;

            TiltEmFrames.OrbitElements orientation = edit.Orbit.Orientation;

            export.HasOrbit = true;
            export.OrbitRelativeToParent = edit.Orbit.RelativeToParent;
            export.Inclination = orientation.Inclination;
            export.LongitudeOfAscendingNode = orientation.LongitudeOfAscendingNode;
            export.ArgumentOfPeriapsis = orientation.ArgumentOfPeriapsis;
            export.Eccentricity = edit.Orbit.Eccentricity;
            export.SemiMajorAxis = edit.Orbit.SemiMajorAxis;

            //Kopernicus takes the radian form under meanAnomalyAtEpoch and the degree form under
            //meanAnomalyAtEpochD. Degrees, so the number in the file matches the one in the UI.
            export.MeanAnomalyAtEpochD = edit.Orbit.MeanAnomalyAtEpoch * (180.0 / Math.PI);

            return export;
        }
    }
}
