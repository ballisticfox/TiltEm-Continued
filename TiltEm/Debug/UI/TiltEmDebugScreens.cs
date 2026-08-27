using KSP.UI.Screens.DebugToolbar;
using UnityEngine;

namespace TiltEm
{
    /// <summary>
    /// Registers Tilt'Em's tabs into the stock debug menu (Alt+F12).
    /// </summary>
    //At the main menu, once: DebugScreenSpawner has to exist before its widgets can be cloned,
    //and what is registered here lasts the rest of the session.
    [KSPAddon(KSPAddon.Startup.MainMenu, true)]
    internal class TiltEmDebugScreens : MonoBehaviour
    {
        private const string Root = "TiltEm";

        // ReSharper disable once UnusedMember.Local
        private void Start()
        {
            if (!DebugUi.Initialize()) return;

            RectTransform frames = DebugUi.CreateScreen<FramesScreen>("TiltEm_Frames");
            frames.GetComponent<FramesScreen>().BuildUi();
            Add(null, Root, "Tilt'Em", frames);

            RectTransform bodies = DebugUi.CreateScreen<BodiesScreen>("TiltEm_Bodies");
            bodies.GetComponent<BodiesScreen>().BuildUi();
            Add(Root, "TiltEm_Bodies", "Bodies", bodies);

            RectTransform vessel = DebugUi.CreateScreen<VesselScreen>("TiltEm_Vessel");
            vessel.GetComponent<VesselScreen>().BuildUi();
            Add(Root, "TiltEm_Vessel", "Vessel", vessel);

            RectTransform tiltEditor = DebugUi.CreateScreen<TiltEditorScreen>("TiltEm_TiltEditor");
            tiltEditor.GetComponent<TiltEditorScreen>().BuildUi();
            Add(Root, "TiltEm_TiltEditor", "Tilt Editor", tiltEditor);

            RectTransform orbitEditor = DebugUi.CreateScreen<OrbitEditorScreen>("TiltEm_OrbitEditor");
            orbitEditor.GetComponent<OrbitEditorScreen>().BuildUi();
            Add(Root, "TiltEm_OrbitEditor", "Orbit Editor", orbitEditor);
        }

        /// <summary>
        /// Appends to the list the debug menu has yet to read, rather than registering the tab
        /// directly.
        /// </summary>
        //DebugScreen.AddContentScreen would work too, but it runs before the stock screens have
        //been added and so puts Tilt'Em above them. Appending here keeps it after the stock
        //entries, at the bottom of the sidebar where a mod belongs.
        private static void Add(string parentName, string name, string text, RectTransform screen)
        {
            DebugUi.Screens.screens.Add(new AddDebugScreens.ScreenWrapper
            {
                parentName = parentName,
                name = name,
                text = text,
                screen = screen,
            });
        }
    }
}
