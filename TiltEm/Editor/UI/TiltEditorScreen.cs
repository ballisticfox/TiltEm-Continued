using TMPro;
using UnityEngine;

namespace TiltEm
{
    /// <summary>
    /// The tilt editor's tab: what the selected body's pole is doing, and the controls that are
    /// not a ring in the sky.
    /// </summary>
    //Numbers come from the open edit while there is one, and off the running game before that.
    //The edit is the authority for a reason: it holds a right ascension the pole itself cannot,
    //once the declination reaches the top.
    internal class TiltEditorScreen : MonoBehaviour
    {
        [SerializeField]
        private EditTiltBodyToggle _toggle;
        [SerializeField]
        private TiltFrameToggle _frame;
        [SerializeField]
        private TiltModeButton _mode;
        [SerializeField]
        private ExportButton _exportButton;
        [SerializeField]
        private TextMeshProUGUI _status;
        [SerializeField]
        private TextMeshProUGUI _body;
        [SerializeField]
        private TextMeshProUGUI _poleRa;
        [SerializeField]
        private TextMeshProUGUI _poleDec;
        [SerializeField]
        private TextMeshProUGUI _obliquity;
        [SerializeField]
        private TextMeshProUGUI _tiltX;
        [SerializeField]
        private TextMeshProUGUI _tiltZ;
        [SerializeField]
        private TextMeshProUGUI _rotation;
        [SerializeField]
        private TextMeshProUGUI _export;

        /// <summary>Builds the tab once, while the prefab is still inactive.</summary>
        internal void BuildUi()
        {
            Transform content = transform;

            DebugUi.CreateHeader(content, "Tilt editor");
            _status = DebugUi.CreateNote(content, "-");
            _toggle = DebugUi.CreateToggle<EditTiltBodyToggle>(content, "Edit selected body");
            _frame = DebugUi.CreateToggle<TiltFrameToggle>(content, "Pole relative to parent");

            GameObject buttons = DebugUi.CreateRowLayout(content);
            _mode = DebugUi.CreateButton<TiltModeButton>(buttons.transform, "Mode");
            DebugUi.CreateButton<ResetTiltButton>(buttons.transform, "Reset tilt");

            DebugUi.CreateSpacer(content);

            //One block rather than a heading per pair. Nothing can flex any more, so every
            //heading costs a row the tab may not have, and each row is named for the config key
            //it writes anyway.
            _body = DebugUi.CreateRow(content, "Body", "-");
            _poleRa = DebugUi.CreateRow(content, "poleRA", "-");
            _poleDec = DebugUi.CreateRow(content, "poleDec", "-");
            _obliquity = DebugUi.CreateRow(content, "Obliquity", "-");
            _tiltX = DebugUi.CreateRow(content, "tiltx", "-");
            _tiltZ = DebugUi.CreateRow(content, "tiltz", "-");
            _rotation = DebugUi.CreateRow(content, "initialRotation", "-");

            DebugUi.CreateSpacer(content);

            GameObject exportRow = DebugUi.CreateRowLayout(content);
            _exportButton = DebugUi.CreateButton<ExportButton>(exportRow.transform, "Export config");
            _export = DebugUi.CreateNote(content, string.Empty);
        }

        // ReSharper disable once UnusedMember.Local
        private void Update()
        {
            using (TiltEmProfiler.EditorTiltTab.Sample())
            {
                //Every frame the tab is up, which is the only time it can be. The editor's camera
                //pin and its notion of what can be opened both follow whichever tab that is.
                BodyEditor.SetTarget(BodyEditTarget.Tilt);

                CelestialBody selected = BodyEditor.SelectedBody();
                BodyEdit edit = BodyEditor.Active;

                _toggle.Refresh(selected);
                _frame.Refresh(edit);
                _mode.Refresh(edit);

                _status.text = Status(edit);
                _export.text = _exportButton.Result;

                if (edit != null) Show(edit.Body, edit.Tilt);
                else ShowUnedited(selected);
            }
        }

        /// <summary>The open edit's own numbers.</summary>
        private void Show(CelestialBody body, TiltEdit tilt)
        {
            _body.text = body.bodyName + (tilt.RelativeToParent
                             ? " (from " + body.referenceBody.bodyName + "'s equator)"
                             : string.Empty);

            _poleRa.text = DebugFormat.Angle(tilt.PoleRa);
            _poleDec.text = DebugFormat.Angle(tilt.PoleDec);
            _obliquity.text = DebugFormat.Angle(tilt.Obliquity);
            _tiltX.text = DebugFormat.Angle(tilt.TiltX);
            _tiltZ.text = DebugFormat.Angle(tilt.TiltZ);
            _rotation.text = DebugFormat.Angle(tilt.InitialRotation);
        }

        /// <summary>What the game holds for a body nobody has opened yet.</summary>
        private void ShowUnedited(CelestialBody body)
        {
            if (body == null)
            {
                _body.text = "none";
                _poleRa.text = _poleDec.text = _obliquity.text = "-";
                _tiltX.text = _tiltZ.text = _rotation.text = "-";
                return;
            }

            if (!TiltEm.TryGetTilt(body.bodyName, out BodyTilt tilt)) tilt = TiltEmFrames.Untilted;

            Vector3d legacy = TiltEmFrames.ToLegacyEuler(tilt);

            _body.text = body.bodyName;
            _poleRa.text = DebugFormat.Angle(tilt.PoleRa);
            _poleDec.text = DebugFormat.Angle(tilt.PoleDec);
            _obliquity.text = DebugFormat.Angle(tilt.Obliquity);
            _tiltX.text = DebugFormat.Angle(legacy.x);
            _tiltZ.text = DebugFormat.Angle(legacy.z);

            //The prime meridian is added in, not shown beside. It and initialRotation are two
            //constant offsets on the same angle, and opening a body folds the first into the
            //second; showing the sum keeps the number from stepping the moment you tick the box.
            _rotation.text = DebugFormat.Angle(Wrap(body.initialRotation + tilt.PrimeMeridian));
        }

        private static string Status(BodyEdit edit)
        {
            if (PrincipiaCheck.Installed) return "Principia is installed, so Tilt'Em is standing down.";
            if (HighLogic.LoadedScene != GameScenes.TRACKSTATION) return "Editing runs in the tracking station.";

            if (edit == null) return "Select a body and tick the box to edit it.";

            return "Editing " + edit.Body.bodyName + ". Drag a ring; hold the modifier key for fine control.";
        }

        private static double Wrap(double degrees)
        {
            double wrapped = degrees % 360.0;
            return wrapped < 0.0 ? wrapped + 360.0 : wrapped;
        }
    }
}
