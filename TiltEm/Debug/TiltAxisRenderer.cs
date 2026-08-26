using System.Collections.Generic;
using UnityEngine;
using Vectrosity;
// ReSharper disable UnusedMember.Global

#if DEBUG
namespace TiltEm
{
    /// <summary>
    /// Draws the focused body's three axes in the tracking station, so you can see a tilt instead
    /// of reading it off a number. Debugging aid only.
    /// </summary>
    [KSPAddon(KSPAddon.Startup.TrackingStation, false)]
    public class TiltAxisRenderer : MonoBehaviour
    {
        /// <summary>One axis: its fixed appearance, plus the line it reuses each frame.</summary>
        private class Axis
        {
            public readonly string Name;
            public readonly Vector3 Direction;
            public readonly Color Color;
            public readonly List<Vector3> Points = new List<Vector3> { Vector3.zero, Vector3.zero };
            public VectorLine Line;

            public Axis(string name, Vector3 direction, Color color)
            {
                Name = name;
                Direction = direction;
                Color = color;
            }
        }

        /// <summary>Half-length of each axis, as a fraction of the body's radius.</summary>
        private const double ArmLength = 0.0025;

        /// <summary>The scaled-space layer the tracking station renders bodies on.</summary>
        private const int ScaledSpaceLayer = 31;

        //Blue marks the pole, the axis worth looking at. These are not Unity's gizmo colours.
        private readonly Axis[] _axes =
        {
            new Axis("AxialTiltLeftRight", Vector3.right, Color.red),
            new Axis("AxialTiltUpDown", Vector3.up, Color.blue),
            new Axis("AxialTiltFwdBack", Vector3.forward, Color.green),
        };

        public void Update()
        {
            CelestialBody body = TargetBody();

            if (body == null || body.scaledBody == null)
            {
                Hide();
                return;
            }

            Transform scaled = body.scaledBody.transform;
            float arm = (float)(body.Radius * ArmLength);

            foreach (Axis axis in _axes)
            {
                Draw(axis, scaled, arm);
            }
        }

        public void OnDisable()
        {
            foreach (Axis axis in _axes)
            {
                if (axis.Line != null) VectorLine.Destroy(ref axis.Line);
            }
        }

        private void Hide()
        {
            foreach (Axis axis in _axes)
            {
                if (axis.Line != null) axis.Line.active = false;
            }
        }

        private static void Draw(Axis axis, Transform scaled, float arm)
        {
            //The body's own axis, rotated into world space. The tilt lives in that rotation.
            Vector3 offset = scaled.rotation * axis.Direction * arm;

            axis.Points[0] = scaled.position + offset;
            axis.Points[1] = scaled.position - offset;

            if (axis.Line == null) axis.Line = CreateLine(axis);

            axis.Line.active = true;
            axis.Line.Draw3D();
        }

        //Vectrosity holds the point list by reference, so later frames only move the endpoints.
        private static VectorLine CreateLine(Axis axis)
        {
            var line = new VectorLine(axis.Name, axis.Points, 2f, LineType.Continuous);

            line.rectTransform.gameObject.layer = ScaledSpaceLayer;
            line.color = axis.Color;
            line.smoothColor = true;
            line.UpdateImmediate = true;

            return line;
        }

        private static CelestialBody TargetBody()
        {
            if (HighLogic.LoadedScene != GameScenes.TRACKSTATION) return null;
            if (PlanetariumCamera.fetch == null || PlanetariumCamera.fetch.target == null) return null;

            return PlanetariumCamera.fetch.target.celestialBody;
        }
    }
}
#endif
