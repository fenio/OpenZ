using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// k252: what the ready-gun tiles (OzReadyGuns) need from the HUD's press handling. A press that began on a tile and turned into
    /// a drag is handed to the map: the tile no longer counts as pressed (no click when the button comes up), the press is no longer
    /// the HUD's, and PlayerInput, held back while the HUD owned the press, runs again, so its placement (the same as PLACE) follows
    /// the pointer and places the gun where the button is let go.
    /// </summary>
    public partial class OpenZHud
    {
        internal void HandPressToMap(HudButton b)
        {
            if (b != null && leftDown == b) { leftDown = null; b.Pressed = false; b.Refresh(); }
            PressCapture = Capture.None; PressBuilding = null;
            if (gatedByUs && input) { input.enabled = true; gatedByUs = false; }
        }

        /// <summary>The HUD has slid out at the end of the match.</summary>
        internal bool HasExited => exited;

        /// <summary>IsOverUi without the blockers under `mine` (a tile is hidden where any other HUD panel, or an open build menu, is).</summary>
        internal bool OverOtherUi(Vector2 screen, Transform mine)
        {
            if (Modal) return true;
            if (!hudVisible || !Canvas) return false;
            foreach (var b in blockers) if (b && (mine == null || !b.IsChildOf(mine)) && Contains(b, screen)) return true;
            return false;
        }
    }
}
