using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

namespace TiltEm
{
    /// <summary>
    /// The rings you grab in the tracking station, and the drag that turns one into an edit.
    /// </summary>
    //Hung off the body's scaled transform for the same reason the tilt axes are: position and
    //the floating-origin shift arrive through the parent, so the rings cannot slide off the body
    //the way a set of world-space lines rebuilt each frame does.
    //After the default order, because ScaledMovement.OnLateUpdate is where a scaled body's
    //rotation is written, and it is a LateUpdate like this one. Pointing a ring writes a world
    //rotation, which Unity stores against the parent's rotation as it stands at that moment, so
    //running first would leave every ring turning with the body it is meant to be turning.
    [DefaultExecutionOrder(100)]
    [KSPAddon(KSPAddon.Startup.TrackingStation, false)]
    public class EditorHandles : MonoBehaviour
    {
        private const string LockName = "TiltEmEditorHandles";

        /// <summary>What the modifier key multiplies a drag by, for placing a value exactly.</summary>
        private const float FineControl = 0.1f;

        /// <summary>Which rings each editor shows, and where each one sits.</summary>
        //Radii are multiples of the body's own radius for a tilt, and of the orbit's semi-major
        //axis for an orbit. Spread out so no two rings overlap at any viewing angle, and inside
        //the tilt axes so the two aids do not fight for the same pixels.
        private static readonly Ring[] Rings =
        {
            new Ring(EditHandle.Spin, Color.blue, 1.5f),
            new Ring(EditHandle.TiltX, Color.red, 2.25f),
            new Ring(EditHandle.TiltZ, Color.green, 3f),
            new Ring(EditHandle.PoleRa, Color.cyan, 2.25f),
            new Ring(EditHandle.PoleDec, Color.magenta, 3f),
            new Ring(EditHandle.LongitudeOfAscendingNode, Color.cyan, 0.94f),
            new Ring(EditHandle.Inclination, Color.magenta, 1f),
            new Ring(EditHandle.ArgumentOfPeriapsis, Color.yellow, 1.06f),
        };

        private static readonly EditHandle[] TiltMode =
            { EditHandle.Spin, EditHandle.TiltX, EditHandle.TiltZ };

        private static readonly EditHandle[] PoleMode =
            { EditHandle.Spin, EditHandle.PoleRa, EditHandle.PoleDec };

        private static readonly EditHandle[] OrbitMode =
        {
            EditHandle.LongitudeOfAscendingNode, EditHandle.Inclination, EditHandle.ArgumentOfPeriapsis,
        };

        private readonly Dictionary<EditHandle, DragRing> _rings = new Dictionary<EditHandle, DragRing>();

        private GameObject _root;
        private CelestialBody _anchor;
        private DragRing _dragging;
        private float _dragAngle;
        private bool _locked;

        private struct Ring
        {
            public readonly EditHandle Handle;
            public readonly Color Color;
            public readonly float RadiusFactor;

            public Ring(EditHandle handle, Color color, float radiusFactor)
            {
                Handle = handle;
                Color = color;
                RadiusFactor = radiusFactor;
            }
        }

        // ReSharper disable once UnusedMember.Global
        public void LateUpdate()
        {
            BodyEdit edit = BodyEditor.Active;

            if (edit == null || !BodyEditor.Available)
            {
                Stop();
                return;
            }

            CelestialBody anchor = AnchorFor(edit);

            if (anchor == null || anchor.scaledBody == null)
            {
                Stop();
                return;
            }

            if (_root == null) Build();
            if (!ReferenceEquals(anchor, _anchor)) Attach(anchor);

            EditHandle[] active = ActiveHandles(edit);

            Place(edit, anchor, active);
            Interact(edit, active);
        }

        // ReSharper disable once UnusedMember.Global
        public void OnDestroy()
        {
            //Before the GameObject goes: a control lock left behind outlives the scene and takes
            //the tracking station's camera with it.
            Stop();

            if (_root != null) Destroy(_root);
        }

        /// <summary>The body the rings are drawn around.</summary>
        //An orbit is drawn about the body it goes round, not the body that goes round it.
        private static CelestialBody AnchorFor(BodyEdit edit)
        {
            if (BodyEditor.Target != BodyEditTarget.Orbit) return edit.Body;

            return edit.Orbit == null ? null : edit.Orbit.Parent;
        }

        private static EditHandle[] ActiveHandles(BodyEdit edit)
        {
            if (BodyEditor.Target == BodyEditTarget.Orbit) return edit.Orbit == null ? new EditHandle[0] : OrbitMode;

            return edit.Tilt.Mode == TiltEditMode.Pole ? PoleMode : TiltMode;
        }

        /// <summary>What one unit of a ring's radius factor is worth, in scaled-space units.</summary>
        private static float BaseRadius(BodyEdit edit, CelestialBody anchor)
        {
            if (BodyEditor.Target == BodyEditTarget.Orbit && edit.Orbit != null)
            {
                return (float)(edit.Orbit.SemiMajorAxis * ScaledSpace.InverseScaleFactor);
            }

            return (float)(anchor.Radius * ScaledSpace.InverseScaleFactor);
        }

        private void Place(BodyEdit edit, CelestialBody anchor, EditHandle[] active)
        {
            Vector3 centre = anchor.scaledBody.transform.position;
            float radius = BaseRadius(edit, anchor);

            foreach (Ring ring in Rings)
            {
                DragRing drag = _rings[ring.Handle];
                bool on = Array.IndexOf(active, ring.Handle) >= 0;

                drag.Show(on);

                if (on) drag.Place(centre, WorldAxis(edit, ring.Handle), radius * ring.RadiusFactor);
            }
        }

        /// <summary>The handle's rotation axis, in world space.</summary>
        private static Vector3 WorldAxis(BodyEdit edit, EditHandle handle)
        {
            Vector3d celestial = BodyEditor.Target == BodyEditTarget.Orbit
                ? OrbitAxis(edit, handle)
                : TiltAxis(edit, handle);

            return BodyAxes.ToWorld(celestial).normalized;
        }

        private static Vector3d TiltAxis(BodyEdit edit, EditHandle handle)
        {
            TiltEdit tilt = edit.Tilt;

            //The pole is asked for in the celestial frame whatever the numbers are written in:
            //the spin handle turns the body about where its axis actually points.
            Vector3d axis = HandleAxes.Tilt(handle, tilt.PoleRa, tilt.TiltX, tilt.Tilt.Tilt.Z);

            //The other axes belong to the frame the numbers live in, so a pole written against a
            //tilted parent needs its axes carried into the celestial frame before they can be
            //drawn against the sky.
            if (handle != EditHandle.Spin && tilt.RelativeToParent
                && TiltEm.TryGetTilt(edit.Body.referenceBody.bodyName, out BodyTilt parent))
            {
                axis = parent.Tilt.LocalToWorld(axis);
            }

            return axis;
        }

        private static Vector3d OrbitAxis(BodyEdit edit, EditHandle handle)
        {
            Vector3d axis = HandleAxes.Orbit(handle, edit.Orbit.Orientation);

            //Parent-relative elements are written in the parent's equatorial frame, so the axes
            //that turn them live there too and have to be carried into the celestial frame
            //before anything can be drawn against the sky.
            if (edit.Orbit.RelativeToParent
                && TiltEm.TryGetTilt(edit.Orbit.Parent.bodyName, out BodyTilt parent) && !parent.IsIdentity)
            {
                axis = parent.Tilt.LocalToWorld(axis);
            }

            return axis;
        }

        private void Interact(BodyEdit edit, EditHandle[] active)
        {
            Camera camera = PlanetariumCamera.Camera;

            if (camera == null) return;

            Vector3 pointer = Input.mousePosition;

            if (_dragging != null)
            {
                //Kept lit for the whole drag, so the ring being turned stays the one that
                //obviously is even once the pointer has left it.
                Highlight(_dragging);

                if (Input.GetMouseButton(0)) Drag(edit, camera, pointer);
                else EndDrag();

                return;
            }

            DragRing nearest = Nearest(camera, pointer, active);

            Highlight(nearest);

            //Not while the pointer is over the debug menu, which sits in front of all of this.
            if (nearest == null || !Input.GetMouseButtonDown(0) || PointerOverUi()) return;

            BeginDrag(nearest, camera, pointer);
        }

        /// <summary>The ring nearest the pointer, if the pointer is close enough to grab one.</summary>
        private DragRing Nearest(Camera camera, Vector3 pointer, EditHandle[] active)
        {
            DragRing nearest = null;
            float best = float.MaxValue;

            foreach (EditHandle handle in active)
            {
                DragRing ring = _rings[handle];

                if (!ring.TryGrabDistance(camera, pointer, out float pixels) || pixels >= best) continue;

                nearest = ring;
                best = pixels;
            }

            return nearest;
        }

        private void Highlight(DragRing hovered)
        {
            foreach (Ring ring in Rings)
            {
                _rings[ring.Handle].SetHighlighted(ReferenceEquals(_rings[ring.Handle], hovered));
            }
        }

        private void BeginDrag(DragRing ring, Camera camera, Vector3 pointer)
        {
            ring.BeginDrag(camera, pointer);

            if (!ring.TryAngle(camera, pointer, out _dragAngle)) return;

            _dragging = ring;

            //MAP_UI only, and never CAMERACONTROLS. Stock writes pivot.rotation - the rotation
            //that cancels the sky - in two branches: one that runs while camera controls are
            //unlocked, and one that runs while everything is. Lock only the camera and neither
            //fires, so the pivot keeps last frame's rotation and the whole view rides along with
            //the rotating frame until the lock comes off. Nothing is lost by leaving it unlocked:
            //the map camera orbits on the right button, so a left-drag was never going to swing
            //it. MAP_UI is still worth holding, to stop a double-click retargeting mid-drag.
            InputLockManager.SetControlLock(ControlTypes.MAP_UI, LockName);
            _locked = true;
        }

        private void Drag(BodyEdit edit, Camera camera, Vector3 pointer)
        {
            if (!_dragging.TryAngle(camera, pointer, out float angle)) return;

            float delta = Mathf.DeltaAngle(_dragAngle, angle);

            //Taken from where the pointer is now rather than accumulated from where it started,
            //so a frame the ring could not be read in costs that frame and nothing more.
            _dragAngle = angle;

            if (GameSettings.MODIFIER_KEY.GetKey()) delta *= FineControl;

            Apply(edit, _dragging.Handle, delta);
        }

        private void EndDrag()
        {
            _dragging = null;

            if (!_locked) return;

            InputLockManager.RemoveControlLock(LockName);
            _locked = false;
        }

        private static void Apply(BodyEdit edit, EditHandle handle, float degrees)
        {
            switch (handle)
            {
                case EditHandle.PoleRa:
                    edit.Tilt.NudgePole(degrees, 0.0);
                    break;
                case EditHandle.PoleDec:
                    edit.Tilt.NudgePole(0.0, degrees);
                    break;
                case EditHandle.TiltX:
                    edit.Tilt.NudgeLegacyTilt(degrees, 0.0);
                    break;
                case EditHandle.TiltZ:
                    edit.Tilt.NudgeLegacyTilt(0.0, degrees);
                    break;
                case EditHandle.Spin:
                    edit.Tilt.NudgeInitialRotation(degrees);
                    break;
                case EditHandle.Inclination:
                    if (edit.Orbit != null) edit.Orbit.NudgeOrientation(degrees, 0.0, 0.0);
                    break;
                case EditHandle.LongitudeOfAscendingNode:
                    if (edit.Orbit != null) edit.Orbit.NudgeOrientation(0.0, degrees, 0.0);
                    break;
                case EditHandle.ArgumentOfPeriapsis:
                    if (edit.Orbit != null) edit.Orbit.NudgeOrientation(0.0, 0.0, degrees);
                    break;
            }
        }

        private static bool PointerOverUi()
        {
            return EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
        }

        /// <summary>Puts every ring away and gives the camera back.</summary>
        private void Stop()
        {
            EndDrag();

            if (_root == null) return;

            foreach (Ring ring in Rings)
            {
                _rings[ring.Handle].Show(false);
            }
        }

        private void Build()
        {
            _root = new GameObject("TiltEmHandles");

            Material material = new Material(GizmoLines.Shader());

            foreach (Ring ring in Rings)
            {
                _rings[ring.Handle] = new DragRing(_root.transform, ring.Handle, material,
                    ring.Color, ring.RadiusFactor);
            }
        }

        /// <summary>Hangs the rings off a body's scaled transform, in that body's own units.</summary>
        private void Attach(CelestialBody anchor)
        {
            _anchor = anchor;

            Transform scaled = anchor.scaledBody.transform;

            _root.transform.SetParent(scaled, false);
            _root.transform.localPosition = Vector3.zero;
            _root.transform.localRotation = Quaternion.identity;

            //Cancelling the parent's scale lets everything inside be written in scaled-space
            //units. Bodies are spheres, so the scale is uniform and one axis reads it.
            _root.transform.localScale = Vector3.one / scaled.localScale.x;

            //The body's own layer, not a constant of ours. Layer 31 belongs to whichever camera
            //is drawing orbit lines and changes hands with zoom; the scaled bodies keep theirs.
            _root.layer = anchor.scaledBody.layer;

            foreach (Ring ring in Rings)
            {
                _rings[ring.Handle].SetLayer(anchor.scaledBody.layer);
            }
        }
    }
}
