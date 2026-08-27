using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TiltEm
{
    /// <summary>
    /// Base for a debug-screen button, mirroring stock's DebugScreenToggle for toggles.
    /// </summary>
    //KSP ships a base class for toggles and for inputs, but not for plain buttons.
    internal abstract class DebugButton : MonoBehaviour
    {
        public Button button;

        /// <summary>The button's own caption, so a button can say what it will do next.</summary>
        public TextMeshProUGUI label;

        // ReSharper disable once UnusedMember.Local
        private void Awake()
        {
            button.onClick.AddListener(OnClick);
            SetupValues();
        }

        protected virtual void SetupValues() { }

        protected void SetLabel(string text)
        {
            if (label != null) label.text = text;
        }

        protected abstract void OnClick();
    }
}
