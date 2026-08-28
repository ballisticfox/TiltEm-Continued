using UnityEngine;

namespace TiltEm
{
    /// <summary>
    /// Which axis the map and tracking-station camera treats as up, and the easing toward it.
    /// Session state only; not persisted across saves.
    /// </summary>
    public static class MapCamera
    {
        /// <summary>
        /// How fast the up axis chases a new target, in reciprocal seconds.
        /// </summary>
        private const float MapNorthSharpness = 10f;

        /// <summary>Below this, snap rather than animate the remainder.</summary>
        private const float MapNorthSnapDegrees = 0.01f;

        //Vector3.zero means "nothing established yet". A real up axis is a unit vector, so it
        //never collides with the default.
        private static Vector3 _mapNorth = Vector3.zero;

        public static MapCameraRotation MapRotation { get; private set; } = MapCameraRotation.PoleUp;

        /// <summary>Whether something is holding the rotation mode and refusing the toggle.</summary>
        public static bool RotationLocked { get; private set; }

        /// <summary>What the player had chosen before the pin took the camera.</summary>
        private static MapCameraRotation _beforeLock;

        /// <summary>The one line the rotation key writes, reused every time it writes it.</summary>
        //One ScreenMessage rather than a new one per press. ScreenMessages rewrites a message it
        //is handed again in place, and only matches an anonymous one by its exact text, so
        //posting a fresh string every time stacks a new line for every press rather than
        //replacing the last. Stock's own camera readout is a single reused instance for the same
        //reason; the refusal below shares it because it answers the same key press.
        private static readonly ScreenMessage Readout =
            new ScreenMessage("", 3f, ScreenMessageStyle.UPPER_CENTER);

        /// <summary>
        /// Pins the rotation mode until <see cref="UnlockRotation"/>. The tilt editor holds this:
        /// its handles are read against the system plane, so a camera that could roll onto the
        /// body's own pole would turn them with the thing being dragged.
        /// </summary>
        public static void LockRotation(MapCameraRotation rotation)
        {
            //Only on the way in, so re-pinning an already-pinned camera does not overwrite the
            //choice being held for the player.
            if (!RotationLocked) _beforeLock = MapRotation;

            MapRotation = rotation;
            RotationLocked = true;
        }

        /// <summary>Gives the camera back, along with the mode the player had it on.</summary>
        public static void UnlockRotation()
        {
            if (!RotationLocked) return;

            RotationLocked = false;
            MapRotation = _beforeLock;
        }

        public static void ToggleMapRotation()
        {
            //Answered on screen rather than ignored: the key otherwise looks broken, and the
            //player has no other sign that something took the camera.
            if (RotationLocked)
            {
                Announce("Rotation held by the tilt editor");
                return;
            }

            MapRotation = MapRotation == MapCameraRotation.PoleUp
                ? MapCameraRotation.SystemUp
                : MapCameraRotation.PoleUp;

            Announce("Rotation: " + MapRotationName(MapRotation));
        }

        /// <summary>Writes the rotation key's one line, replacing whatever it last said.</summary>
        private static void Announce(string text)
        {
            Readout.message = text;

            ScreenMessages.PostScreenMessage(Readout);
        }

        private static string MapRotationName(MapCameraRotation rotation)
        {
            return rotation == MapCameraRotation.SystemUp ? "System up" : "Pole up";
        }

        /// <summary>
        /// Eases the up axis toward <paramref name="target"/>, so switching rotation mode or
        /// focusing a different body swings rather than cuts.
        /// Uses 1 - exp(-k dt) rather than k * dt for frame-rate independence.
        /// </summary>
        public static Vector3 SmoothMapNorth(Vector3 target)
        {
            //First frame, or the first after a scene change. Easing from the default would swing
            //the camera up out of nowhere every time the map opens.
            if (_mapNorth == Vector3.zero)
            {
                _mapNorth = target;
                return _mapNorth;
            }

            if (Vector3.Angle(_mapNorth, target) < MapNorthSnapDegrees)
            {
                _mapNorth = target;
                return _mapNorth;
            }

            float step = 1f - Mathf.Exp(-MapNorthSharpness * Time.unscaledDeltaTime);
            _mapNorth = Vector3.Slerp(_mapNorth, target, step).normalized;

            return _mapNorth;
        }

        /// <summary>
        /// Drops the eased axis so the next frame adopts its target outright.
        /// Called on scene loads to avoid easing across a planetarium rebuild.
        /// </summary>
        public static void ResetMapNorth()
        {
            _mapNorth = Vector3.zero;
        }
    }
}
