using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;

namespace OpenZ
{
    /// <summary>
    /// has one key. The mouse buttons, Shift / Ctrl / Alt as modifiers, the group keys 1 .. 0, the arrow keys (they always
    /// scroll) and Esc stay fixed.</summary>
    public enum OzAction
    {
        NextRobot, NextVehicle, NextGun, AllRobots, AllVehicles, AllCannons, AllUnits, NearestOnly,
        ScrollUp, ScrollDown, ScrollLeft, ScrollRight, FactoryList, PlaceCannon, ExitCrew, LastMessage, HideHud,
        RepairVehicles,
        Patrol, StopUnits, HoldPosition, HelpOverlay,
    }

    /// <summary>
    /// menu_exe_v1: the key bindings, read by the live input code (PlayerInput, RtsCamera, OpenZHud, OpenZMinimap) instead of
    /// fixed keys. Saved per action in PlayerPrefs "oz_key_&lt;Action&gt;" (the Input System Key number; missing = the default),
    /// saved at once on every change. "Ctrl" actions (ALL ROBOTS ...) keep Ctrl as their modifier: only the key changes.
    /// A conflict = two actions of the same context (plain or Ctrl) on one key: allowed (both fire), shown in red in CONTROLS.
    /// HIDE THE HUD and GO TO THE LAST MESSAGE are read without a modifier test (OpenZHud F10 / Space), so they conflict with a
    /// Ctrl action on the same key too. PLACE A STORED CANNON (C) is not read while Ctrl is held: Ctrl+C is ALL CANNONS. F6-F9, F11, F12 are
    /// reserved: LegacyMatchCheck (F6-F8, every level), NightLights (F9), CaptureKit (F7-F12) read them in the exe as well.
    /// View / input side only: the sim never reads a key.
    /// </summary>
    public static class OzBindings
    {
        public sealed class Def
        {
            public OzAction action; public string label, section, pref; public Key key; public bool ctrl, hold;
        }

        public const string SelectSection = "SELECT", CameraSection = "CAMERA", OrderSection = "ORDERS", UiSection = "INTERFACE";

        /// <summary>In OzAction order (checked in the static constructor).</summary>
        public static readonly Def[] Defs =
        {
            D(OzAction.NextRobot, "Next robot squad", SelectSection, Key.R),
            D(OzAction.NextVehicle, "Next vehicle", SelectSection, Key.V),
            D(OzAction.NextGun, "Next gun", SelectSection, Key.G),
            D(OzAction.AllRobots, "All robots", SelectSection, Key.R, true),
            D(OzAction.AllVehicles, "All vehicles", SelectSection, Key.V, true),
            D(OzAction.AllCannons, "All cannons", SelectSection, Key.C, true),
            D(OzAction.AllUnits, "All robots + vehicles", SelectSection, Key.A, true),
            D(OzAction.NearestOnly, "Only the unit nearest to the point", OrderSection, Key.Z, false, true),
            D(OzAction.ScrollUp, "Scroll up", CameraSection, Key.W, false, true),
            D(OzAction.ScrollDown, "Scroll down", CameraSection, Key.S, false, true),
            D(OzAction.ScrollLeft, "Scroll left", CameraSection, Key.A, false, true),
            D(OzAction.ScrollRight, "Scroll right", CameraSection, Key.D, false, true),
            D(OzAction.FactoryList, "Factory list", UiSection, Key.B),
            D(OzAction.PlaceCannon, "Place a stored cannon", UiSection, Key.C),
            D(OzAction.ExitCrew, "Exit crew (gun / APC)", UiSection, Key.E),
            D(OzAction.LastMessage, "Go to the last 5 messages", UiSection, Key.Space),
            D(OzAction.HideHud, "Hide the HUD", UiSection, Key.F10),
            D(OzAction.RepairVehicles, "Send damaged vehicles to repair and back", OrderSection, Key.R),   // shares R with NEXT ROBOT: repair wins while a damaged vehicle is selected
            D(OzAction.Patrol, "Patrol (then right-click the far end)", OrderSection, Key.Q),
            D(OzAction.StopUnits, "Stop", OrderSection, Key.X),
            D(OzAction.HoldPosition, "Hold position (fire, never chase)", OrderSection, Key.H),
            D(OzAction.HelpOverlay, "Controls help (this list)", UiSection, Key.F1),
        };

        static Def D(OzAction a, string label, string section, Key k, bool ctrl = false, bool hold = false)
            => new Def { action = a, label = label, section = section, key = k, ctrl = ctrl, hold = hold, pref = "oz_key_" + a };

        static readonly Key[] cur = new Key[Defs.Length];
        static bool loaded;
        /// <summary>Bumped on every change (views that show a key refresh when it moves).</summary>
        public static int Version { get; private set; }

        static OzBindings()
        {
            for (int i = 0; i < Defs.Length; i++)
                if ((int)Defs[i].action != i) Debug.LogError("[OzBindings] Defs out of OzAction order at " + i);
        }

        static void Load()
        {
            if (loaded) return;
            loaded = true;
            for (int i = 0; i < Defs.Length; i++)
            {
                int v = PlayerPrefs.GetInt(Defs[i].pref, 0);
                cur[i] = v > 0 && Reserved((Key)v) == null ? (Key)v : Defs[i].key;                            // review fix: a saved debug / reserved key falls back
            }
            // a key map saved before the cannon key became C may hold a plain action the player moved onto C: for exactly that player
            // the cannon key stays on P (saved once, so it shows in CONTROLS and RESET TO DEFAULTS still gives C)
            int pc = (int)OzAction.PlaceCannon;
            if (!PlayerPrefs.HasKey(Defs[pc].pref))
                for (int i = 0; i < Defs.Length; i++)
                    if (i != pc && !Defs[i].ctrl && cur[i] == Key.C && cur[i] != Defs[i].key)
                    { cur[pc] = Key.P; PlayerPrefs.SetInt(Defs[pc].pref, (int)Key.P); PlayerPrefs.Save(); break; }
        }

        /// <summary>Read the saved keys again (after PlayerPrefs were changed from outside, e.g. a proof restoring them).</summary>
        public static void Reload() { loaded = false; Version++; }

        public static Def DefOf(OzAction a) => Defs[(int)a];
        public static Key KeyOf(OzAction a) { Load(); return cur[(int)a]; }
        public static bool IsDefault(OzAction a) => KeyOf(a) == Defs[(int)a].key;
        public static int ChangedCount { get { Load(); int n = 0; for (int i = 0; i < Defs.Length; i++) if (cur[i] != Defs[i].key) n++; return n; } }

        /// <summary>Bind a key (saved at once). False = a reserved key (why says which).</summary>
        public static bool Set(OzAction a, Key k, out string why)
        {
            Load();
            why = Reserved(k);
            if (why != null) return false;
            cur[(int)a] = k;
            if (k == Defs[(int)a].key) PlayerPrefs.DeleteKey(Defs[(int)a].pref); else PlayerPrefs.SetInt(Defs[(int)a].pref, (int)k);
            PlayerPrefs.Save();
            Version++;
            Debug.Log($"[OzBindings] {a} = {Cap(k)}{(Defs[(int)a].ctrl ? " (with CTRL)" : "")}{(k == Defs[(int)a].key ? " (default)" : "")}, saved (oz_key_{a}); conflicts: {ConflictText()}");
            return true;
        }

        public static void ResetAll()
        {
            Load();
            for (int i = 0; i < Defs.Length; i++) { cur[i] = Defs[i].key; PlayerPrefs.DeleteKey(Defs[i].pref); }
            PlayerPrefs.Save();
            Version++;
            Debug.Log("[OzBindings] every key back to the default (Zod key map + OpenZ camera), saved");
        }

        // ------------------------------------------------------------------ reads for the input code
        /// <summary>The bound key went down this frame (no modifier test: the callers keep their Ctrl / Alt rules).</summary>
        public static bool Pressed(OzAction a) { var c = Control(a); return c != null && c.wasPressedThisFrame; }
        /// <summary>The bound key is held.</summary>
        public static bool Held(OzAction a) { var c = Control(a); return c != null && c.isPressed; }

        static UnityEngine.InputSystem.Controls.KeyControl Control(OzAction a)
        {
            var kb = Keyboard.current; if (kb == null) return null;
            try { return kb[KeyOf(a)]; } catch (ArgumentOutOfRangeException) { return null; }
        }

        // ------------------------------------------------------------------ rules
        /// <summary>null = the key can be bound, else why not (shown in CONTROLS).</summary>
        public static string Reserved(Key k)
        {
            if (k == Key.None || !Usable(k)) return "NOT A KEY";
            switch (k)
            {
                case Key.Escape: return "ESC IS THE MENU KEY";
                case Key.LeftShift: case Key.RightShift: case Key.LeftCtrl: case Key.RightCtrl: case Key.LeftAlt: case Key.RightAlt:
                case Key.LeftMeta: case Key.RightMeta: case Key.ContextMenu: return "SHIFT / CTRL / ALT ARE MODIFIERS";
                case Key.UpArrow: case Key.DownArrow: case Key.LeftArrow: case Key.RightArrow: return "THE ARROW KEYS ALWAYS SCROLL";
                case Key.PrintScreen: return "PRINT SCREEN IS THE SYSTEM'S";
                case Key.Minus: case Key.Equals: case Key.NumpadPlus: case Key.NumpadMinus: return "+ AND - CHANGE THE GAME SPEED";   // OzSpeedKeys
                case Key.F6: case Key.F7: case Key.F8: case Key.F9: case Key.F11: case Key.F12: return "F6-F9, F11, F12 ARE DEBUG / CAPTURE KEYS";   // review fix: LegacyMatchCheck F6-F8, NightLights F9, CaptureKit F7-F12
            }
            if (k >= Key.Digit1 && k <= Key.Digit0) return "1 .. 0 ARE THE GROUP KEYS";
            return null;
        }

        static bool Usable(Key k)
        {
            if (!Enum.IsDefined(typeof(Key), k) || k == Key.None) return false;
            if (k.ToString().StartsWith("IME", StringComparison.Ordinal)) return false;
            var kb = Keyboard.current;
            if (kb != null) { try { var _ = kb[k]; } catch (ArgumentOutOfRangeException) { return false; } }
            return true;
        }

        /// <summary>Actions whose live handler never tests Ctrl (OpenZHud: HIDE THE HUD, LAST MESSAGE):
        /// they fire on their key with or without Ctrl.</summary>
        /// <summary>
        /// repair order while the selection holds a damaged own vehicle, else to NEXT ROBOT, so it is not a conflict.</summary>
        public static bool SharedKey(OzAction a, OzAction b) => (a == OzAction.RepairVehicles && b == OzAction.NextRobot) || (a == OzAction.NextRobot && b == OzAction.RepairVehicles);

        public static bool ModifierBlind(OzAction a) => a == OzAction.HideHud || a == OzAction.LastMessage;

        /// <summary>The other action on the same key in the same context (plain / Ctrl), or on the same key at all when one of the
        /// two is modifier-blind (a Ctrl action on such a key would fire both with no red cap), or null.</summary>
        public static Def ConflictOf(OzAction a)
        {
            Load();
            var d = Defs[(int)a]; Key k = cur[(int)a];
            for (int i = 0; i < Defs.Length; i++)
                if (i != (int)a && cur[i] == k && (Defs[i].ctrl == d.ctrl || ModifierBlind(a) || ModifierBlind(Defs[i].action)) && !SharedKey(a, Defs[i].action)) return Defs[i];
            return null;
        }

        public static string ConflictText()
        {
            Load();
            var sb = new StringBuilder(); var seen = new HashSet<int>();
            for (int i = 0; i < Defs.Length; i++)
            {
                var o = ConflictOf((OzAction)i); if (o == null) continue;
                int j = (int)o.action; if (seen.Contains(i) || seen.Contains(j)) continue;
                seen.Add(i); seen.Add(j);
                if (sb.Length > 0) sb.Append(" · ");
                sb.Append(Defs[i].ctrl ? "CTRL+" : "").Append(Cap(cur[i])).Append(": ").Append(Defs[i].label.ToUpperInvariant()).Append(" + ").Append(o.label.ToUpperInvariant());
            }
            return sb.Length == 0 ? "none" : sb.ToString();
        }

        // ------------------------------------------------------------------ display
        /// <summary>Key cap text (OzKeyNames): the label of the player's layout when it is a Latin character, else the key by its place; never another script.</summary>
        public static string Cap(Key k)
        {
            switch (k)
            {
                case Key.Space: return "SPACE";
                case Key.Enter: return "ENTER";
                case Key.NumpadEnter: return "NUM ENTER";
                case Key.Tab: return "TAB";
                case Key.Backspace: return "BACKSPACE";
                case Key.Delete: return "DEL";
                case Key.Insert: return "INS";
                case Key.Home: return "HOME";
                case Key.End: return "END";
                case Key.PageUp: return "PG UP";
                case Key.PageDown: return "PG DN";
                case Key.CapsLock: return "CAPS";
                case Key.Escape: return "ESC";
                case Key.None: return "-";
            }
            if (k >= Key.F1 && k <= Key.F12) return "F" + (1 + (int)k - (int)Key.F1);
            if (k >= Key.Numpad0 && k <= Key.Numpad9) return "NUM " + ((int)k - (int)Key.Numpad0);
            return OzKeyNames.Name(k);                                                      // Latin capitals on any keyboard layout, every special key with a whole name
        }

        public static string Cap(OzAction a) => Cap(KeyOf(a));

        /// <summary>One line for the logs: "R V G · CTRL+R V C A · Z · W S A D · B C E SPACE F10 (0 changed)".</summary>
        public static string Summary()
        {
            Load();
            var sb = new StringBuilder();
            for (int i = 0; i < Defs.Length; i++) { if (i > 0) sb.Append(' '); sb.Append(Defs[i].action).Append('=').Append(Defs[i].ctrl ? "CTRL+" : "").Append(Cap(cur[i])); }
            return $"{sb} ({ChangedCount} changed, conflicts {ConflictText()})";
        }
    }
}
