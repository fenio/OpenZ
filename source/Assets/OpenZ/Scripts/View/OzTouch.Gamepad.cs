using UnityEngine;
using UnityEngine.InputSystem;

namespace OpenZ
{
    /// <summary>
    /// The gamepad part of the pointer layer (OzTouch.cs): any pad the Input System knows as a standard gamepad (Xbox and
    /// PlayStation layouts alike; the buttons are read by their place, south / east / west / north). It moves the same virtual
    /// pointer the touch controls use, so every screen of the game works with it, and presses the game's own keys for the rest.
    /// It starts on a stick, a trigger or a button; the real mouse or a finger takes the pointer back.
    ///
    ///  left stick      the pointer: dead zone, squared response, faster after a moment at full tilt. Held against a screen edge
    ///                  the map scrolls as with the mouse (when EDGE SCROLL is on).
    ///  A / Cross       left button: select, press, hold and move for the selection box. After a D-pad step in the main menu, the
    ///                  level selection or the end screen it is ENTER (the entry in focus).
    ///  X / Square      right button: the order (also: cancel a placement, a rally point).
    ///  B / Circle      back: closes the help card, the build menu, the rally tool, a placement, a menu (ESC); with nothing open
    ///                  in a match it clears the selection.
    ///  Y / Triangle    damaged vehicles of the selection to repair, else the next robot squad (the keyboard's R).
    ///  right stick     scrolls the map (RtsCamera.DragPixels).
    ///  LT / RT         zoom out / in (RtsCamera.ZoomBy).
    ///  LB / RB         held: CTRL / SHIFT (force attack, store a group; queue waypoints, add to the selection).
    ///  D-pad           in a match: groups 1 (up) 2 (right) 3 (down) 4 (left), with LB held the selection is stored; in the menus
    ///                  the arrow keys.
    ///  Start           ESC (the pause menu). Select / View: the MINIMAL UI key.
    ///  L3 / R3         exit crew / go to the last message.
    /// Remove this file and the layer is touch only (the calls into it are partial methods).
    /// </summary>
    public sealed partial class OzTouch
    {
        /// <summary>Pointer speed at full tilt, scroll speed of the map at full tilt (pixels a second on a 1080 px tall screen), zoom
        /// rate of a fully pressed trigger (e-folds a second), the sticks' dead zone.</summary>
        public static float PadPointerSpeed = 1400f, PadScrollSpeed = 800f, PadZoomRate = 1.4f, PadDead = 0.2f;
        Vector2 padPos; float padFullAt = -1f; bool padNav, padEnter, padHot = true;

        static partial void PadProbe(ref bool yes) { yes = true; }
        partial void PadSet(Vector2 p) { padPos = p; }

        partial void PadActivity(ref bool active)
        {
            var g = Gamepad.current;
            if (g == null) return;
            // a stick or a trigger takes the pointer when it LEAVES its rest (a stick that never rests, a worn pad, cannot keep taking it from the mouse)
            bool hot = g.leftStick.ReadValue().sqrMagnitude > 0.25f || g.rightStick.ReadValue().sqrMagnitude > 0.25f
                       || g.leftTrigger.ReadValue() > 0.35f || g.rightTrigger.ReadValue() > 0.35f;
            bool rise = hot && !padHot; padHot = hot;
            if (driver == Driver.Pad ? hot : rise) { active = true; return; }
            var d = g.dpad;
            active = g.buttonSouth.wasPressedThisFrame || g.buttonEast.wasPressedThisFrame || g.buttonWest.wasPressedThisFrame || g.buttonNorth.wasPressedThisFrame
                     || g.startButton.wasPressedThisFrame || g.selectButton.wasPressedThisFrame || g.leftShoulder.wasPressedThisFrame || g.rightShoulder.wasPressedThisFrame
                     || g.leftStickButton.wasPressedThisFrame || g.rightStickButton.wasPressedThisFrame
                     || d.up.wasPressedThisFrame || d.down.wasPressedThisFrame || d.left.wasPressedThisFrame || d.right.wasPressedThisFrame;
        }

        /// <summary>The gamepad takes the pointer where it stands (the middle of the screen when it was parked).</summary>
        partial void PadEnter()
        {
            var p = sent.pos;
            bool inside = p.x >= 0f && p.y >= 0f && p.x < Screen.width && p.y < Screen.height;
            padPos = inside ? p : new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            padNav = padEnter = false; padFullAt = -1f;
        }

        partial void TickPad(float now)
        {
            heldKeys.Clear();
            var g = Gamepad.current;
            if (g == null) { want = new Out { pos = padPos }; return; }
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f), k = Screen.height / 1080f;
            var hud = OpenZHud.I;
            bool level = hud && hud.isActiveAndEnabled && hud.W != null;
            bool match = level && !hud.Modal && !OzConfirm.Blocks;
            bool keyMenu = !level || (hud.EndScreen && hud.EndScreen.IsShown);                       // screens the arrow keys and ENTER can drive

            // ---- the pointer
            Vector2 ls = g.leftStick.ReadValue(); float lm = ls.magnitude;
            if (lm > PadDead)
            {
                float t = Mathf.Clamp01((lm - PadDead) / (1f - PadDead));
                if (t > 0.95f) { if (padFullAt < 0f) padFullAt = now; } else padFullAt = -1f;
                float boost = padFullAt >= 0f ? 1f + Mathf.Clamp01((now - padFullAt - 0.3f) / 0.5f) * 0.8f : 1f;
                padPos += ls / lm * (t * t * PadPointerSpeed * boost * k * dt);
                padNav = false;
            }
            else padFullAt = -1f;
            padPos.x = Mathf.Clamp(padPos.x, 0f, Mathf.Max(0f, Screen.width - 1f));
            padPos.y = Mathf.Clamp(padPos.y, 0f, Mathf.Max(0f, Screen.height - 1f));

            // ---- A: the left button, or ENTER on the entry the D-pad put in focus
            bool a = g.buttonSouth.isPressed;
            if (g.buttonSouth.wasPressedThisFrame) { padEnter = padNav && keyMenu; if (padEnter) TapKey(Key.Enter); }
            if (!a) padEnter = false;
            want = new Out { pos = padPos, buttons = (a && !padEnter ? 1 : 0) | (g.buttonWest.isPressed ? 2 : 0) };

            // ---- held: CTRL, SHIFT
            if (g.leftShoulder.isPressed) heldKeys.Add(Key.LeftCtrl);
            if (g.rightShoulder.isPressed) heldKeys.Add(Key.LeftShift);

            // ---- B: back
            if (g.buttonEast.wasPressedThisFrame)
            {
                bool open = level && ((hud.Production && hud.Production.IsOpen) || (hud.Rally && hud.Rally.Armed) || (hud.input && hud.input.Placing != null));
                if (level && hud.TouchHelpClose()) { }
                else if (match && !open) { if (hud.input && hud.input.Selection.Count > 0) { hud.input.Selection.Clear(); OzSfx.Ui("ui_deselect"); } }
                else TapKey(Key.Escape);
            }
            if (g.startButton.wasPressedThisFrame) TapKey(Key.Escape);

            // ---- D-pad
            var d = g.dpad;
            if (match)
            {
                if (d.up.wasPressedThisFrame) TapKey(Key.Digit1);
                if (d.right.wasPressedThisFrame) TapKey(Key.Digit2);
                if (d.down.wasPressedThisFrame) TapKey(Key.Digit3);
                if (d.left.wasPressedThisFrame) TapKey(Key.Digit4);
            }
            else
            {
                bool step = false;
                if (d.up.wasPressedThisFrame) { TapKey(Key.UpArrow); step = true; }
                if (d.down.wasPressedThisFrame) { TapKey(Key.DownArrow); step = true; }
                if (d.left.wasPressedThisFrame) { TapKey(Key.LeftArrow); step = true; }
                if (d.right.wasPressedThisFrame) { TapKey(Key.RightArrow); step = true; }
                if (step && keyMenu) padNav = true;
            }

            if (!match) return;
            // ---- the match's own keys
            if (g.selectButton.wasPressedThisFrame) TapKey(OzBindings.KeyOf(OzAction.MinimalUi));
            if (g.buttonNorth.wasPressedThisFrame && hud.input && !hud.input.SendToRepair()) TapKey(OzBindings.KeyOf(OzAction.NextRobot));
            if (g.leftStickButton.wasPressedThisFrame) TapKey(OzBindings.KeyOf(OzAction.ExitCrew));
            if (g.rightStickButton.wasPressedThisFrame) TapKey(OzBindings.KeyOf(OzAction.LastMessage));

            // ---- the view
            var cam = hud.rts;
            if (!cam || !cam.isActiveAndEnabled) return;
            Vector2 rs = g.rightStick.ReadValue(); float rm = rs.magnitude;
            if (rm > PadDead)
            {
                float t = Mathf.Clamp01((rm - PadDead) / (1f - PadDead));
                cam.DragPixels(-(rs / rm) * (t * PadScrollSpeed * k * dt));                         // the stick pushes the view, so the ground goes the other way
            }
            float z = g.rightTrigger.ReadValue() - g.leftTrigger.ReadValue();
            if (Mathf.Abs(z) > 0.1f) cam.ZoomBy(Mathf.Exp(-z * PadZoomRate * dt));
        }
    }
}
