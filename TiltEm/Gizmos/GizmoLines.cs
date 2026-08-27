using UnityEngine;

namespace TiltEm
{
    /// <summary>
    /// Shared line drawing for the things Tilt'Em hangs off a body in scaled space: the tilt
    /// axes, and the editor's drag rings.
    /// </summary>
    //One place, because the awkward parts are the same for both. The shader has to be looked up
    //by a name KSP might not carry, and the width has to be recomputed every frame or the lines
    //vanish as you zoom out.
    internal static class GizmoLines
    {
        /// <summary>Line thickness as a fraction of camera distance, so it stays legible.</summary>
        private const float WidthPerDistance = 0.004f;

        /// <summary>Ceiling on that thickness, as a fraction of the shape's own size.</summary>
        //Without it the width grows without bound as you zoom out, and past roughly a tenth of
        //the shape the lines stop reading as lines and become camera-facing slabs across the
        //view. Going sub-pixel far away is the right trade.
        private const float MaxWidthFraction = 0.01f;

        private static Shader _shader;
        private static bool _looked;

        /// <summary>An unlit shader KSP is known to carry, with fallbacks.</summary>
        public static Shader Shader()
        {
            if (_looked) return _shader;

            _looked = true;
            _shader = UnityEngine.Shader.Find("Legacy Shaders/Particles/Alpha Blended")
                      ?? UnityEngine.Shader.Find("Particles/Alpha Blended")
                      ?? UnityEngine.Shader.Find("Sprites/Default");

            if (_shader == null)
            {
                Debug.LogWarning("[TiltEm]: no shader found for the scaled-space lines; they will not draw.");
            }

            return _shader;
        }

        /// <summary>
        /// A line renderer whose points are read in its own local space, so it follows whatever
        /// it is parented to for free.
        /// </summary>
        //Local, not world, is the whole reason these are parented to a body's scaled transform:
        //position, rotation and the floating-origin shift all arrive through the parent, so
        //nothing here has to win a race with the camera.
        public static LineRenderer Create(Transform parent, string name, Material material,
            Color color, int points)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);

            LineRenderer line = go.AddComponent<LineRenderer>();

            line.material = material;
            line.startColor = color;
            line.endColor = color;
            line.positionCount = points;
            line.useWorldSpace = false;
            line.alignment = LineAlignment.View;

            return line;
        }

        /// <summary>Lays a closed circle of the given radius in the line's local XZ plane.</summary>
        //XZ, so the circle's own axis is local +Y and pointing a ring somewhere is a matter of
        //rotating that axis onto it.
        public static void Circle(LineRenderer line, float radius)
        {
            int points = line.positionCount;

            for (int i = 0; i < points; i++)
            {
                float angle = i * (2f * Mathf.PI / (points - 1));

                line.SetPosition(i, new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius));
            }
        }

        /// <summary>
        /// The width to draw at, for a shape whose own size is <paramref name="reference"/>.
        /// Roughly constant on screen as you zoom.
        /// </summary>
        public static float WidthFor(float reference)
        {
            if (PlanetariumCamera.fetch == null) return reference * MaxWidthFraction;

            return Mathf.Min(PlanetariumCamera.fetch.Distance * WidthPerDistance,
                reference * MaxWidthFraction);
        }
    }
}
