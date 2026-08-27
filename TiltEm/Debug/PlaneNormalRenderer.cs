using UnityEngine;
// ReSharper disable UnusedMember.Global

namespace TiltEm
{
    /// <summary>
    /// Draws an arrow from the focused body along the normal of its own orbital plane, so the
    /// plane a tilt is being measured against is something you can see rather than infer.
    /// </summary>
    //Hung off the body's scaled transform like the tilt axes, and for the same reason: position
    //and the floating-origin shift arrive through the parent. The arrow's direction does not,
    //so its world rotation is written every frame - the plane normal is fixed in the sky and
    //must not turn with the body underneath it.
    [KSPAddon(KSPAddon.Startup.EveryScene, false)]
    public class PlaneNormalRenderer : MonoBehaviour
    {
        /// <summary>Length of the arrow, as a multiple of the body's drawn radius.</summary>
        private const float Length = 10f;

        /// <summary>Where the head starts, as a fraction of that length.</summary>
        private const float HeadStart = 0.82f;

        /// <summary>How much wider the head is than the shaft.</summary>
        private const float HeadWidth = 5f;

        private static readonly Color Colour = new Color(1f, 0.85f, 0.2f);

        private float _length;
        private GameObject _root;
        private LineRenderer _shaft;
        private LineRenderer _head;
        private CelestialBody _attachedTo;

        //LateUpdate, after ScaledMovement has written the parent's rotation, so the world
        //rotation set here is not stored against a stale one.
        public void LateUpdate()
        {
            CelestialBody body = DebugOptions.DrawPlaneNormal ? TargetBody() : null;

            if (body == null || body.scaledBody == null || !HasOrbit(body))
            {
                Show(false);
                return;
            }

            if (!ReferenceEquals(body, _attachedTo)) Attach(body);

            Show(true);
            Aim(body);
        }

        public void OnDestroy()
        {
            if (_root != null) Destroy(_root);
        }

        private void Attach(CelestialBody body)
        {
            _attachedTo = body;

            if (_root == null) Build();

            Transform scaled = body.scaledBody.transform;

            _root.transform.SetParent(scaled, false);
            _root.transform.localPosition = Vector3.zero;
            _root.transform.localScale = Vector3.one / scaled.localScale.x;

            //The body's own layer, not a constant of ours; layer 31 changes hands with zoom.
            _root.layer = body.scaledBody.layer;
            _shaft.gameObject.layer = body.scaledBody.layer;
            _head.gameObject.layer = body.scaledBody.layer;

            _length = (float)body.Radius * ScaledSpace.InverseScaleFactor * Length;

            _shaft.SetPosition(0, Vector3.zero);
            _shaft.SetPosition(1, Vector3.up * (_length * HeadStart));

            _head.SetPosition(0, Vector3.up * (_length * HeadStart));
            _head.SetPosition(1, Vector3.up * _length);
        }

        /// <summary>Points the arrow along the orbit normal and keeps it legible as you zoom.</summary>
        private void Aim(CelestialBody body)
        {
            //The orbital frame's Z column is the plane normal by construction, so there is
            //nothing to derive: it is the same vector the argument-of-periapsis handle turns
            //the orbit about.
            Vector3 normal = BodyAxes.ToWorld(body.orbit.OrbitFrame.Z).normalized;

            //Written in world space: the arrow marks a plane in the sky, and inheriting the
            //body's spin would have it sweep round once a day.
            _root.transform.rotation = Quaternion.FromToRotation(Vector3.up, normal);

            float width = GizmoLines.WidthFor(_length);

            _shaft.widthMultiplier = width;

            //Tapered to nothing, which is what makes it read as an arrowhead rather than a stub.
            _head.startWidth = width * HeadWidth;
            _head.endWidth = 0f;
        }

        private void Build()
        {
            _root = new GameObject("TiltEmPlaneNormal");

            Material material = new Material(GizmoLines.Shader());

            _shaft = GizmoLines.Create(_root.transform, "TiltEmNormalShaft", material, Colour, 2);
            _head = GizmoLines.Create(_root.transform, "TiltEmNormalHead", material, Colour, 2);
        }

        private void Show(bool visible)
        {
            if (_root != null && _root.activeSelf != visible) _root.SetActive(visible);
        }

        private static bool HasOrbit(CelestialBody body)
        {
            return body.orbit != null && body.orbit.referenceBody != null
                   && !ReferenceEquals(body.orbit.referenceBody, body);
        }

        private static CelestialBody TargetBody()
        {
            //The same rule the tilt axes use, so the two aids never disagree about whether a map
            //camera is up or about what it is looking at.
            if (!TiltEm.MapViewIsUp()) return null;
            if (PlanetariumCamera.fetch == null || PlanetariumCamera.fetch.target == null) return null;

            return PlanetariumCamera.fetch.target.celestialBody;
        }
    }
}
