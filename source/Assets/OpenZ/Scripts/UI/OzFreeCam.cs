using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// FREE CAMERA (Options, GAME tab, under EDGE SCROLL; both the main menu and the pause menu). OFF by default.
    /// ON: the camera behaves exactly as with the testing overlay (P) open: holding the middle mouse button turns (mouse left / right)
    /// and tilts (mouse up / down) the view round the point it looks at, instead of dragging the map; released, the view stays; the
    /// minimap's view box turns with it; keys, the screen edge, the wheel, fly-tos and clicks follow the turned view (RtsCamera's free
    /// look, RtsCamera.FreeTurn). OFF again: the view goes back to the fixed north-up view (0.6 s), as when the overlay closes.
    /// The ending's camera lock and the briefing flight end the free look exactly as before. Saved in oz_free_camera (0 / 1), live.
    /// The CONTROLS tables name the free camera's middle-mouse control only while this is on (OzFreeCamRowText).
    /// </summary>
    public static class OzFreeCam
    {
        public const string PrefKey = "oz_free_camera";
        public const string Label = "FREE CAMERA";
        public const string Tip = "FREE CAMERA|Off (default): the middle mouse button drags the map. On: hold the middle mouse button and move the mouse to turn and tilt the view; the minimap's view box turns with it. Switch it off to go back to the fixed view.|";

        static int on = -1;
        /// <summary>The player's switch (read once, then kept here; saved at once when set).</summary>
        public static bool On
        {
            get { if (on < 0) on = PlayerPrefs.GetInt(PrefKey, 0) == 1 ? 1 : 0; return on == 1; }
            set
            {
                on = value ? 1 : 0; PlayerPrefs.SetInt(PrefKey, on); PlayerPrefs.Save();
                Debug.Log($"[OpenZ Options] free camera: {(value ? "ON (middle mouse button: turn and tilt)" : "OFF (middle mouse button: drag the map)")} ({PrefKey} {on}, saved, live)");
            }
        }

        // ---- the CONTROLS tables (OzControlsTable): the row that names the middle mouse button reads differently while this is on
        /// <summary>The main menu's CONTROLS table: the camera row, as built (off) and while the free camera is on.</summary>
        public const string FullRowOff = "Zoom · middle drag flies the view", FullRowOn = "Zoom · free camera: middle drag turns and tilts";
        /// <summary>The pause menu's and the help overlay's short table: the scroll row, as built (off) and while the free camera is on.</summary>
        public const string CompactRowOff = "Scroll (arrows too) · wheel zooms", CompactRowOn = "Scroll · zoom · free camera: middle drag";

        /// <summary>OzControlsTable hook: a row label of the camera row gets a small live text switch; every other row is left as it is.
        /// Returns the label.</summary>
        public static OzText ControlsRow(OzText label, string text)
        {
            if (label == null || label.rectTransform == null) return label;
            string onText = text == FullRowOff ? FullRowOn : text == CompactRowOff ? CompactRowOn : null;
            if (onText == null) return label;
            var sw = label.rectTransform.gameObject.AddComponent<OzFreeCamRowText>();
            sw.Init(label, text, onText);
            return label;
        }
    }
}
