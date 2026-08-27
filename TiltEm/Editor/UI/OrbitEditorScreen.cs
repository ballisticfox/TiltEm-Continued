using Kopernicus;
using System;
using TMPro;
using UnityEngine;
using OrbitElements = TiltEm.TiltEmFrames.OrbitElements;

namespace TiltEm
{
    /// <summary>
    /// The orbit editor's tab: the selected body's elements, in whichever frame they are being
    /// dragged in.
    /// </summary>
    internal class OrbitEditorScreen : MonoBehaviour
    {
        [SerializeField]
        private EditOrbitBodyToggle _toggle;
        [SerializeField]
        private OrbitFrameToggle _frame;
        [SerializeField]
        private ExportButton _exportButton;
        [SerializeField]
        private TextMeshProUGUI _status;
        [SerializeField]
        private TextMeshProUGUI _body;
        [SerializeField]
        private TextMeshProUGUI _inclination;
        [SerializeField]
        private TextMeshProUGUI _lan;
        [SerializeField]
        private TextMeshProUGUI _argumentOfPeriapsis;
        [SerializeField]
        private TextMeshProUGUI _eccentricity;
        [SerializeField]
        private TextMeshProUGUI _semiMajorAxis;
        [SerializeField]
        private TextMeshProUGUI _meanAnomaly;
        [SerializeField]
        private TextMeshProUGUI _export;

        /// <summary>Builds the tab once, while the prefab is still inactive.</summary>
        internal void BuildUi()
        {
            Transform content = transform;

            DebugUi.CreateHeader(content, "Orbit editor");
            _status = DebugUi.CreateNote(content, "-");
            _toggle = DebugUi.CreateToggle<EditOrbitBodyToggle>(content, "Edit selected body");
            _frame = DebugUi.CreateToggle<OrbitFrameToggle>(content, "Elements relative to parent");

            GameObject buttons = DebugUi.CreateRowLayout(content);
            DebugUi.CreateButton<ResetOrbitButton>(buttons.transform, "Reset orbit");

            DebugUi.CreateSpacer(content);

            _body = DebugUi.CreateRow(content, "Body", "-");
            _inclination = DebugUi.CreateRow(content, "inclination", "-");
            _lan = DebugUi.CreateRow(content, "longitudeOfAscendingNode", "-");
            _argumentOfPeriapsis = DebugUi.CreateRow(content, "argumentOfPeriapsis", "-");
            _eccentricity = DebugUi.CreateRow(content, "eccentricity", "-");
            _semiMajorAxis = DebugUi.CreateRow(content, "semiMajorAxis", "-");
            _meanAnomaly = DebugUi.CreateRow(content, "meanAnomalyAtEpochD", "-");

            DebugUi.CreateSpacer(content);

            GameObject exportRow = DebugUi.CreateRowLayout(content);
            _exportButton = DebugUi.CreateButton<ExportButton>(exportRow.transform, "Export config");
            _export = DebugUi.CreateNote(content, string.Empty);
        }

        // ReSharper disable once UnusedMember.Local
        private void Update()
        {
            BodyEditor.SetTarget(BodyEditTarget.Orbit);

            CelestialBody selected = BodyEditor.SelectedBody();
            BodyEdit edit = BodyEditor.Active;

            _toggle.Refresh(selected);
            _frame.Refresh(edit);

            _status.text = Status(edit, selected);
            _export.text = _exportButton.Result;

            Show(edit != null ? edit.Body : selected, edit);
        }

        private void Show(CelestialBody body, BodyEdit edit)
        {
            Orbit orbit = body == null ? null : body.orbit;

            if (orbit == null || orbit.referenceBody == null || ReferenceEquals(orbit.referenceBody, body))
            {
                _body.text = body == null ? "none" : body.bodyName + ", orbiting nothing";
                _inclination.text = _lan.text = _argumentOfPeriapsis.text = "-";
                _eccentricity.text = _semiMajorAxis.text = _meanAnomaly.text = "-";
                return;
            }

            //The open edit owns its elements, so a drag reads back exactly what it wrote rather
            //than whatever a decomposition makes of the frame it produced.
            bool relative = edit != null && edit.Orbit != null
                ? edit.Orbit.RelativeToParent
                : body.Get("relativeToParent", false);

            OrbitElements elements = edit != null && edit.Orbit != null
                ? edit.Orbit.Orientation
                : Unedited(orbit, relative);

            //The parent rides along on the body row: with nothing able to flex, a row of its own
            //is a row the tab may not have.
            _body.text = body.bodyName + ", orbiting " + orbit.referenceBody.bodyName
                         + (relative ? " (its equator)" : string.Empty);

            _inclination.text = DebugFormat.Angle(elements.Inclination);
            _lan.text = DebugFormat.Angle(elements.LongitudeOfAscendingNode);
            _argumentOfPeriapsis.text = DebugFormat.Angle(elements.ArgumentOfPeriapsis);

            _eccentricity.text = orbit.eccentricity.ToString("F5");
            _semiMajorAxis.text = DebugFormat.Distance(orbit.semiMajorAxis);
            _meanAnomaly.text = DebugFormat.Angle(orbit.meanAnomalyAtEpoch * (180.0 / Math.PI));
        }

        private static OrbitElements Unedited(Orbit orbit, bool relative)
        {
            //False means the parent is untilted, where the two frames agree and the stored
            //elements are already the answer.
            return relative && ParentRelativeOrbit.TryGetLocalElements(orbit, out OrbitElements local)
                ? local
                : ParentRelativeOrbit.Read(orbit);
        }

        private static string Status(BodyEdit edit, CelestialBody selected)
        {
            if (PrincipiaCheck.Installed) return "Principia is installed, so Tilt'Em is standing down.";
            if (HighLogic.LoadedScene != GameScenes.TRACKSTATION) return "Editing runs in the tracking station.";

            //The one body the orbit editor has nothing to offer: a root star orbits nothing.
            if (edit == null && selected != null && !BodyEditor.CanEdit(selected))
            {
                return selected.bodyName + " orbits nothing, so it has no orbit to edit.";
            }

            if (edit == null) return "Select a body and tick the box to edit its orbit.";

            return "Editing " + edit.Body.bodyName + ". The rings are drawn about its parent.";
        }
    }
}
