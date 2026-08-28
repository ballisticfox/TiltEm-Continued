using HarmonyLib;
using TMPro;
using LabelRef = HarmonyLib.AccessTools.FieldRef<ManeuverNodeEditorTabOrbitAdv, TMPro.TextMeshProUGUI>;
using OrbitElements = TiltEm.TiltEmFrames.OrbitElements;

// ReSharper disable All

namespace TiltEm.Harmony
{
    /// <summary>
    /// Shows the maneuver node editor's orientation elements against the parent's equator instead
    /// of the celestial one. See section 8.2 of Docs/TILT_MATHEMATICS.pdf.
    /// </summary>
    //Stock prints inc, LAN and argPe straight from the orbit's celestial elements, so an
    //equatorial orbit around a tilted planet shows the obliquity as inclination. A postfix
    //rather than a transpiler: only three of five fields change frame, so rewriting three
    //strings is smaller and survives the tab's internals moving.
    [HarmonyPatch(typeof(ManeuverNodeEditorTabOrbitAdv))]
    [HarmonyPatch("UpdateUIElements")]
    internal class ManeuverNodeEditorTabOrbitAdv_UpdateUIElements
    {
        /// <summary>The orbit the tab decided to show.</summary>
        //Read rather than re-derived, so the readout cannot describe a different orbit than
        //the one stock just printed.
        private static readonly AccessTools.FieldRef<ManeuverNodeEditorTabOrbitAdv, Orbit> OrbitToDisplay =
            AccessTools.FieldRefAccess<ManeuverNodeEditorTabOrbitAdv, Orbit>("orbitToDisplay");

        private static readonly LabelRef Inclination = Label("orbitInclination");
        private static readonly LabelRef Lan = Label("orbitLongitudeOfAscendingNode");
        private static readonly LabelRef ArgumentOfPeriapsis = Label("orbitArgumentOfPeriapsis");

        private static LabelRef Label(string fieldName)
        {
            return AccessTools.FieldRefAccess<ManeuverNodeEditorTabOrbitAdv, TextMeshProUGUI>(fieldName);
        }

        [HarmonyPostfix]
        private static void PostfixUpdateUIElements(ManeuverNodeEditorTabOrbitAdv __instance)
        {
            using (TiltEmProfiler.ManeuverNodeElements.Sample())
            {
                Orbit orbit = OrbitToDisplay(__instance);

                if (!ParentRelativeOrbit.TryGetLocalElements(orbit, out OrbitElements local)) return;

                SetText(Inclination(__instance), local.Inclination);
                SetText(Lan(__instance), local.LongitudeOfAscendingNode);
                SetText(ArgumentOfPeriapsis(__instance), local.ArgumentOfPeriapsis);
            }
        }

        /// <summary>Writes a degree-formatted value to a label, null-tolerant.</summary>
        //The labels are serialised prefab references; a partly set-up prefab or a renamed
        //field would turn a cosmetic miss into an exception every frame the tab is open.
        private static void SetText(TextMeshProUGUI label, double degrees)
        {
            if (label == null) return;

            //Escaped, not literal: U+00B0 has lookalikes. One decimal and a leading space match
            //stock's own format, so a corrected field looks no different from an untouched one.
            label.text = degrees.ToString("F1") + " \u00B0";
        }
    }
}
