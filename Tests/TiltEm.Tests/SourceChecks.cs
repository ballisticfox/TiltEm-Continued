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
            PrincipiaTurnsTheModOff();
        }

        /// <summary>
        /// Principia rewrites the same reference frames Tilt'Em does, so every entry point has
        /// to stand down when it is installed. Nothing numeric can see this, and a half-disabled
        /// mod is worse than either alone, so the three gates are pinned here.
        /// </summary>
        private static void PrincipiaTurnsTheModOff()
        {
            var check = StripComments(Read(Path.Combine("TiltEm", "Loader", "PrincipiaCheck.cs")));

            // The DLL name, not a KSPAssembly name: Principia declares no KSPAssembly attribute,
            // so KSP falls back to the file name.
            Present("-", "Principia is detected by its adapter assembly", check,
                @"""principia\.ksp_plugin_adapter""");
            Present("-", "the detection reads KSP's loaded assemblies", check,
                @"AssemblyLoader\.loadedAssemblies");
            Present("-", "and matches on the DLL name", check, @"\.dllName\s*==");

            var gated = new[]
            {
                new[] { "TiltEm.cs", @"HarmonyInstance\.PatchAll" },
                new[] { "Loader" + Path.DirectorySeparatorChar + "TiltLoader.cs", @"AddTiltData" },
                new[] { "Loader" + Path.DirectorySeparatorChar + "OrbitFrameLoader.cs", @"Events\.OnPostLoad\.Add" },
                // The editor writes poles and orbits straight into the running game, which is the
                // last thing anyone wants happening under a mod that owns the same frames.
                new[] { "Editor" + Path.DirectorySeparatorChar + "BodyEditor.cs", @"Begin\(CelestialBody" },
            };

            foreach (var entry in gated)
            {
                var text = StripComments(Read(Path.Combine("TiltEm", entry[0])));

                // Ordering matters, not just presence: the guard has to come before the work.
                Present("-", entry[0] + " stands down when Principia is installed", text,
                    @"PrincipiaCheck\.Installed[\s\S]*?" + entry[1]);
            }
        }

        /// <summary>
        /// L1/L2/L3. The simulation can only exercise logic the harness itself restates, so these
        /// pin both halves of the fix in the shipped sources: the entitlement gate in CBUpdate,
        /// and the handover that ends the outgoing body's frame on a sphere-of-influence change.
        /// </summary>
        private static void TheRotatingFrameSurvivesAnSoiChange()
        {
            var cbUpdate = StripComments(Read(Path.Combine("TiltEm", "Harmony", "Frames", "CelestialBody_CBUpdate.cs")));
            var anchor = StripComments(Read(Path.Combine("TiltEm", "Frames", "PlanetariumAnchor.cs")));
            var handover = StripComments(Read(Path.Combine("TiltEm", "Harmony", "Frames", "OrbitPhysicsManager_SetDominantBody.cs")));

            Present("L2", "CBUpdate gates the rotating branch on entitlement", cbUpdate,
                @"body\.inverseRotation && PlanetariumAnchor\.MayHoldRotatingFrame\(body\)");
            Present("L2", "the dominant body is the authority when there is one", anchor,
                @"OrbitPhysicsManager\.DominantBody");
            Present("L3", "and an absent one falls back to the anchor holder", anchor,
                @"ZupAnchorBody == null \|\| ReferenceEquals\(ZupAnchorBody, body\)");

            //The outgoing body has to be read before the original runs; setDominantBody assigns
            //dominantBody on its first line and its own event reports the new body as "from".
            Present("L1", "the outgoing body is captured in a prefix", handover,
                @"HarmonyPrefix[\s\S]{0,240}out CelestialBody __state");
            Present("L1", "the handover ends the outgoing rotating frame", handover,
                @"outgoing\.inverseRotation = false");
            Present("L1", "and releases its anchor whether or not it cleared the flag", handover,
                @"PlanetariumAnchor\.ReleaseZupAnchor\(outgoing\)");
        }

        /// <summary>
        /// K1. TeleportChecks shows what an anchor from the wrong body does - 839 km of it - but
        /// only by suppressing the re-anchor inside the simulation. The shipped hole was in
        /// ZupAtT, which took the caller's body and the global anchor and combined them without
        /// checking they referred to the same body, so this pins the fix.
        /// </summary>
        private static void TheAnchoredZupIsKeyedToItsOwnBody()
        {
            var zupAtT = StripComments(Read(Path.Combine("TiltEm", "Harmony", "Frames", "Planetarium_ZupAtT.cs")));

            Present("K1", "ZupAtT resolves the body that owns the anchor", zupAtT,
                @"PlanetariumAnchor\.ZupAnchorBody");

            //The elapsed rotation has to come from the anchor's body, since that is the rotation
            //Zup is actually following. Advancing the caller's instead is the 839 km failure.
            Present("K1", "and advances that body's rotation, not the caller's", zupAtT,
                @"RotationAngleAt\(anchorBody,");
            Absent("K1", "the caller's own rotation is not used to advance Zup", zupAtT,
                @"RotationAngleAt\(body,");

            //M4. An anchor is a frame paired with the angle it was captured at, and with none
            //latched the two come from different places: ZupAnchor is whatever ResetZupAnchor
            //left behind, ZupAnchorRotationAngle a plain zero. The unlatched case therefore has
            //to derive both, not read the stored pair.
            Present("M4", "an unlatched anchor is derived rather than read", zupAtT,
                @"if \(latched == null\)");
            Present("M4", "and derived the way CBUpdate derives it", zupAtT,
                @"anchor = TiltEmFrames\.AnchorFor\(tilt, body\.rotationAngle, body\.BodyFrame");
            Present("M4", "paired with the angle that anchor was taken at", zupAtT,
                @"anchorRotationAngle = body\.rotationAngle");
            Absent("M4", "the stored pair is not used directly", zupAtT,
                @"TiltEmFrames\.Zup\(PlanetariumAnchor\.ZupAnchor,");
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
            var shared = StripComments(Read(Path.Combine("TiltEm", "Frames", "ParentRelativeOrbit.cs")));

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

            var editor = StripComments(Read(Path.Combine("TiltEm", "Harmony", "UI",
                "ManeuverNodeEditorTabOrbitAdv_UpdateUIElements.cs")));

            Present("J1", "the maneuver editor patch reads the parent-relative elements", editor,
                @"ParentRelativeOrbit\.TryGetLocalElements\(");
            Absent("J1", "the maneuver editor patch does not convert the wrong way", editor,
                @"TryGetCelestialElements");

            //Read rather than re-derived: if the patch picked its own orbit it could end up
            //relabelling numbers that describe a different one than stock just printed.
            Present("J1", "it corrects the orbit stock actually displayed", editor,
                @"FieldRefAccess<ManeuverNodeEditorTabOrbitAdv, Orbit>\(""orbitToDisplay""\)");

            var setOrbit = StripComments(Read(Path.Combine("TiltEm", "Harmony", "UI",
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
            var tiltConfig = Read(Path.Combine("TiltEm", "Config", "TiltConfig.cs"));
            Present("I1", "TiltConfig prefers the pole form over the legacy Euler form", tiltConfig,
                @"TryReadPole\(body, ""poleRA"", ""poleDec"", ""tiltx"", ""tiltz""");

            foreach (var name in new[] { "TiltLoader.cs", "OrbitFrameLoader.cs" })
            {
                var text = StripComments(Read(Path.Combine("TiltEm", "Loader", name)));

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
                Read(Path.Combine("TiltEm", "Config", "TiltReader.cs")),
                @"ParserTarget\(""tiltRelativeToParent""\)");
            Present("I2", "the rebase composes the parent's tilt onto the body's", tiltConfig,
                @"TiltEmFrames\.ToCelestialTilt\(parentTilt,");
            Present("I2", "the walk up the tree is recursive, so a moon resolves through its giant",
                tiltConfig, @"TryReadEffective\(parent, depth \+ 1");
            Present("I2", "a body with no parent is warned about, not silently rebased", tiltConfig,
                @"ReferenceEquals\(parent, body\)[\s\S]{0,300}?LogWarning");

            var reader = Read(Path.Combine("TiltEm", "Config", "OrbitReader.cs"));
            Present("I1", "relativeToParent is declared on the Orbit node", reader,
                @"ParserTargetExternal\(""Body"",\s*""Orbit""");
            Present("I1", "relativeToParent is a ParserTarget", reader, @"ParserTarget\(""relativeToParent""\)");

            var loader = StripComments(Read(Path.Combine("TiltEm", "Loader", "OrbitFrameLoader.cs")));
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
            var bodyAxes = StripComments(Read(Path.Combine("TiltEm", "Camera", "BodyAxes.cs")));

            Present("G2", "CelestialNorth reads the tilt's own pole", bodyAxes,
                @"CelestialNormal[\s\S]{0,600}?return tilt\.Tilt\.Z;");
            Present("G2", "and hands it back swizzled into Unity's axes", bodyAxes,
                @"CelestialNorth\(CelestialBody body\)[\s\S]{0,200}?CelestialNormal\(body\)\.xzy");
            Present("G3", "WorldNorth reads BodyFrame's pole", bodyAxes,
                @"WorldNorth[\s\S]{0,1200}?body\.BodyFrame\.Z\.xzy");

            var mapCamera = StripComments(Read(Path.Combine("TiltEm", "Harmony", "Camera", "PlanetariumCamera_LateUpdate.cs")));
            Present("G2", "the map camera asks for the celestial pole", mapCamera, @"BodyAxes\.CelestialNorth\s*\(");
            Absent("G2", "the map camera does not use the world pole", mapCamera, @"BodyAxes\.WorldNorth\s*\(");

            // G5. The tree walk and the key gating need a live game, so pin them here.
            var tiltEmSrc = StripComments(tiltEm);
            var mapNorth = StripComments(Read(Path.Combine("TiltEm", "Camera", "MapCamera.cs")));

            Present("G5", "SystemNorth walks up to the star", bodyAxes, @"StarFor\s*\(");
            Present("G5", "the walk terminates on a self-referencing parent", bodyAxes,
                @"ReferenceEquals\(parent,\s*current\)");
            Present("G5", "an unconfigured plane falls back to the star's pole", bodyAxes,
                @"TryGetOrbitalPlane[\s\S]{0,200}?CelestialNormal\(star\)");
            Present("G5", "the toggle is gated on the map being up", tiltEmSrc,
                @"MapViewIsUp\(\)\s*\)\s*return");
            Present("G5", "and on camera controls being unlocked", tiltEmSrc,
                @"IsUnlocked\(ControlTypes\.CAMERACONTROLS\)");

            var mapCam = StripComments(Read(Path.Combine("TiltEm", "Harmony", "Camera", "PlanetariumCamera_LateUpdate.cs")));
            Present("G5", "the map camera honours the rotation mode", mapCam,
                @"MapCameraRotation\.SystemUp[\s\S]{0,120}?BodyAxes\.SystemNorth");
            Present("G5", "and still uses the body pole in the other mode", mapCam,
                @"BodyAxes\.CelestialNorth\s*\(");

            //G9. The System flight camera. Stock's Modes enum cannot grow a sixth value, so the
            //mode is Orbital plus a flag, and everything that makes it a mode rather than a
            //latent state is arrangement rather than arithmetic: where it sits in the cycle, what
            //takes it away again, and which of the two frames it reaches.
            var flightFoR = StripComments(Read(Path.Combine("TiltEm", "Harmony", "Camera", "FlightGlobals_GetFoR.cs")));
            var systemMode = StripComments(Read(Path.Combine("TiltEm", "Harmony", "Camera", "FlightCamera_SystemMode.cs")));
            var flightFrame = StripComments(Read(Path.Combine("TiltEm", "Camera", "FlightCameraFrame.cs")));

            Present("G9", "the flight camera can take the system plane", flightFoR,
                @"FlightCameraFrame\.SystemUp[\s\S]{0,120}?BodyAxes\.WorldSystemNorth");
            //The orbital frame only. The surface frame is about which way is north on the ground,
            //which the plane of the system has nothing to say about.
            Present("G9", "and only for the orbital frame", flightFoR,
                @"mode\s*==\s*FoRModes\.OBT_ABS\s*&&\s*FlightCameraFrame\.SystemUp");
            Present("G9", "the world normal is used in flight, not the celestial one", flightFoR,
                @"WorldSystemNorth");
            Absent("G9", "and the flight camera never takes the celestial form", flightFoR,
                @"BodyAxes\.SystemNorth\s*\(");

            Present("G9", "the extra step sits after Orbital", systemMode,
                @"mode\s*!=\s*FlightCamera\.Modes\.ORBITAL\s*\|\|\s*FlightCameraFrame\.SystemUp[\s\S]{0,80}?return true");
            Present("G9", "and re-enters the mode so the frame lerps rather than cuts", systemMode,
                @"setMode\(FlightCamera\.Modes\.ORBITAL\)");
            //Last, after both the re-entry and the restore have been let through: whatever is
            //left is the player choosing a different mode.
            Present("G9", "any other mode change drops the system frame", systemMode,
                @"SystemCameraMode\.Switching[\s\S]{0,700}?FlightCameraFrame\.SystemUp = false");
            //But not a restore. Coming back from map view re-asserts the mode the vessel was
            //saved in, which is Orbital, and treating that as a choice would take the mode away.
            Present("G9", "re-asserting the same mode keeps the system frame", systemMode,
                @"m\s*==\s*__state\s*&&\s*FlightCameraFrame\.SystemUp");
            Present("G9", "which needs the mode setMode is about to overwrite", systemMode,
                @"out FlightCamera\.Modes __state[\s\S]{0,120}?__state = __instance\.mode");
            Present("G9", "a scene change drops it too", tiltEmSrc, @"FlightCameraFrame\.Reset\(\)");

            //Stock's own message object, rewritten and re-posted. PostMessage rewrites a message
            //that is still on screen in place, which is what keeps mode changes on one line; and
            //removing it first would destroy its text object through Object.Destroy, deferred to
            //the end of the frame, leaving a live-looking reference to write into and lose.
            Present("G9", "the readout rewrites stock's own message", systemMode,
                @"readout\.message = Localizer\.Format[\s\S]{0,120}?PostScreenMessage\(readout\)");
            Absent("G9", "and never removes it first", systemMode, @"RemoveMessage");

            //Same rule for the map rotation key, which writes the same kind of line.
            Present("G6", "the rotation key reuses one message so its line is replaced", mapNorth,
                @"Readout\.message = text[\s\S]{0,80}?PostScreenMessage\(Readout\)");
            Absent("G6", "and never posts a bare string, which would stack a new line", mapNorth,
                @"PostScreenMessage\(""");

            //Stock wraps every mode name in "Camera: <<1>>" and writes the names in caps, so the
            //one this adds has to arrive the same way or it reads as a different kind of message.
            Present("G9", "the readout uses stock's own Camera: wrapper", systemMode,
                @"Localizer\.Format\(""#autoLOC_133776"", Name\)");
            Present("G9", "and a localisable name that falls back to English", systemMode,
                @"TryGetStringByTag\(""#autoLOC_TiltEm_CameraSystem""[\s\S]{0,120}?""SYSTEM""");
            Present("G9", "which the shipped localisation carries", 
                Read(Path.Combine("Resources", "Localization", "en-us", "TiltEmLoc.cfg")),
                @"#autoLOC_TiltEm_CameraSystem\s*=\s*SYSTEM");
            Present("G9", "the mode is session state, not something a save carries", flightFrame,
                @"public static bool SystemUp");
            //Stock leaves Orbital below two kilometres, and Auto swaps what it resolves to
            //without calling setMode at all. Reading the mode back covers both without a hook.
            Present("G9", "and cannot outlive the Orbital mode it rides on", flightFrame,
                @"_systemUp && IsOrbital\(\)[\s\S]{0,300}?mode == FlightCamera\.Modes\.ORBITAL");

            //G7. The correction has to be substituted where stock computes endRot, not applied
            //after LateUpdate has finished. A postfix samples the pitch axis after
            //transform.localRotation has been refreshed, where stock samples it before, and it
            //moves the camera at the end of the method - which KSPCommunityFixes'
            //OptimisedVectorLines cannot see, its projection matrix being cached once per frame
            //on the first orbit-line projection. Orbit lines then lag the bodies whenever the
            //camera moves.
            Present("G7", "the map camera correction is transpiled in", mapCam,
                @"\[HarmonyTranspiler\]");
            Present("G7", "it substitutes stock's own AngleAxis call", mapCam,
                @"nameof\(Quaternion\.AngleAxis\)");
            Present("G7", "and rebuilds the heading from camHdg rather than the passed angle", mapCam,
                @"camera\.camHdg\s*\*\s*Mathf\.Rad2Deg");
            Absent("G7", "the pivot is not written after LateUpdate returns", mapCam,
                @"\[HarmonyPostfix\]");
            Absent("G7", "and the pivot is not written by this patch at all", mapCam,
                @"pivot\.rotation\s*=");

            // G6. The ease itself is verified numerically; these pin the two decisions in it that
            // a numeric check cannot see.
            Present("G6", "the up axis is eased, not taken raw", mapCam, @"MapCamera\.SmoothMapNorth\s*\(");
            Present("G6", "the ease is frame-rate independent, not k * dt", mapNorth,
                @"1f\s*-\s*Mathf\.Exp\(-MapNorthSharpness\s*\*\s*Time\.unscaledDeltaTime\)");
            Present("G6", "directions are slerped, not lerped", mapNorth, @"Vector3\.Slerp\(_mapNorth");
            Present("G6", "a scene change adopts the new axis outright", tiltEmSrc,
                @"MapCamera\.ResetMapNorth\(\);[\s\S]{0,400}?data\.to\s*<\s*GameScenes\.SPACECENTER");

            var reader = Read(Path.Combine("TiltEm", "Config", "TiltConfig.cs"));
            Present("G5", "the orbital plane prefers the pole form over the legacy pair", reader,
                @"TryReadPole\(body, ""orbitalPlaneRA"", ""orbitalPlaneDec"",[\s\S]{0,40}?""orbitalPlaneX"", ""orbitalPlaneZ""");

            //G8. The stock system needs an explicit plane, because the fallback - the star's own
            //tilt - is 7.57 degrees away from the plane its planets actually orbit in. Nothing is
            //built into the mod any more, so the shipped config is the only place it can come
            //from, and the same goes for every tilt.
            Absent("G8", "no tilts are built into the mod", tiltEmSrc,
                @"DefaultLegacyTilts|BuildDefaultTilts|BuildDefaultOrbitalPlanes");

            var cfg = Read(Path.Combine("Resources", "TiltEm.cfg"));
            Present("G8", "the shipped config sets the stock system plane", cfg,
                @"orbitalPlaneRA\s*=\s*0[\s\S]{0,80}?orbitalPlaneDec\s*=\s*90");
            Present("G8", "and sets it on the star, not a planet", cfg,
                @"@Body\[Sun\][\s\S]{0,600}?orbitalPlaneDec");
            Present("G8", "and carries the stock tilts, which nothing else does now", cfg,
                @"@Body\[Kerbin\][\s\S]{0,200}?tiltx\s*=");

            //G7. The projection cache KSPCommunityFixes keeps for Vectrosity is filled lazily by
            //whichever LateUpdate draws a line first, and every Vectrosity consumer in KSP draws
            //from its own. Dropping it once each camera has settled is what makes the lazy fill
            //land after the pose is final. Soft dependency: resolved by name, no-op when absent.
            var cache = StripComments(Read(Path.Combine("TiltEm", "Harmony", "Camera", "VectorLineProjectionCache.cs")));
            Present("G7", "the projection cache is dropped after the map camera moves", cache,
                @"HarmonyPatch\(typeof\(PlanetariumCamera\)\)[\s\S]{0,400}?VectorLineProjectionCache\.Invalidate");
            Present("G7", "and after the scaled camera moves", cache,
                @"HarmonyPatch\(typeof\(ScaledCamera\)\)[\s\S]{0,400}?VectorLineProjectionCache\.Invalidate");
            Present("G7", "KSPCF is resolved by name, not referenced", cache,
                @"AccessTools\.TypeByName\(CacheType\)");
            Present("G7", "and a missing cache is a no-op", cache,
                @"if \(_lastCachedFrame == null\) return;");

            var flightCamera = StripComments(Read(Path.Combine("TiltEm", "Harmony", "Camera", "FlightGlobals_GetFoR.cs")));
            Present("G3", "the orbital camera asks for the world pole", flightCamera, @"BodyAxes\.WorldNorth\s*\(");
            Absent("G3", "the orbital camera does not use the celestial pole", flightCamera, @"BodyAxes\.CelestialNorth\s*\(");
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
            // Narrowed to a vessel receiver: LineRenderer has a SetPosition of its own, and
            // the defect is specifically moving a vessel by its root transform.
            Absent("C2", "no vessel SetPosition call (CoM/root mismatch)", mod,
                @"[Vv]essel\w*\s*\.\s*SetPosition\s*\(");
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
            var cbUpdate = Read(Path.Combine("TiltEm", "Harmony", "Frames", "CelestialBody_CBUpdate.cs"));

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
            var frames = Read(Path.Combine("TiltEm", "Frames", "TiltEmFrames.cs"));
            var cbUpdate = Read(Path.Combine("TiltEm", "Harmony", "Frames", "CelestialBody_CBUpdate.cs"));
            var zupAtT = Read(Path.Combine("TiltEm", "Harmony", "Frames", "Planetarium_ZupAtT.cs"));

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
            var path = Path.Combine(_root, "TiltEm");
            if (!Directory.Exists(path)) return "";

            var text = "";
            foreach (var file in Directory.GetFiles(path, "*.cs", SearchOption.AllDirectories))
            {
                // Skip build output.
                if (file.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar)) continue;
                if (file.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar)) continue;

                text += StripComments(File.ReadAllText(file)) + "\n";
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
