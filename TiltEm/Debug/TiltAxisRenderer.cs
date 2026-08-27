using UnityEngine;
// ReSharper disable UnusedMember.Global

namespace TiltEm
{
    /// <summary>
    /// Draws the focused body's three axes wherever a map camera is up, so you can see a tilt
    /// instead of reading it off a number. Debugging aid only.
    /// </summary>
    //The axes are children of the body's scaled transform rather than lines rebuilt in world
    //space each frame, and that is the whole design: position, rotation, and the floating-origin
    //shift ScaledSpace applies every LateUpdate all arrive through the parent, so nothing here
    //has to win a race with the camera. The Vectrosity version drew during Update, before
    //PlanetariumCamera.LateUpdate had moved the camera and before scaled space had shifted, so
    //its lines slid off the body and flickered.
    //EveryScene rather than two attributes: AddonLoader only ever reads the first KSPAddon
    //on a type, so a second one for flight would be silently ignored. The scene test lives
    //in TargetBody instead.
    [KSPAddon(KSPAddon.Startup.EveryScene, false)]
    public class TiltAxisRenderer : MonoBehaviour
    {
        /// <summary>Half-length of each axis, as a multiple of the body's drawn radius.</summary>
        private const float ArmLength = 15f;

        /// <summary>Line thickness as a fraction of camera distance, so it stays legible.</summary>
        //Width is the one thing still computed per frame. Unlike position, a frame of lag in it
        //cannot be seen.
        private const float WidthPerDistance = 0.004f;

        /// <summary>Ceiling on that thickness, as a fraction of the arm's length.</summary>
        //Without it the width grows without bound as you zoom out, and past roughly a tenth of
        //the arm the three lines stop reading as axes and become camera-facing slabs sitting
        //across the view. Going sub-pixel far away is the right trade.
        private const float MaxWidthFraction = 0.01f;


        //Blue marks the pole, the axis worth looking at. These are not Unity's gizmo colours.
        private static readonly Vector3[] Directions = { Vector3.right, Vector3.up, Vector3.forward };
        private static readonly Color[] Colors = { Color.red, Color.blue, Color.green };

        private float _arm;
        private GameObject _root;
        private LineRenderer[] _lines;
        private CelestialBody _attachedTo;

        //LateUpdate to sit alongside stock's own line renderers, though nothing here needs it.
        public void LateUpdate()
        {
            CelestialBody body = DebugOptions.DrawAxes ? TargetBody() : null;

            if (body == null || body.scaledBody == null)
            {
                Show(false);
                return;
            }

            if (!ReferenceEquals(body, _attachedTo)) Attach(body);

            Show(true);
            UpdateWidth();
        }

        public void OnDestroy()
        {
            if (_root != null) Destroy(_root);
        }

        /// <summary>Hangs the axes off the body's scaled transform, sized to that body.</summary>
        private void Attach(CelestialBody body)
        {
            _attachedTo = body;

            if (_root == null) Build();

            Transform scaled = body.scaledBody.transform;

            //The body's own layer, not a constant of ours. Layer 31 belongs to whichever
            //camera is drawing orbit lines: past max3DlineDrawDist MapView switches Vectrosity
            //to its 2D path and hands 31 to the screen-space canvas camera, which then draws our
            //world-space lines through a camera positioned in screen pixels. The scaled bodies
            //keep their own layer at every zoom, so sharing it keeps the axes with them.
            SetLayer(body.scaledBody.layer);

            _root.transform.SetParent(scaled, false);
            _root.transform.localPosition = Vector3.zero;
            _root.transform.localRotation = Quaternion.identity;

            //Cancelling the parent's scale lets everything inside be written in scaled-space
            //units. Bodies are spheres, so the scale is uniform and one axis is enough to read.
            float scale = scaled.localScale.x;
            _root.transform.localScale = Vector3.one / scale;

            _arm = (float)body.Radius * ScaledSpace.InverseScaleFactor * ArmLength;

            for (int i = 0; i < _lines.Length; i++)
            {
                _lines[i].SetPosition(0, Directions[i] * _arm);
                _lines[i].SetPosition(1, Directions[i] * -_arm);
            }
        }

        /// <summary>Puts the axes on the same layer as the body they annotate.</summary>
        private void SetLayer(int layer)
        {
            _root.layer = layer;

            foreach (LineRenderer line in _lines)
            {
                line.gameObject.layer = layer;
            }
        }

        private void Build()
        {
            _root = new GameObject("TiltEmAxes");

            Material material = new Material(LineShader());

            _lines = new LineRenderer[Directions.Length];

            for (int i = 0; i < _lines.Length; i++)
            {
                GameObject go = new GameObject("TiltEmAxis" + i);
                go.transform.SetParent(_root.transform, false);

                LineRenderer line = go.AddComponent<LineRenderer>();
                line.material = material;
                line.startColor = Colors[i];
                line.endColor = Colors[i];
                line.positionCount = 2;

                //Local, not world: this is what makes the lines follow the body for free.
                line.useWorldSpace = false;
                line.alignment = LineAlignment.View;

                _lines[i] = line;
            }
        }

        /// <summary>Keeps the lines a roughly constant thickness on screen as you zoom.</summary>
        private void UpdateWidth()
        {
            if (PlanetariumCamera.fetch == null) return;

            float width = Mathf.Min(PlanetariumCamera.fetch.Distance * WidthPerDistance,
                _arm * MaxWidthFraction);

            foreach (LineRenderer line in _lines)
            {
                line.widthMultiplier = width;
            }
        }

        private void Show(bool visible)
        {
            if (_root != null && _root.activeSelf != visible) _root.SetActive(visible);
        }

        /// <summary>An unlit shader KSP is known to carry, with fallbacks.</summary>
        private static Shader LineShader()
        {
            Shader shader = Shader.Find("Legacy Shaders/Particles/Alpha Blended")
                            ?? Shader.Find("Particles/Alpha Blended")
                            ?? Shader.Find("Sprites/Default");

            if (shader == null)
            {
                Debug.LogWarning("[TiltEm]: no shader found for the tilt axes; they will not draw.");
            }

            return shader;
        }

        private static CelestialBody TargetBody()
        {
            //The same rule the map-rotation key uses, so the two never disagree about whether
            //a map camera is up.
            if (!TiltEm.MapViewIsUp()) return null;
            if (PlanetariumCamera.fetch == null || PlanetariumCamera.fetch.target == null) return null;

            return PlanetariumCamera.fetch.target.celestialBody;
        }
    }
}
