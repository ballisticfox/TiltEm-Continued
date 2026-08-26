using HarmonyLib;
using KSP.Localization;
using KSP.UI;
using KSP.UI.Screens;
using System.Collections.Generic;

// ReSharper disable All

namespace TiltEm.Harmony
{
    /// <summary>
    /// Adds the body's obliquity to the tracking station's knowledge base panel, as one more row
    /// under Physical Characteristics.
    /// </summary>
    [HarmonyPatch(typeof(KbApp_PlanetParameters))]
    [HarmonyPatch("CreatePhysicalCharacteristics")]
    internal class KbAppPlanetParameters_CreatePhysicalCharacteristics
    {
        /// <summary>The colour stock wraps every value in on this panel.</summary>
        private const string ValueColour = "#b8f4d1";

        /// <summary>Falls back to English if a localisation for the tag is missing.</summary>
        private static string Title =>
            Localizer.TryGetStringByTag("#autoLOC_TiltEm_AxialTilt", out string localised)
                ? localised
                : "Obliquity";

        [HarmonyPostfix]
        private static void PostfixCreatePhysicalCharacteristics(KbApp_PlanetParameters __instance,
            List<UIListItem> __result)
        {
            //Built the way stock builds its own rows, so the new one is indistinguishable.
            string value = "<color=" + ValueColour + ">"
                           + TiltEm.GetTiltForDisplay(__instance.currentBody.bodyName) + "°</color>";

            __result.Add(__instance.cascadingList.CreateBody(Title, value));
        }
    }
}
