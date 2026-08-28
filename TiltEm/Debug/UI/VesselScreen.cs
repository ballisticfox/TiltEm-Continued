using TMPro;
using UnityEngine;

namespace TiltEm
{
    /// <summary>
    /// The active vessel's orbit and orientation, plus the frame velocity it is riding.
    /// </summary>
    internal class VesselScreen : MonoBehaviour
    {
        [SerializeField]
        private TextMeshProUGUI _orbitMode;
        [SerializeField]
        private TextMeshProUGUI _orbitTransformRotation;
        [SerializeField]
        private TextMeshProUGUI _orbitFrame;
        [SerializeField]
        private TextMeshProUGUI _rotation;
        [SerializeField]
        private TextMeshProUGUI _surfaceRelativeRotation;
        [SerializeField]
        private TextMeshProUGUI _position;
        [SerializeField]
        private TextMeshProUGUI _frameVelocity;

        /// <summary>Builds the tab once, while the prefab is still inactive.</summary>
        internal void BuildUi()
        {
            Transform content = transform;

            DebugUi.CreateHeader(content, "Active vessel");
            _orbitMode = DebugUi.CreateRow(content, "Orbit update mode", "-");
            _orbitTransformRotation = DebugUi.CreateRow(content, "Orbit transform rotation", "-");
            _orbitFrame = DebugUi.CreateRow(content, "Orbit frame", "-");
            _rotation = DebugUi.CreateRow(content, "Rotation", "-");
            _surfaceRelativeRotation = DebugUi.CreateRow(content, "Surface-relative rotation", "-");
            _position = DebugUi.CreateRow(content, "Position", "-");

            DebugUi.CreateSpacer(content);

            //Krakensbane's, not the vessel's, so it is worth keeping visually apart: it reads
            //non-zero with no vessel loaded at all.
            DebugUi.CreateHeader(content, "Krakensbane");
            _frameVelocity = DebugUi.CreateRow(content, "Frame velocity", "-");
        }

        // ReSharper disable once UnusedMember.Local
        private void Update()
        {
            using (TiltEmProfiler.DebugUiVessel.Sample())
            {
                Vessel vessel = FlightGlobals.ActiveVessel;

                if (vessel == null)
                {
                    ShowNoVessel();
                }
                else
                {
                    _orbitMode.text = vessel.orbitDriver.updateMode.ToString();
                    _orbitTransformRotation.text = DebugFormat.Euler(vessel.orbitDriver.driverTransform.rotation);
                    _orbitFrame.text = DebugFormat.Euler(vessel.orbit.OrbitFrame.Rotation);
                    _rotation.text = DebugFormat.Euler(vessel.vesselTransform.rotation);
                    _surfaceRelativeRotation.text = DebugFormat.Euler(vessel.srfRelRotation);
                    _position.text = DebugFormat.Vector(vessel.vesselTransform.position);
                }

                _frameVelocity.text = DebugFormat.Vector(Krakensbane.GetFrameVelocity());
            }
        }

        private void ShowNoVessel()
        {
            _orbitMode.text = "no active vessel";
            _orbitTransformRotation.text = "-";
            _orbitFrame.text = "-";
            _rotation.text = "-";
            _surfaceRelativeRotation.text = "-";
            _position.text = "-";
        }
    }
}
