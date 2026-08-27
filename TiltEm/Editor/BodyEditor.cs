using System.Collections.Generic;

namespace TiltEm
{
    /// <summary>
    /// The editing session: which body is open, in which editor, and every body edited so far.
    ///
    /// Session state only. Nothing here reaches a save file, and every edit lasts until the game
    /// restarts or the player resets it.
    /// </summary>
    //Static because the edits outlive the tab that made them: closing the session hands the body
    //back to the camera but leaves it where the player put it.
    public static class BodyEditor
    {
        private static readonly Dictionary<string, BodyEdit> Edits = new Dictionary<string, BodyEdit>();

        /// <summary>The body currently open for editing, or null when none is.</summary>
        public static BodyEdit Active { get; private set; }

        /// <summary>Which editor the open tab is, which decides whether the camera is pinned.</summary>
        public static BodyEditTarget Target { get; private set; }

        /// <summary>Every body edited this session, open or not.</summary>
        public static ICollection<BodyEdit> Edited => Edits.Values;

        /// <summary>
        /// Whether the editor may run at all: the tracking station, and not alongside Principia.
        /// </summary>
        //Tracking station only, as asked. It is also the one scene with no active vessel to be
        //dragged along by a body that moves, and no dominant body whose rotating frame a change
        //of pole would invalidate.
        public static bool Available => !PrincipiaCheck.Installed
                                        && HighLogic.LoadedScene == GameScenes.TRACKSTATION;

        /// <summary>Whether this body can be opened in the editor the tab is showing.</summary>
        public static bool CanEdit(CelestialBody body)
        {
            if (!Available || body == null) return false;

            //A root star orbits nothing, so there is no orbit editor for it. Its tilt is fair game.
            return Target != BodyEditTarget.Orbit || HasOrbit(body);
        }

        /// <summary>The body the tracking station has selected, whatever it is focused on.</summary>
        public static CelestialBody SelectedBody()
        {
            return Available ? MapFocus.Body() : null;
        }

        /// <summary>Whether this body is the one currently open.</summary>
        public static bool IsEditing(CelestialBody body)
        {
            return body != null && Active != null && ReferenceEquals(Active.Body, body);
        }

        /// <summary>
        /// Switches tab. The camera is pinned only while the tilt editor has a body open, so this
        /// can release it.
        /// </summary>
        public static void SetTarget(BodyEditTarget target)
        {
            //Called every frame by whichever tab is open, so it has to be free when nothing
            //changed. Begin and End refresh the pin themselves.
            if (Target == target) return;

            Target = target;

            //The orbit editor has no business with a body whose orbit does not exist, and the
            //tab it just left did.
            if (Active != null && target == BodyEditTarget.Orbit && Active.Orbit == null) End();

            RefreshCameraPin();
        }

        /// <summary>
        /// Opens a body for editing, reusing the edit already held for it. Returns null when the
        /// body cannot be edited.
        /// </summary>
        public static BodyEdit Begin(CelestialBody body)
        {
            if (!CanEdit(body)) return null;

            if (!Edits.TryGetValue(body.bodyName, out BodyEdit edit))
            {
                edit = new BodyEdit(body);
                Edits[body.bodyName] = edit;
            }

            Active = edit;
            RefreshCameraPin();

            return edit;
        }

        /// <summary>Closes the open body, leaving its edit in place.</summary>
        public static void End()
        {
            Active = null;
            RefreshCameraPin();
        }

        /// <summary>Drops every edit, putting all of it back.</summary>
        public static void ResetAll()
        {
            CelestialBody open = Active == null ? null : Active.Body;

            foreach (BodyEdit edit in Edits.Values)
            {
                edit.Revert();
            }

            Edits.Clear();
            Active = null;

            if (open != null) Begin(open);
            else RefreshCameraPin();
        }

        /// <summary>
        /// Closes the open body on a scene change, keeping its edit. Called from Tilt'Em's own
        /// scene hook, which already runs before anything the new scene builds.
        /// </summary>
        //The camera has to be released here whatever else happens: a pin left set outlives the
        //tracking station and would refuse the map-rotation key for the rest of the session.
        public static void SceneChanged()
        {
            End();
        }

        /// <summary>
        /// Holds the map camera on the system plane while a tilt is being dragged, and lets go
        /// the moment it is not.
        /// </summary>
        //The tilt handles are read against the system plane. If the camera could roll onto the
        //body's own pole, the handles would turn with the thing being dragged.
        private static void RefreshCameraPin()
        {
            if (Active != null && Target == BodyEditTarget.Tilt)
            {
                MapCamera.LockRotation(MapCameraRotation.SystemUp);
                return;
            }

            MapCamera.UnlockRotation();
        }

        private static bool HasOrbit(CelestialBody body)
        {
            return body.orbit != null && body.orbit.referenceBody != null
                   && !ReferenceEquals(body.orbit.referenceBody, body);
        }
    }
}
