using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// The camera row of a CONTROLS table (OzFreeCam.ControlsRow): shows the free camera's middle-mouse control while FREE CAMERA is
    /// on, the row as built while it is off. Checked when the table is shown and every frame while it stays open (one bool compare).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class OzFreeCamRowText : MonoBehaviour
    {
        OzText label; string offText, onText; int shown = -1;

        /// <summary>The text on the row now (for the checks).</summary>
        public string Text => label != null ? label.text : "";
        public bool ShowsFreeCamera => shown == 1;

        public void Init(OzText l, string off, string on) { label = l; offText = off; onText = on; shown = -1; Refresh(); }

        void OnEnable() { shown = -1; Refresh(); }
        void Update() => Refresh();

        /// <summary>Sets the text for the switch as it is now (also called by the checks while the table is hidden).</summary>
        public void Refresh()
        {
            if (label == null) return;
            int want = OzFreeCam.On ? 1 : 0;
            if (want == shown) return;
            shown = want; label.Set(want == 1 ? onText : offText);
        }
    }
}
