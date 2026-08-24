using HarmonyLib;
using TMPro;
using UnityEngine;

// ReSharper disable All

namespace TiltEm.Harmony
{
    /// <summary>
    /// Shows the maneuver node editor's orientation elements against the parent's equator
    /// instead of the celestial one.
    ///
    /// The "Orbit (advanced)" tab reads inclination, longitude of the ascending node and
    /// argument of periapsis straight off the orbit and prints them. Those are stored in the
    /// celestial frame, which on a tilted body is not the frame the player is flying in: park a
    /// craft in a perfectly equatorial orbit around a planet with 26 degrees of obliquity and
    /// stock reports an inclination of 26 degrees. The number is not wrong, but it answers a
    /// question nobody asked - the useful one is "how far out of my planet's equator am I", and
    /// that is what a player reads the field as. See ParentRelativeOrbit.
    ///
    /// A postfix rather than a transpiler: the tab writes five fields and only three of them
    /// change frame, so rewriting three strings after the fact is both smaller and immune to
    /// the tab's internals moving. The other two are already frame-independent - eccentricity
    /// obviously, and the ejection angle because it is measured from the parent's prograde
    /// direction rather than from any equator.
    ///
    /// Untilted parents are left completely alone, so a stock system's readouts keep stock's
    /// exact strings.
    /// </summary>
    [HarmonyPatch(typeof(ManeuverNodeEditorTabOrbitAdv))]
    [HarmonyPatch("UpdateUIElements")]
    internal class ManeuverNodeEditorTabOrbitAdv_UpdateUIElements
    {
        /// <summary>
        /// The orbit the tab decided to show - the selected node's next patch, or the vessel's
        /// current orbit when no node is selected. Read rather than re-derived so the readout
        /// can never end up describing a different orbit than the one stock just printed.
        /// </summary>
        private static readonly AccessTools.FieldRef<ManeuverNodeEditorTabOrbitAdv, Orbit> OrbitToDisplay =
            AccessTools.FieldRefAccess<ManeuverNodeEditorTabOrbitAdv, Orbit>("orbitToDisplay");

        private static readonly AccessTools.FieldRef<ManeuverNodeEditorTabOrbitAdv, TextMeshProUGUI> Inclination =
            AccessTools.FieldRefAccess<ManeuverNodeEditorTabOrbitAdv, TextMeshProUGUI>("orbitInclination");

        private static readonly AccessTools.FieldRef<ManeuverNodeEditorTabOrbitAdv, TextMeshProUGUI> Lan =
            AccessTools.FieldRefAccess<ManeuverNodeEditorTabOrbitAdv, TextMeshProUGUI>("orbitLongitudeOfAscendingNode");

        private static readonly AccessTools.FieldRef<ManeuverNodeEditorTabOrbitAdv, TextMeshProUGUI> ArgumentOfPeriapsis =
            AccessTools.FieldRefAccess<ManeuverNodeEditorTabOrbitAdv, TextMeshProUGUI>("orbitArgumentOfPeriapsis");

        [HarmonyPostfix]
        private static void PostfixUpdateUIElements(ManeuverNodeEditorTabOrbitAdv __instance)
        {
            var orbit = OrbitToDisplay(__instance);

            if (!ParentRelativeOrbit.TryGetLocalElements(orbit, out var local)) return;

            SetText(Inclination(__instance), local.Inclination);
            SetText(Lan(__instance), local.LongitudeOfAscendingNode);
            SetText(ArgumentOfPeriapsis(__instance), local.ArgumentOfPeriapsis);
        }

        /// <summary>
        /// Null-tolerant because the three labels are serialised references into the tab's
        /// prefab: a partially-set-up prefab, or a KSP update that renames one, would otherwise
        /// turn a cosmetic miss into an exception thrown every frame the tab is open.
        /// </summary>
        private static void SetText(TextMeshProUGUI label, double degrees)
        {
            if (label == null) return;

            label.text = ParentRelativeOrbit.FormatDegrees(degrees);
        }
    }
}
