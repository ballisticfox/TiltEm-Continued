using KSP.UI.Screens.DebugToolbar.Screens;
using UnityEngine;

namespace TiltEm
{
    /// <summary>
    /// The checkmark that opens the selected body for editing.
    /// </summary>
    //It reports on whatever the tracking station has selected rather than on the body being
    //edited, so picking a second body and ticking it moves the editor there. That is the same
    //gesture as opening the first one.
    internal abstract class EditBodyToggle : DebugScreenToggle
    {
        /// <summary>True while the screen is writing the toggle, so its own callback stands down.</summary>
        //Toggle.isOn fires the callback whatever set it, and letting a refresh be mistaken for a
        //click would end a session the moment the player selected a different body.
        private bool _refreshing;

        protected abstract BodyEditTarget Target { get; }

        /// <summary>Puts the toggle in step with what is selected and what is open.</summary>
        internal void Refresh(CelestialBody selected)
        {
            SetToggleText("Edit selected body (" + (selected == null ? "none" : selected.bodyName) + ")");

            _refreshing = true;
            Set(BodyEditor.IsEditing(selected));
            _refreshing = false;
        }

        protected override void OnToggleChanged(bool state)
        {
            if (_refreshing) return;

            CelestialBody selected = BodyEditor.SelectedBody();

            if (state)
            {
                BodyEditor.SetTarget(Target);
                BodyEditor.Begin(selected);
                return;
            }

            //Only if it is this body that is open. Unticking a box that was showing some other
            //body's session should not close that session.
            if (BodyEditor.IsEditing(selected)) BodyEditor.End();
        }
    }

    internal class EditTiltBodyToggle : EditBodyToggle
    {
        protected override BodyEditTarget Target => BodyEditTarget.Tilt;
    }

    internal class EditOrbitBodyToggle : EditBodyToggle
    {
        protected override BodyEditTarget Target => BodyEditTarget.Orbit;
    }

    /// <summary>Swaps which pair of numbers the tilt handles drive.</summary>
    internal class TiltModeButton : DebugButton
    {
        protected override void OnClick()
        {
            BodyEdit edit = BodyEditor.Active;

            if (edit == null) return;

            edit.Tilt.Mode = edit.Tilt.Mode == TiltEditMode.Tilt ? TiltEditMode.Pole : TiltEditMode.Tilt;
        }

        /// <summary>Names the mode the button is in, not the one it would switch to.</summary>
        internal void Refresh(BodyEdit edit)
        {
            TiltEditMode mode = edit == null ? TiltEditMode.Pole : edit.Tilt.Mode;

            SetLabel(mode == TiltEditMode.Pole ? "Mode: Pole (RA/Dec)" : "Mode: Tilt (tiltx/tiltz)");
        }
    }

    /// <summary>
    /// Base for the two flags that move a body's numbers into its parent's equatorial frame.
    /// </summary>
    internal abstract class RelativeToParentToggle : DebugScreenToggle
    {
        private bool _refreshing;

        internal void Refresh(BodyEdit edit)
        {
            _refreshing = true;
            Set(IsOn(edit));
            _refreshing = false;
        }

        protected override void OnToggleChanged(bool state)
        {
            if (_refreshing) return;

            Apply(BodyEditor.Active, state);
        }

        protected abstract bool IsOn(BodyEdit edit);

        protected abstract void Apply(BodyEdit edit, bool state);
    }

    /// <summary>Whether the pole is written against the parent's equator.</summary>
    internal class TiltFrameToggle : RelativeToParentToggle
    {
        protected override bool IsOn(BodyEdit edit)
        {
            return edit != null && edit.Tilt.RelativeToParent;
        }

        protected override void Apply(BodyEdit edit, bool state)
        {
            if (edit != null) edit.Tilt.RelativeToParent = state;
        }
    }

    /// <summary>Whether the orbit handles work in the parent's equatorial frame.</summary>
    internal class OrbitFrameToggle : RelativeToParentToggle
    {
        protected override bool IsOn(BodyEdit edit)
        {
            return edit != null && edit.Orbit != null && edit.Orbit.RelativeToParent;
        }

        protected override void Apply(BodyEdit edit, bool state)
        {
            if (edit != null && edit.Orbit != null) edit.Orbit.RelativeToParent = state;
        }
    }

    /// <summary>Puts the open body's tilt back, leaving the session and the mode as they are.</summary>
    internal class ResetTiltButton : DebugButton
    {
        protected override void OnClick()
        {
            BodyEdit edit = BodyEditor.Active;

            if (edit != null) edit.Tilt.Revert();
        }
    }

    /// <summary>Puts the open body's orbit back.</summary>
    internal class ResetOrbitButton : DebugButton
    {
        protected override void OnClick()
        {
            BodyEdit edit = BodyEditor.Active;

            if (edit != null && edit.Orbit != null) edit.Orbit.Revert();
        }
    }

    /// <summary>Puts every body edited this session back, wherever they were edited from.</summary>
    //On the main tab rather than beside the per-editor resets: it undoes work the player may
    //have done on bodies they are no longer looking at, which is not something to put next to a
    //button that undoes only what is in front of them.
    internal class ResetAllEditsButton : DebugButton
    {
        protected override void OnClick()
        {
            BodyEditor.ResetAll();
        }
    }

    /// <summary>Writes the open body out as a config, and says where it went.</summary>
    //The path is worth more than the fact it worked: the file lands in PluginData, which is not
    //somewhere anyone looks by habit. It clears itself afterwards so a path from ten minutes ago
    //is not still sitting there looking like the result of the button just pressed.
    internal class ExportButton : DebugButton
    {
        /// <summary>How long the result stays on screen, in seconds.</summary>
        private const float ResultSeconds = 10f;

        [SerializeField]
        private string _result;
        [SerializeField]
        private float _clearAt;

        internal string Result => Time.unscaledTime < _clearAt ? _result : string.Empty;

        protected override void OnClick()
        {
            BodyEdit edit = BodyEditor.Active;

            Show(Describe(edit));
        }

        private static string Describe(BodyEdit edit)
        {
            if (edit == null) return "No body is open.";
            if (!edit.Dirty) return edit.Body.bodyName + " has not been changed.";

            return EditExporter.Export(edit) ?? "The write failed; see KSP.log.";
        }

        private void Show(string result)
        {
            _result = result;
            _clearAt = Time.unscaledTime + ResultSeconds;
        }
    }
}
