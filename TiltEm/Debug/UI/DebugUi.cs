using KSP.UI.Screens.DebugToolbar;
using KSP.UI.Screens.DebugToolbar.Screens;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TiltEm
{
    /// <summary>
    /// Clones widgets out of KSP's own debug screens so Tilt'Em's tabs match them exactly.
    /// </summary>
    //Cloning rather than styling: the debug menu's look is not exposed as a skin, and a
    //hand-built approximation drifts every time the theme is touched.
    internal static class DebugUi
    {
        private static GameObject _labelPrefab;
        private static GameObject _buttonPrefab;
        private static GameObject _togglePrefab;
        private static GameObject _spacerPrefab;

        private static bool _initialized;

        /// <summary>The live screen list the debug menu builds its sidebar from.</summary>
        public static AddDebugScreens Screens { get; private set; }

        /// <summary>
        /// Finds the templates. Valid only once DebugScreenSpawner exists, so call it no earlier
        /// than the main menu.
        /// </summary>
        public static bool Initialize()
        {
            if (_initialized) return true;

            DebugScreenSpawner spawner = DebugScreenSpawner.Instance;

            //The live screen, not the prefab: the spawner parents the instantiated one under
            //itself, and its list is the one that still has to be read to build the sidebar.
            Screens = spawner == null ? null : spawner.GetComponentInChildren<AddDebugScreens>(true);

            if (Screens == null || Screens.screens == null)
            {
                Debug.LogWarning("[TiltEm]: the stock debug screens are unavailable, so Tilt'Em's "
                                 + "debug tabs were not registered.");
                return false;
            }

            //Each widget comes from the stock screen that uses it most plainly: the console's
            //bottom bar for a button, the database overview for a label, the debugging screen
            //for a toggle.
            foreach (AddDebugScreens.ScreenWrapper wrapper in Screens.screens)
            {
                if (wrapper.screen == null) continue;

                GameObject root = wrapper.screen.gameObject;

                if (wrapper.name == "Debug") TakeButton(root);
                else if (wrapper.name == "Database") TakeLabel(root);
                else if (wrapper.name == "Debugging") TakeToggle(root);
            }

            if (_spacerPrefab == null) BuildSpacer();

            _initialized = _labelPrefab != null && _buttonPrefab != null && _togglePrefab != null;

            if (!_initialized)
            {
                Debug.LogWarning("[TiltEm]: could not clone every stock debug widget (label="
                                 + (_labelPrefab != null) + " button=" + (_buttonPrefab != null)
                                 + " toggle=" + (_togglePrefab != null)
                                 + "), so Tilt'Em's debug tabs were not registered.");
            }

            return _initialized;
        }

        private static void TakeButton(GameObject root)
        {
            if (_buttonPrefab != null) return;

            Transform button = root.transform.Find("BottomBar/Button");
            if (button != null) _buttonPrefab = Clone(button.gameObject, "TiltEm_Button");
        }

        private static void TakeLabel(GameObject root)
        {
            if (_labelPrefab != null) return;

            Transform label = root.transform.Find("TotalLabel");
            if (label != null) _labelPrefab = Clone(label.gameObject, "TiltEm_Label");
        }

        private static void TakeToggle(GameObject root)
        {
            if (_togglePrefab != null) return;

            Transform toggle = root.transform.Find("PrintErrorsToScreen");
            if (toggle == null) return;

            _togglePrefab = Clone(toggle.gameObject, "TiltEm_Toggle");

            //The stock behaviour rides along with the clone and would drive KSP's own setting.
            DebugScreenToggle stock = _togglePrefab.GetComponent<DebugScreenToggle>();
            if (stock != null) Object.DestroyImmediate(stock);
        }

        private static void BuildSpacer()
        {
            _spacerPrefab = new GameObject("TiltEm_Spacer", typeof(RectTransform));
            _spacerPrefab.SetActive(false);

            LayoutElement layout = _spacerPrefab.AddComponent<LayoutElement>();
            layout.minHeight = 8f;
            layout.preferredHeight = 8f;

            Object.DontDestroyOnLoad(_spacerPrefab);
        }

        private static GameObject Clone(GameObject source, string name)
        {
            GameObject clone = Object.Instantiate(source);
            clone.name = name;
            clone.SetActive(false);
            Object.DontDestroyOnLoad(clone);

            return clone;
        }

        /// <summary>An empty screen laid out top-down, ready for a content script to fill.</summary>
        public static RectTransform CreateScreen<T>(string name) where T : MonoBehaviour
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.SetActive(false);
            Object.DontDestroyOnLoad(go);
            go.AddComponent<T>();

            RectTransform rect = go.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            VerticalLayoutGroup layout = go.AddComponent<VerticalLayoutGroup>();
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.spacing = 4f;
            layout.padding = new RectOffset(8, 8, 8, 8);

            return rect;
        }

        public static TextMeshProUGUI CreateLabel(Transform parent, string text)
        {
            GameObject go = Object.Instantiate(_labelPrefab, parent, false);
            go.SetActive(true);
            go.name = "Label";

            //The clone carries the database screen's fixed width; let it fill the row instead.
            LayoutElement layout = go.GetComponent<LayoutElement>();
            if (layout != null)
            {
                layout.preferredWidth = -1;
                layout.flexibleWidth = 1;
            }

            TextMeshProUGUI tmp = go.GetComponentInChildren<TextMeshProUGUI>();
            RectTransform textRect = tmp.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;

            tmp.text = text;

            return tmp;
        }

        public static TextMeshProUGUI CreateHeader(Transform parent, string text)
        {
            TextMeshProUGUI tmp = CreateLabel(parent, text);
            tmp.fontStyle = FontStyles.Bold;
            tmp.fontSize *= 1.2f;

            return tmp;
        }

        /// <summary>A name on the left and its value on the right, returned so it can be updated.</summary>
        public static TextMeshProUGUI CreateRow(Transform parent, string name, string initialValue)
        {
            GameObject row = new GameObject("Row", typeof(RectTransform));
            row.transform.SetParent(parent, false);

            HorizontalLayoutGroup layout = row.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 8f;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            row.AddComponent<LayoutElement>().minHeight = 20f;

            CreateLabel(row.transform, name);

            TextMeshProUGUI value = CreateLabel(row.transform, initialValue);
            value.alignment = TextAlignmentOptions.MidlineRight;

            return value;
        }

        /// <summary>A full-width block that keeps its own line breaks, for per-body tables.</summary>
        public static TextMeshProUGUI CreateBlock(Transform parent)
        {
            TextMeshProUGUI tmp = CreateLabel(parent, string.Empty);
            tmp.enableWordWrapping = false;
            tmp.alignment = TextAlignmentOptions.TopLeft;

            return tmp;
        }

        public static T CreateButton<T>(Transform parent, string text) where T : DebugButton
        {
            GameObject go = Object.Instantiate(_buttonPrefab, parent, false);
            go.name = "Button";

            LayoutElement layout = go.GetComponent<LayoutElement>();
            if (layout != null)
            {
                layout.preferredWidth = -1;
                layout.flexibleWidth = 1;
                layout.minHeight = 30f;
            }

            TextMeshProUGUI tmp = go.GetComponentInChildren<TextMeshProUGUI>();
            if (tmp != null) tmp.text = text;

            T component = go.AddComponent<T>();
            component.button = go.GetComponent<Button>();

            //Activating runs Awake, which is where the component wires itself up.
            go.SetActive(true);

            return component;
        }

        public static T CreateToggle<T>(Transform parent, string label) where T : DebugScreenToggle
        {
            GameObject go = Object.Instantiate(_togglePrefab, parent, false);
            go.name = "Toggle";

            LayoutElement layout = go.GetComponent<LayoutElement>();
            if (layout != null)
            {
                layout.preferredWidth = -1;
                layout.flexibleWidth = 1;
            }

            //The stock toggle is a fixed width inside its wrapper; stretch it to the full row.
            Toggle inner = go.GetComponentInChildren<Toggle>(true);
            if (inner != null)
            {
                RectTransform rect = inner.GetComponent<RectTransform>();
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.offsetMin = Vector2.zero;
                rect.offsetMax = Vector2.zero;
            }

            T component = go.AddComponent<T>();
            component.toggle = inner;

            Transform labelTransform = inner == null ? null : inner.transform.Find("Label");
            if (labelTransform != null) component.toggleText = labelTransform.GetComponent<TextMeshProUGUI>();
            component.text = label;

            go.SetActive(true);

            return component;
        }

        public static void CreateSpacer(Transform parent)
        {
            GameObject go = Object.Instantiate(_spacerPrefab, parent, false);
            go.SetActive(true);
            go.name = "Spacer";
        }

        /// <summary>A left-to-right strip, for putting buttons side by side.</summary>
        public static GameObject CreateRowLayout(Transform parent)
        {
            GameObject go = new GameObject("HorizontalLayout", typeof(RectTransform));
            go.transform.SetParent(parent, false);

            HorizontalLayoutGroup layout = go.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 8f;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            go.AddComponent<LayoutElement>().minHeight = 30f;

            return go;
        }
    }
}
