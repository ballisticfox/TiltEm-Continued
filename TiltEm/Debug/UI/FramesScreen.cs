using HarmonyLib;
using KSP.UI.Screens.DebugToolbar.Screens;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TiltEm
{
    /// <summary>
    /// The rotating frame and the planetarium it is anchored against: what Tilt'Em is doing
    /// right now, and the two controls for making it do it again.
    /// </summary>
    internal class FramesScreen : MonoBehaviour
    {
        [SerializeField]
        private TextMeshProUGUI _dominantBody;
        [SerializeField]
        private TextMeshProUGUI _inverseRotation;
        [SerializeField]
        private TextMeshProUGUI _planetariumRotation;
        [SerializeField]
        private TextMeshProUGUI _zupRotation;
        [SerializeField]
        private TextMeshProUGUI _inverseRotAngle;

        internal static OrbitPhysicsManager PhysicsManager
        {
            get { return Traverse.Create<OrbitPhysicsManager>().Field<OrbitPhysicsManager>("fetch").Value; }
        }

        /// <summary>Builds the tab once, while the prefab is still inactive.</summary>
        internal void BuildUi()
        {
            Transform content = transform;

            DebugUi.CreateHeader(content, "Rotating frame");
            _dominantBody = DebugUi.CreateRow(content, "Dominant body", "-");
            _inverseRotation = DebugUi.CreateRow(content, "Rotated planetarium", "-");

            GameObject buttons = DebugUi.CreateRowLayout(content);
            DebugUi.CreateButton<ToggleRotatingFrameButton>(buttons.transform, "Toggle rotating frame");
            DebugUi.CreateButton<ResetPhysicsButton>(buttons.transform, "Reset");

            DebugUi.CreateSpacer(content);

            DebugUi.CreateHeader(content, "Planetarium");
            _planetariumRotation = DebugUi.CreateRow(content, "Rotation", "-");
            _zupRotation = DebugUi.CreateRow(content, "Zup rotation", "-");
            _inverseRotAngle = DebugUi.CreateRow(content, "Inverse rotation angle", "-");

            DebugUi.CreateSpacer(content);

            DebugUi.CreateHeader(content, "Aids");
            DebugUi.CreateToggle<DrawAxesToggle>(content, "Draw tilt axes in map view");
            DebugUi.CreateToggle<DrawPlaneNormalToggle>(content, "Draw orbital plane normal");

            DebugUi.CreateSpacer(content);

            DebugUi.CreateHeader(content, "Body editor");
            DebugUi.CreateButton<ResetAllEditsButton>(DebugUi.CreateRowLayout(content).transform,
                "Reset all edits");
        }

        // ReSharper disable once UnusedMember.Local
        private void Update()
        {
            using (TiltEmProfiler.DebugUiFrames.Sample())
            {
                //OrbitPhysicsManager is a flight-scene object, so there is no dominant body in the
                //tracking station at all. Falling back to whatever the camera is focused on keeps
                //the row useful there, marked so it cannot be mistaken for the real thing.
                CelestialBody dominant = OrbitPhysicsManager.DominantBody;
                CelestialBody shown = dominant != null ? dominant : MapFocus.Body();

                _dominantBody.text = shown == null
                    ? "none"
                    : dominant != null ? shown.bodyName : shown.bodyName + " (focused)";

                //No dominant body means nothing holds the rotating frame, so this reads false rather
                //than unknown.
                _inverseRotation.text = (shown != null && shown.inverseRotation).ToString();

                _planetariumRotation.text = DebugFormat.Euler(Planetarium.Rotation);
                _zupRotation.text = DebugFormat.Euler(Planetarium.Zup.Rotation);
                _inverseRotAngle.text = Planetarium.InverseRotAngle.ToString("F2") + "°";
            }
        }
    }

    /// <summary>Hands the rotating frame over, the same call the threshold crossing makes.</summary>
    internal class ToggleRotatingFrameButton : DebugButton
    {
        protected override void OnClick()
        {
            CelestialBody dominant = OrbitPhysicsManager.DominantBody;

            if (dominant != null)
            {
                //Logged because the interesting part is the time it happened at, which is what
                //you match against the rest of the log when a handover goes wrong.
                Debug.Log(dominant.inverseRotation
                    ? "[TiltEm]: setting NORMAL rotation t:" + Planetarium.GetUniversalTime()
                    : "[TiltEm]: setting INVERSE rotation t:" + Planetarium.GetUniversalTime());
            }

            FramesScreen.PhysicsManager.ToggleRotatingFrame();
        }
    }

    /// <summary>Clears the physics manager's stuck-debug flag.</summary>
    internal class ResetPhysicsButton : DebugButton
    {
        protected override void OnClick()
        {
            //Stock's spelling, not a typo here.
            FramesScreen.PhysicsManager.degub = false;
        }
    }

    internal class DrawAxesToggle : DebugScreenToggle
    {
        protected override void SetupValues()
        {
            SetToggle(DebugOptions.DrawAxes);
        }

        protected override void OnToggleChanged(bool state)
        {
            DebugOptions.DrawAxes = state;
        }
    }

    internal class DrawPlaneNormalToggle : DebugScreenToggle
    {
        protected override void SetupValues()
        {
            SetToggle(DebugOptions.DrawPlaneNormal);
        }

        protected override void OnToggleChanged(bool state)
        {
            DebugOptions.DrawPlaneNormal = state;
        }
    }
}
