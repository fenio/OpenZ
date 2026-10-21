using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// The MINIMAL UI key (OzAction.MinimalUi, default U, changed in the main menu's CONTROLS like every other key): in a match it
    /// flips the setting of the OPTIONS switch MINIMAL UI through the switch's own setter (OzSettingTips.MinimalUi), so it is saved
    /// at once (oz_minimal_ui), the HUD fades its always-on panels out or in (TickMinimal) and the switch shows the new state the
    /// next time OPTIONS is open (the row reads the setting every frame).
    ///
    /// The key does nothing while a menu owns the keyboard (OpenZHud.Modal: the pause menu, the end screen, the
    /// arrange mode), while a yes / no question is up, while the HUD is hidden as a whole (its own key, a briefing) and once the
    /// match is over. With Ctrl or Alt held it is not this key (OpenZHud.Update tests them, as for the factory list key).
    ///
    /// Feedback: the switch's own sounds (ui_toggle_on / ui_toggle_off) and one message plate in the toast style,
    /// "MINIMAL UI ON · KEY U" / "MINIMAL UI OFF · KEY U" (the bound key's name). MINIMAL UI hides the message plates; this one is
    /// the answer to the player's own key press, so it is drawn in both states (ToastView.keep) for MinimalNoteHold seconds. A
    /// press while the plate is still up rewrites that plate in its slot: ten quick presses never stack ten plates.
    /// </summary>
    public partial class OpenZHud
    {
        /// <summary>Seconds the key's note stays (the feedback toasts' own time).</summary>
        public const float MinimalNoteHold = 2.2f;
        /// <summary>Checks: times the key flipped MINIMAL UI since the game started.</summary>
        public static int MinimalKeyFlips { get; private set; }

        /// <summary>The key's note as it stands on the screen now, or null (checks).</summary>
        public string MinimalNoteText
        {
            get { foreach (var x in toasts) if (x.used && x.keep && x.rt.gameObject.activeInHierarchy) return x.text.text; return null; }
        }

        /// <summary>How many of the four always-on panels are switched on: status strip, territory tab, unit panel, command bar (checks).</summary>
        public int MinimalPanelsOn
        {
            get
            {
                int n = 0;
                if (top && top.gameObject.activeSelf) n++;
                if (terr && terr.gameObject.activeSelf) n++;
                if (unitPanel && unitPanel.gameObject.activeSelf) n++;
                if (cmdBar && cmdBar.gameObject.activeSelf) n++;
                return n;
            }
        }

        /// <summary>The MENU button and the minimap, which MINIMAL UI keeps, are switched on (checks).</summary>
        public bool MinimalKeepsOn => menuBtn != null && menuBtn.Rt && menuBtn.Rt.gameObject.activeSelf && Minimap && Minimap.Panel && Minimap.Panel.gameObject.activeSelf;

        /// <summary>The note's text for a state: the OPTIONS row's own name, the state, the bound key.</summary>
        public static string MinimalNote(bool on) => OzSettingTips.MinimalLabel + (on ? " ON" : " OFF") + " · KEY " + OzBindings.Cap(OzAction.MinimalUi);

        /// <summary>The bound key went down (OpenZHud.Update), or a check stands in for the key. True = MINIMAL UI was flipped.</summary>
        public bool MinimalUiKey()
        {
            if (Modal || OzConfirm.Blocks || !hudVisible || exited || W == null || W.GameOver) return false;
            bool on = !OzSettingTips.MinimalUi;
            OzSettingTips.MinimalUi = on;                                                                  // the OPTIONS switch's own setter: saved at once, the panels follow (TickMinimal)
            OzSfx.Ui(on ? "ui_toggle_on" : "ui_toggle_off");                                               // the switch's own sounds
            MinimalKeyFlips++;
            string text = MinimalNote(on);
            ToastView note = null;
            foreach (var x in toasts) if (x.used && x.keep) { note = x; break; }
            if (BuildMenuShowing) OzSoundLog.Add("hud", "toast", text, "held back (build menu open)");     // no plate over the open build menu; the switch's sound has answered
            else if (note == null) ShowToast(text, "chevron", false, null, true, MinimalNoteHold, true);
            else
            {   // still up from the last press: the same plate, in its slot, from the start of its time
                note.baseText = text; note.count = 1; note.text.Set(text); note.t0 = OzMotion.Now; note.hold = MinimalNoteHold;
                OzSoundLog.Add("hud", "toast", text, "shown");
            }
            Debug.Log($"[OpenZ HUD] MINIMAL UI key ({OzBindings.Cap(OzAction.MinimalUi)}): {(on ? "ON" : "OFF")}");
            return true;
        }
    }
}
