using UnityEngine;
using UnityEngine.InputSystem;

namespace OpenZ
{
    /// <summary>
    /// K327: the repair key as the build menu's SET RALLY shortcut. PlayerInput asks Take() right before it reads the repair key; the
    /// rule is OzRallyKeyCore (View/OzRallyKeyCore.cs). SetRally = OpenZProductionWindow.RallyKeyClick (the SET RALLY button's own
    /// click with the HUD's click sound, UI/OpenZProductionWindow.RallyKey.cs); Cancel = the armed tool is dropped as with Esc
    /// (ui_cancel); Swallow = nothing. The key is the repair key (OzAction.RepairVehicles): rebound in CONTROLS, the shortcut moves with it.
    /// View / input side only: the simulation never reads a key.
    /// </summary>
    public static class OzRallyKey
    {
        /// <summary>Checks: the last route and the frame it was taken in.</summary>
        public static OzRallyKeyCore.Route Last { get; private set; } = OzRallyKeyCore.Route.None;
        public static int LastFrame { get; private set; } = -1;

        /// <summary>PlayerInput, before its own reading of the repair key (no Ctrl, no Alt). True = this press belongs to the build menu
        /// or its rally tool: PlayerInput sends no repair order and cycles no robot squad for it.</summary>
        public static bool Take(PlayerInput input)
        {
            if (!OzBindings.Pressed(OzAction.RepairVehicles)) return false;                                  // most frames: nothing more is read
            var hud = OpenZHud.I;
            var pw = hud ? hud.Production : null; var rally = hud ? hud.Rally : null;
            var kb = Keyboard.current; var m = Mouse.current;
            var f = new OzRallyKeyCore.Frame
            {
                keyDown = true,
                tapped = OzTouch.SentByTouch(OzBindings.KeyOf(OzAction.RepairVehicles)),
                gameOver = input == null || input.game == null || input.game.World == null || input.game.World.GameOver,
                menuOpen = pw && pw.IsOpen, menuWasOpen = pw && pw.RallyKeyWasOpen,
                armed = rally && rally.Armed, armedWas = pw && pw.RallyKeyWasArmed,
                esc = kb != null && kb.escapeKey.wasPressedThisFrame,
                leftPress = m != null && m.leftButton.wasPressedThisFrame,
            };
            var r = OzRallyKeyCore.Decide(f);
            OzRallyKeyCore.Count(r); Last = r; LastFrame = Time.frameCount;
            switch (r)
            {
                case OzRallyKeyCore.Route.SetRally:
                    bool clicked = pw.RallyKeyClick();
                    Debug.Log($"[OpenZ] {OzBindings.Cap(OzAction.RepairVehicles)} with the build menu open: {(clicked ? "SET RALLY clicked" : "the SET RALLY button is not there, nothing done")} (rally tool {(rally && rally.Armed ? "armed" : "not armed")}, menu {(pw.IsOpen ? "open" : "closed")}; no repair order, no robot squad cycled)");
                    break;
                case OzRallyKeyCore.Route.Cancel:
                    if (rally) rally.Disarm();
                    AudioDirector.UI("ui_cancel");
                    Debug.Log($"[OpenZ] {OzBindings.Cap(OzAction.RepairVehicles)} again: the rally tool is cancelled (no repair order)");
                    break;
                case OzRallyKeyCore.Route.Swallow:
                    Debug.Log($"[OpenZ] {OzBindings.Cap(OzAction.RepairVehicles)} in the frame the build menu closed or opened (esc {f.esc}, left press {f.leftPress}, menu {f.menuWasOpen} -> {f.menuOpen}, tool {f.armedWas} -> {f.armed}): that action wins, the key adds nothing");
                    break;
            }
            return OzRallyKeyCore.Taken(r);
        }
    }
}
