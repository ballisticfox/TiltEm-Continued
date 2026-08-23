using System;
using System.IO;
using System.Text.RegularExpressions;

namespace TiltEm.Verification
{
    /// <summary>
    /// Some defects are "this code should no longer exist" or "this call site should read a
    /// different field". Those cannot be asserted numerically, and the constructs involved
    /// (Transform.right, PhysicsGlobals) need a live Unity scene, so they are checked against
    /// the shipped sources instead. Crude, but it fails loudly if the code comes back.
    /// </summary>
    public static class SourceChecks
    {
        private static string _root;

        public static void Run(string repoRoot)
        {
            _root = repoRoot;

            TransitionMachineryIsGone();
            CbUpdateRestoresStockFidelity();
            FrameMathIsFloatFree();
            CamerasUseTheRightPole();
        }

        /// <summary>
        /// G2, G3. CameraChecks verifies the two pole expressions numerically; these pin the
        /// shipped call sites to them, because the failure mode is using the wrong one of the
        /// two - which compiles, runs, and looks nearly right.
        /// </summary>
        private static void CamerasUseTheRightPole()
        {
            var tiltEm = Read(Path.Combine("TiltEm", "TiltEm.cs"));

            Present("G2", "CelestialNorth reads the tilt's own pole", tiltEm,
                @"CelestialNorth[\s\S]{0,1200}?tilt\.Tilt\.Z\.xzy");
            Present("G3", "WorldNorth reads BodyFrame's pole", tiltEm,
                @"WorldNorth[\s\S]{0,1200}?body\.BodyFrame\.Z\.xzy");

            var mapCamera = Read(Path.Combine("TiltEm", "Harmony", "PlanetariumCamera_LateUpdate.cs"));
            Present("G2", "the map camera asks for the celestial pole", mapCamera, @"TiltEm\.CelestialNorth\s*\(");
            Absent("G2", "the map camera does not use the world pole", mapCamera, @"TiltEm\.WorldNorth\s*\(");

            var flightCamera = Read(Path.Combine("TiltEm", "Harmony", "FlightGlobals_GetFoR.cs"));
            Present("G3", "the orbital camera asks for the world pole", flightCamera, @"TiltEm\.WorldNorth\s*\(");
            Absent("G3", "the orbital camera does not use the celestial pole", flightCamera, @"TiltEm\.CelestialNorth\s*\(");
            Present("G3", "the camera patch touches only OBT_ABS and SRF_NORTH", flightCamera,
                @"mode\s*!=\s*FoRModes\.OBT_ABS\s*&&\s*mode\s*!=\s*FoRModes\.SRF_NORTH\s*\)\s*return");
            Absent("G3", "no other frame-of-reference mode is touched", flightCamera,
                @"FoRModes\.(?!OBT_ABS|SRF_NORTH)\w+");

            // The surface camera's east must be crossed against the pole, not the celestial axis.
            // That single substitution is the whole fix, and it reads almost identically to the
            // stock line it replaces.
            Present("G4", "the surface camera crosses the vertical against the pole", flightCamera,
                @"Vector3\.Cross\(north,\s*up\)");
            Absent("G4", "the surface camera no longer crosses against Vector3.up", flightCamera,
                @"Vector3\.Cross\(Vector3\.up");
        }

        /// <summary>
        /// C1, C2, C3, C4, and the A3 fix-up. The vessel-moving code existed only to compensate
        /// for the discontinuity; with the frames continuous there is nothing to compensate, so
        /// all of it goes rather than being corrected in place.
        /// </summary>
        private static void TransitionMachineryIsGone()
        {
            var mod = ReadAllModSources();

            Absent("C1", "no selective TRACK_Phys vessel loop", mod, @"TRACK_Phys");
            Absent("C1", "no iteration over loaded vessels", mod, @"VesselsLoaded");
            Absent("C2", "no SetPosition call (CoM/root mismatch)", mod, @"\.SetPosition\s*\(");
            Absent("C3", "no HoldVesselUnpack call", mod, @"HoldVesselUnpack");
            Absent("C3", "no GoOnRails call", mod, @"GoOnRails");
            Absent("C4", "no rotating-frame transition handler dereferencing data.host", mod, @"data\.host");
            // Reading OrbitFrame for the debug readout is fine; the defect was mutating it.
            Absent("A3", "no ApplyTiltToFrame helper", mod, @"ApplyTiltToFrame");
            Absent("A3", "OrbitFrame is never written or passed by ref", mod,
                @"OrbitFrame\s*=|ref\s+[\w\.]*OrbitFrame");
            Absent("A3", "no onRotatingFrameTransition subscription", mod, @"onRotatingFrameTransition");

            Missing("A2", "TiltEmUtil (mode-conditional tilt shuffling) is deleted",
                !File.Exists(Path.Combine(_root, "TiltEm", "TiltEmUtil.cs")));
            Absent("A2", "no RestorePlanetTilt / RestorePlanetariumTilt", mod, @"RestorePlanet");
        }

        /// <summary>D2, D3. Lines the replacement CBUpdate used to drop or hardcode.</summary>
        private static void CbUpdateRestoresStockFidelity()
        {
            var cbUpdate = Read(Path.Combine("TiltEm", "Harmony", "CelestialBody_CBUpdate.cs"));

            Present("D2", "CBUpdate sets transformRight", cbUpdate, @"transformRight\s*=");
            Present("D2", "CBUpdate sets transformUp", cbUpdate, @"transformUp\s*=");

            Present("D3", "CBUpdate uses PhysicsGlobals.GravitationalAcceleration",
                cbUpdate, @"PhysicsGlobals\.GravitationalAcceleration");
            Absent("D3", "CBUpdate no longer hardcodes 9.80665", cbUpdate, @"9\.80665");
        }

        /// <summary>
        /// D1. The frame path must never touch UnityEngine.Quaternion, which is single
        /// precision. QuaternionD is fine; the regex excludes it.
        /// </summary>
        private static void FrameMathIsFloatFree()
        {
            var frames = Read(Path.Combine("TiltEm", "TiltEmFrames.cs"));
            var cbUpdate = Read(Path.Combine("TiltEm", "Harmony", "CelestialBody_CBUpdate.cs"));
            var zupAtT = Read(Path.Combine("TiltEm", "Harmony", "Planetarium_ZupAtT.cs"));

            // "Quaternion" not followed by "D", and not inside a comment reference.
            const string floatQuaternion = @"(?<!\w)Quaternion(?!D)\s*[\.\(]";

            Absent("D1", "TiltEmFrames uses no float Quaternion", StripComments(frames), floatQuaternion);
            Absent("D1", "CBUpdate patch uses no float Quaternion", StripComments(cbUpdate), floatQuaternion);
            Absent("D1", "ZupAtT patch uses no float Quaternion", StripComments(zupAtT), floatQuaternion);
            Absent("D1", "no float Vector3 in the frame maths", StripComments(frames), @"(?<!\w)Vector3(?!d)\s*[\.\(]");
        }

        #region Helpers

        private static string Read(string relativePath)
        {
            return File.ReadAllText(Path.Combine(_root, relativePath));
        }

        /// <summary>Concatenates every production .cs file, so a check covers the whole mod.</summary>
        private static string ReadAllModSources()
        {
            var text = "";
            foreach (var dir in new[] { "TiltEm", "TiltEmKopernicus" })
            {
                var path = Path.Combine(_root, dir);
                if (!Directory.Exists(path)) continue;

                foreach (var file in Directory.GetFiles(path, "*.cs", SearchOption.AllDirectories))
                {
                    // Skip build output.
                    if (file.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar)) continue;
                    if (file.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar)) continue;

                    text += StripComments(File.ReadAllText(file)) + "\n";
                }
            }

            return text;
        }

        /// <summary>
        /// Comments discuss the removed code on purpose, so strip them before scanning.
        /// </summary>
        private static string StripComments(string source)
        {
            source = Regex.Replace(source, @"/\*.*?\*/", "", RegexOptions.Singleline);
            source = Regex.Replace(source, @"//.*?$", "", RegexOptions.Multiline);
            return source;
        }

        private static void Absent(string defect, string name, string haystack, string pattern)
        {
            var match = Regex.Match(haystack, pattern);
            Harness.Check(defect, name, !match.Success,
                match.Success ? "still present: \"" + match.Value.Trim() + "\"" : null);
        }

        private static void Present(string defect, string name, string haystack, string pattern)
        {
            Harness.Check(defect, name, Regex.IsMatch(haystack, pattern), null);
        }

        private static void Missing(string defect, string name, bool condition)
        {
            Harness.Check(defect, name, condition, null);
        }

        #endregion

        /// <summary>Walks up from the harness binary until it finds the solution file.</summary>
        public static string FindRepoRoot()
        {
            var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "TiltEm.sln")))
            {
                dir = dir.Parent;
            }

            if (dir == null)
            {
                throw new InvalidOperationException("Could not locate TiltEm.sln above " + AppDomain.CurrentDomain.BaseDirectory);
            }

            return dir.FullName;
        }
    }
}
