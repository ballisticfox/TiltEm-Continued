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
            ParentRelativeOrbitsAreWiredCorrectly();
            ElementReadoutsAreWiredCorrectly();
            TheAnchoredZupIsKeyedToItsOwnBody();
            TheRotatingFrameSurvivesAnSoiChange();
        }

        /// <summary>
        /// L1/L2/L3. The simulation can only exercise logic the harness itself restates, so these
        /// pin both halves of the fix in the shipped sources: the entitlement gate in CBUpdate,
        /// and the handover that ends the outgoing body's frame on a sphere-of-influence change.
        /// </summary>
        private static void TheRotatingFrameSurvivesAnSoiChange()
        {
            var cbUpdate = StripComments(Read(Path.Combine("TiltEm", "Harmony", "CelestialBody_CBUpdate.cs")));
            var tiltEm = StripComments(Read(Path.Combine("TiltEm", "TiltEm.cs")));
            var handover = StripComments(Read(Path.Combine("TiltEm", "Harmony", "OrbitPhysicsManager_SetDominantBody.cs")));

            Present("L2", "CBUpdate gates the rotating branch on entitlement", cbUpdate,
                @"body\.inverseRotation && TiltEm\.MayHoldRotatingFrame\(body\)");
            Present("L2", "the dominant body is the authority when there is one", tiltEm,
                @"OrbitPhysicsManager\.DominantBody");
            Present("L3", "and an absent one falls back to the anchor holder", tiltEm,
                @"ZupAnchorBody == null \|\| ReferenceEquals\(ZupAnchorBody, body\)");

            //The outgoing body has to be read before the original runs; setDominantBody assigns
            //dominantBody on its first line and its own event reports the new body as "from".
            Present("L1", "the outgoing body is captured in a prefix", handover,
                @"HarmonyPrefix[\s\S]{0,240}out CelestialBody __state");
            Present("L1", "the handover ends the outgoing rotating frame", handover,
                @"outgoing\.inverseRotation = false");
            Present("L1", "and releases its anchor whether or not it cleared the flag", handover,
                @"TiltEm\.ReleaseZupAnchor\(outgoing\)");
        }

        /// <summary>
        /// K1. TeleportChecks shows what an anchor from the wrong body does - 839 km of it - but
        /// only by suppressing the re-anchor inside the simulation. The shipped hole was in
        /// ZupAtT, which took the caller's body and the global anchor and combined them without
        /// checking they referred to the same body, so this pins the fix.
        /// </summary>
        private static void TheAnchoredZupIsKeyedToItsOwnBody()
        {
            var zupAtT = StripComments(Read(Path.Combine("TiltEm", "Harmony", "Planetarium_ZupAtT.cs")));

            Present("K1", "ZupAtT resolves the body that owns the anchor", zupAtT,
                @"TiltEm\.ZupAnchorBody");

            //The elapsed rotation has to come from the anchor's body, since that is the rotation
            //Zup is actually following. Advancing the caller's instead is the 839 km failure.
            Present("K1", "and advances that body's rotation, not the caller's", zupAtT,
                @"RotationAngleAt\(anchorBody,");
            Absent("K1", "the caller's own rotation is not used to advance Zup", zupAtT,
                @"RotationAngleAt\(body,");
        }

        /// <summary>
        /// J1. DisplayChecks proves the conversion both ways; these pin the two call sites to it,
        /// because the failure mode is subtle in a way the maths cannot catch.
        ///
        /// The readout and the input have to use OPPOSITE directions of the same conversion. Both
        /// signatures take a tilt and a set of elements and return a set of elements, so swapping
        /// them compiles and runs and produces plausible-looking numbers - it just doubles the
        /// obliquity instead of removing it, and only on a tilted body. Pinning each site to the
        /// direction it needs is the cheapest way to keep that from coming back.
        /// </summary>
        private static void ElementReadoutsAreWiredCorrectly()
        {
            var shared = StripComments(Read(Path.Combine("TiltEm", "ParentRelativeOrbit.cs")));

            //A readout holds a celestial orbit and needs the parent-relative numbers, so it
            //takes the tilt off; an input holds what the player typed and needs it put on.
            Present("J1", "the readout helper converts out of the celestial frame", shared,
                @"TiltEmFrames\.ToLocalElements\(");
            Present("J1", "the input helper converts into it", shared,
                @"TiltEmFrames\.ToCelestialElements\(");

            //Both directions return false for an untilted parent, which is what keeps a stock
            //install bit-identical rather than merely indistinguishable.
            Present("J1", "an untilted parent short-circuits the readout", shared,
                @"return !tilt\.IsIdentity");
            Present("J1", "and the input", shared, @"tilt\.IsIdentity\) return false");

            var editor = StripComments(Read(Path.Combine("TiltEm", "Harmony",
                "ManeuverNodeEditorTabOrbitAdv_UpdateUIElements.cs")));

            Present("J1", "the maneuver editor patch reads the parent-relative elements", editor,
                @"ParentRelativeOrbit\.TryGetLocalElements\(");
            Absent("J1", "the maneuver editor patch does not convert the wrong way", editor,
                @"TryGetCelestialElements");

            //Read rather than re-derived: if the patch picked its own orbit it could end up
            //relabelling numbers that describe a different one than stock just printed.
            Present("J1", "it corrects the orbit stock actually displayed", editor,
                @"FieldRefAccess<ManeuverNodeEditorTabOrbitAdv, Orbit>\(""orbitToDisplay""\)");

            var setOrbit = StripComments(Read(Path.Combine("TiltEm", "Harmony",
                "FlightGlobals_SetShipOrbit.cs")));

            Present("J1", "the Set Orbit patch converts the entered elements", setOrbit,
                @"ParentRelativeOrbit\.TryGetCelestialElements\(");
            Absent("J1", "the Set Orbit patch does not convert the wrong way", setOrbit,
                @"TryGetLocalElements");

            //Only the three orientation elements change frame. Rewriting the shape or the
            //position along the orbit would move the craft somewhere it was not asked to go.
            Present("J1", "it rewrites the three orientation elements", setOrbit,
                @"ref double inc, ref double LAN, ref double argPe");
            Absent("J1", "and leaves shape and position alone", setOrbit,
                @"ref double (ecc|sma|mna|ObT)");
        }

        /// <summary>
        /// I1. OrbitFrameChecks proves the conversion; these pin the two things about the wiring
        /// that the maths cannot see.
        ///
        /// First, that both readers of a body's tilt go through TiltConfig. If they diverged on
        /// the pole-over-legacy-Euler precedence, a moon would be placed against one reading of
        /// its parent's pole and then lit by another - a discrepancy that would look like a
        /// physics bug rather than a config one.
        ///
        /// Second, that the rebase happens on the prefab. Parsing the Orbit node is too early
        /// (the parent's Properties may not have been read yet) and the live system is too late
        /// (the orbit has been Init'd), so OnPostLoad is the only correct point.
        /// </summary>
        private static void ParentRelativeOrbitsAreWiredCorrectly()
        {
            var tiltConfig = Read(Path.Combine("TiltEmKopernicus", "TiltConfig.cs"));
            Present("I1", "TiltConfig reads the pole form first", tiltConfig, @"Has\(""poleRA""\)");
            Present("I1", "TiltConfig falls back to the legacy Euler form", tiltConfig, @"Has\(""tiltx""\)");

            foreach (var name in new[] { "KopernicusLoader.cs", "OrbitFrameLoader.cs" })
            {
                var text = StripComments(Read(Path.Combine("TiltEmKopernicus", name)));

                // TryReadEffective, not TryRead: tiltRelativeToParent changes what a given
                // poleRA/poleDec pair means, so a caller on the raw read would be working from a
                // different pole than the game is.
                Present("I2", name + " reads the tilt through TryReadEffective", text,
                    @"TiltConfig\.TryReadEffective");
                Absent("I2", name + " does not use the raw read", text, @"TiltConfig\.TryRead\s*\(");
                Absent("I1", name + " does not read the tilt keys itself", text,
                    @"""poleRA""|""poleDec""|""tiltx""|""tiltz""");
            }

            Present("I2", "tiltRelativeToParent is declared on the Properties node",
                Read(Path.Combine("TiltEmKopernicus", "TiltReader.cs")),
                @"ParserTarget\(""tiltRelativeToParent""\)");
            Present("I2", "the rebase composes the parent's tilt onto the body's", tiltConfig,
                @"TiltEmFrames\.ToCelestialTilt\(parentTilt,");
            Present("I2", "the walk up the tree is recursive, so a moon resolves through its giant",
                tiltConfig, @"TryReadEffective\(parent, depth \+ 1");
            Present("I2", "a body with no parent is warned about, not silently rebased", tiltConfig,
                @"ReferenceEquals\(parent, body\)[\s\S]{0,300}?LogWarning");

            var reader = Read(Path.Combine("TiltEmKopernicus", "OrbitReader.cs"));
            Present("I1", "relativeToParent is declared on the Orbit node", reader,
                @"ParserTargetExternal\(""Body"",\s*""Orbit""");
            Present("I1", "relativeToParent is a ParserTarget", reader, @"ParserTarget\(""relativeToParent""\)");

            var loader = StripComments(Read(Path.Combine("TiltEmKopernicus", "OrbitFrameLoader.cs")));
            Present("I1", "the rebase runs on the system prefab, via OnPostLoad", loader,
                @"Events\.OnPostLoad\.Add");
            Absent("I1", "the rebase does not re-Init a live orbit", loader, @"\.Init\s*\(\s*\)");
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

            // G5. The tree walk and the key gating need a live game, so pin them here.
            var tiltEmSrc = StripComments(tiltEm);

            Present("G5", "SystemNorth walks up to the star", tiltEmSrc, @"StarFor\s*\(");
            Present("G5", "the walk terminates on a self-referencing parent", tiltEmSrc,
                @"ReferenceEquals\(parent,\s*current\)");
            Present("G5", "an unconfigured plane falls back to the star's pole", tiltEmSrc,
                @"TryGetOrbitalPlane[\s\S]{0,200}?return CelestialNorth\(star\)");
            Present("G5", "the toggle is gated on the map being up", tiltEmSrc,
                @"MapViewIsUp\(\)\s*\)\s*return");
            Present("G5", "and on camera controls being unlocked", tiltEmSrc,
                @"IsUnlocked\(ControlTypes\.CAMERACONTROLS\)");

            var mapCam = StripComments(Read(Path.Combine("TiltEm", "Harmony", "PlanetariumCamera_LateUpdate.cs")));
            Present("G5", "the map camera honours the rotation mode", mapCam,
                @"MapCameraRotation\.SystemUp[\s\S]{0,120}?TiltEm\.SystemNorth");
            Present("G5", "and still uses the body pole in the other mode", mapCam,
                @"TiltEm\.CelestialNorth\s*\(");

            // G6. The ease itself is verified numerically; these pin the two decisions in it that
            // a numeric check cannot see.
            Present("G6", "the up axis is eased, not taken raw", mapCam, @"TiltEm\.SmoothMapNorth\s*\(");
            Present("G6", "the ease is frame-rate independent, not k * dt", tiltEmSrc,
                @"1f\s*-\s*Mathf\.Exp\(-MapNorthSharpness\s*\*\s*Time\.unscaledDeltaTime\)");
            Present("G6", "directions are slerped, not lerped", tiltEmSrc, @"Vector3\.Slerp\(_mapNorth");
            Present("G6", "a scene change adopts the new axis outright", tiltEmSrc,
                @"ResetMapNorth\(\);[\s\S]{0,400}?data\.to\s*<\s*GameScenes\.SPACECENTER");

            var reader = Read(Path.Combine("TiltEmKopernicus", "TiltConfig.cs"));
            Present("G5", "TiltConfig reads the orbital plane pole form first", reader,
                @"Has\(""orbitalPlaneRA""\)");
            Present("G5", "and falls back to the legacy pair", reader, @"Has\(""orbitalPlaneX""\)");

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
