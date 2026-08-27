using UnityEngine;

namespace TiltEm
{
    /// <summary>
    /// One draggable ring: a circle lying square to the axis its handle turns the body about,
    /// and the arithmetic that turns a mouse position into an angle on it.
    /// </summary>
    //A ring rather than a knob, because the whole circle is the grab target. A knob has to be
    //found before it can be dragged, and it disappears whenever it goes round the back.
    internal class DragRing
    {
        /// <summary>How close the pointer has to come, in screen pixels.</summary>
        private const float GrabPixels = 14f;

        /// <summary>Points around the circle. One is repeated to close it.</summary>
        private const int Points = 97;

        private readonly LineRenderer _line;
        private readonly Color _color;

        private Vector3 _centre;
        private Vector3 _normal;
        private float _radius;

        /// <summary>Fixed while a drag runs, so the angle it measures has a stable zero.</summary>
        private Vector3 _reference;

        public DragRing(Transform parent, EditHandle handle, Material material, Color color, float radiusFactor)
        {
            Handle = handle;
            RadiusFactor = radiusFactor;

            _color = color;
            _line = GizmoLines.Create(parent, "TiltEmRing_" + handle, material, color, Points);
        }

        public EditHandle Handle { get; }

        /// <summary>Where this ring sits, as a multiple of whatever the gizmo is sized against.</summary>
        public float RadiusFactor { get; }

        public GameObject GameObject => _line.gameObject;

        /// <summary>Places the ring and points it along its axis.</summary>
        public void Place(Vector3 centre, Vector3 normal, float radius)
        {
            _centre = centre;
            _normal = normal;
            _radius = radius;

            //The ring is drawn in its own local XZ plane, so aiming it is a matter of turning
            //its local up onto the axis. Set in world space: the parent carries the body's spin,
            //and all of these axes but the body's own are fixed in the sky.
            _line.transform.rotation = Quaternion.FromToRotation(Vector3.up, normal);

            GizmoLines.Circle(_line, radius);
            _line.widthMultiplier = GizmoLines.WidthFor(radius);
        }

        public void Show(bool visible)
        {
            if (_line.gameObject.activeSelf != visible) _line.gameObject.SetActive(visible);
        }

        public void SetLayer(int layer)
        {
            _line.gameObject.layer = layer;
        }

        /// <summary>Brightens the ring the pointer is on, so it is clear what a click would grab.</summary>
        public void SetHighlighted(bool highlighted)
        {
            Color color = highlighted ? Color.Lerp(_color, Color.white, 0.6f) : _color;

            _line.startColor = color;
            _line.endColor = color;
            _line.widthMultiplier = GizmoLines.WidthFor(_radius) * (highlighted ? 2f : 1f);
        }

        /// <summary>
        /// How far the pointer is from the ring, in screen pixels, or a miss. Measured against
        /// the nearest point of the circle itself rather than its plane, so the inside of a ring
        /// is not a grab target.
        /// </summary>
        public bool TryGrabDistance(Camera camera, Vector3 pointer, out float pixels)
        {
            pixels = 0f;

            if (!TryPlanePoint(camera, pointer, out Vector3 point)) return false;

            Vector3 offset = point - _centre;
            if (offset.sqrMagnitude <= 0f) return false;

            Vector3 screen = camera.WorldToScreenPoint(_centre + offset.normalized * _radius);

            //Behind the camera, where WorldToScreenPoint mirrors the point back into view.
            if (screen.z <= 0f) return false;

            pixels = Vector2.Distance(new Vector2(screen.x, screen.y), new Vector2(pointer.x, pointer.y));

            return pixels <= GrabPixels;
        }

        /// <summary>Fixes the angle this ring measures from. Call once, as a drag starts.</summary>
        public void BeginDrag(Camera camera, Vector3 pointer)
        {
            //Any direction in the plane will do, as long as it holds still for the whole drag.
            //Projecting a fixed world axis gives a repeatable one; the ring's own axis is the
            //only case it can fail on.
            _reference = Vector3.ProjectOnPlane(Vector3.up, _normal);

            if (_reference.sqrMagnitude < 1e-6f) _reference = Vector3.ProjectOnPlane(Vector3.right, _normal);

            _reference = _reference.normalized;
        }

        /// <summary>
        /// Where the pointer sits on the ring, as an angle about the handle's own axis. Degrees,
        /// in the sense the handle's number counts in.
        /// </summary>
        public bool TryAngle(Camera camera, Vector3 pointer, out float degrees)
        {
            degrees = 0f;

            if (!TryPlanePoint(camera, pointer, out Vector3 point)) return false;

            Vector3 offset = point - _centre;
            if (offset.sqrMagnitude <= 0f) return false;

            //Negated. The swizzle that carries the celestial frame into Unity's swaps two axes,
            //which flips handedness, so a turn that reads positive here counts down in the
            //numbers the handle drives.
            degrees = -Vector3.SignedAngle(_reference, offset, _normal);

            return true;
        }

        /// <summary>Where the pointer's ray crosses the ring's plane.</summary>
        private bool TryPlanePoint(Camera camera, Vector3 pointer, out Vector3 point)
        {
            point = Vector3.zero;

            Ray ray = camera.ScreenPointToRay(pointer);

            float slope = Vector3.Dot(ray.direction, _normal);

            //Edge on, where the plane covers no screen area and every position on it would read
            //as the same angle.
            if (Mathf.Abs(slope) < 1e-5f) return false;

            float distance = Vector3.Dot(_centre - ray.origin, _normal) / slope;

            //Behind the viewer.
            if (distance <= 0f) return false;

            point = ray.GetPoint(distance);

            return true;
        }
    }
}
