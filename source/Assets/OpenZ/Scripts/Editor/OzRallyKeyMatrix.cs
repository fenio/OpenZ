using System.Text;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// K327 counted conflict matrix of the repair key's rule (OzRallyKeyCore), no Play mode needed (plain C#: the same file is run
    /// outside the editor by the kit's own check). Part A: every one of the 512 frames (9 switches) against the rule written out
    /// on its own; part B: the named cases of item 328, one by one. Menu: OpenZ/V1.1/Controls: K327 R Key Matrix (no Play).
    /// One line at the end: "[K327 R key] MATRIX: N checks, M violations" (pass = 0).
    /// </summary>
    public static class OzRallyKeyMatrix
    {
        public const string Tag = "[K327 R key] ";
        static int n, bad; static StringBuilder sb;

        static void Check(bool ok, string what)
        {
            n++;
            if (!ok) { bad++; sb.AppendLine("  FAIL " + what); }
        }

        static OzRallyKeyCore.Frame F(int bits) => new OzRallyKeyCore.Frame
        {
            keyDown = (bits & 1) != 0, tapped = (bits & 2) != 0, gameOver = (bits & 4) != 0, menuOpen = (bits & 8) != 0, menuWasOpen = (bits & 16) != 0,
            armed = (bits & 32) != 0, armedWas = (bits & 64) != 0, esc = (bits & 128) != 0, leftPress = (bits & 256) != 0,
        };

        static string Bits(OzRallyKeyCore.Frame f) =>
            $"key {f.keyDown} tapped {f.tapped} over {f.gameOver} menu {f.menuWasOpen}->{f.menuOpen} tool {f.armedWas}->{f.armed} esc {f.esc} left {f.leftPress}";

        /// <summary>Runs both parts; returns the report (its last line is the result line).</summary>
        public static string Run(out int checks, out int violations)
        {
            n = 0; bad = 0; sb = new StringBuilder();
            var R = OzRallyKeyCore.Route.None;
            int[] perRoute = new int[5];
            // ---- A: all 512 frames against the rule as item 328 asks for it
            for (int bits = 0; bits < 512; bits++)
            {
                var f = F(bits); var r = OzRallyKeyCore.Decide(f); perRoute[(int)r]++;
                bool owned = f.menuOpen || f.menuWasOpen || f.armed || f.armedWas;
                bool stable = f.menuOpen == f.menuWasOpen && f.armed == f.armedWas;
                OzRallyKeyCore.Route want;
                if (!f.keyDown) want = OzRallyKeyCore.Route.None;                                   // no press: nothing
                else if (f.tapped || f.gameOver) want = OzRallyKeyCore.Route.Today;                 // touch / gamepad keys, after the match: as before
                else if (!owned) want = OzRallyKeyCore.Route.Today;                                 // no menu, no tool: repair / next robot as before
                else if (f.esc || f.leftPress || !stable) want = OzRallyKeyCore.Route.Swallow;      // the frame's own closing action wins
                else if (f.menuOpen) want = OzRallyKeyCore.Route.SetRally;                          // the menu's button
                else want = OzRallyKeyCore.Route.Cancel;                                            // the armed tool, menu closed
                Check(r == want, $"A{bits}: {Bits(f)} -> {r}, wanted {want}");
                // never a repair order while the menu or its tool owns the key (Today is the only route that lets the repair key through)
                if (f.keyDown && !f.tapped && !f.gameOver && owned) Check(OzRallyKeyCore.Taken(r), $"A{bits}: owned by the menu but {r} lets a repair through");
                // a route is one action: Taken exactly for the menu's routes
                Check(OzRallyKeyCore.Taken(r) == (r == OzRallyKeyCore.Route.SetRally || r == OzRallyKeyCore.Route.Cancel || r == OzRallyKeyCore.Route.Swallow), $"A{bits}: Taken({r})");
            }
            sb.AppendLine($"A: 512 frames, routes: none {perRoute[0]}, as before {perRoute[1]}, SET RALLY {perRoute[2]}, cancel {perRoute[3]}, nothing {perRoute[4]}");

            // ---- B: the named cases of item 328
            void Case(string id, string what, OzRallyKeyCore.Frame f, OzRallyKeyCore.Route want, bool taken)
            {
                R = OzRallyKeyCore.Decide(f);
                Check(R == want && OzRallyKeyCore.Taken(R) == taken, $"{id} {what}: {R}, wanted {want}");
                sb.AppendLine($"  {(R == want && OzRallyKeyCore.Taken(R) == taken ? "PASS" : "FAIL")} {id} {what}: {OzRallyKeyCore.Name(R)}{(taken ? " (no repair order, no robot squad cycled)" : "")}");
            }
            var open = new OzRallyKeyCore.Frame { keyDown = true, menuOpen = true, menuWasOpen = true };
            var none = new OzRallyKeyCore.Frame { keyDown = true };
            var armed = new OzRallyKeyCore.Frame { keyDown = true, armed = true, armedWas = true };
            Case("1", "a build menu open, nothing selected", open, OzRallyKeyCore.Route.SetRally, true);
            Case("2", "a build menu open and units selected (a damaged vehicle among them): the open menu owns the key", open, OzRallyKeyCore.Route.SetRally, true);
            Case("3", "no menu, a damaged vehicle / the shuttle selected: repair as before", none, OzRallyKeyCore.Route.Today, false);
            Case("4", "no menu, nothing repairable selected: next robot squad as before", none, OzRallyKeyCore.Route.Today, false);
            Case("5", "the rally tool armed (menu closed), the key again: cancel, as Esc", armed, OzRallyKeyCore.Route.Cancel, true);
            var a5 = armed; a5.menuOpen = a5.menuWasOpen = true;
            Case("5b", "the rally tool armed while a menu is open: the button's click (it cancels an armed tool)", a5, OzRallyKeyCore.Route.SetRally, true);
            var e6 = open; e6.esc = true;
            Case("6a", "Esc and the key in the same frame, a menu open: Esc closes it, the key adds nothing", e6, OzRallyKeyCore.Route.Swallow, true);
            var l6 = open; l6.leftPress = true;
            Case("6b", "a left press on the map and the key in the same frame: the press closes the menu, the key adds nothing", l6, OzRallyKeyCore.Route.Swallow, true);
            var c6 = new OzRallyKeyCore.Frame { keyDown = true, menuWasOpen = true };
            Case("6c", "the menu closed earlier in this frame (a HUD click, the factory list key, hide HUD): the key adds nothing", c6, OzRallyKeyCore.Route.Swallow, true);
            var o6 = new OzRallyKeyCore.Frame { keyDown = true, menuOpen = true };
            Case("6d", "a menu opened earlier in this frame: the key adds nothing", o6, OzRallyKeyCore.Route.Swallow, true);
            var t6 = armed; t6.esc = true;
            Case("6e", "Esc and the key with the tool armed: Esc cancels it, the key adds nothing", t6, OzRallyKeyCore.Route.Swallow, true);
            var t6b = new OzRallyKeyCore.Frame { keyDown = true, armedWas = true };
            Case("6f", "the tool placed its point / was cancelled earlier in this frame: the key adds nothing", t6b, OzRallyKeyCore.Route.Swallow, true);
            // 7: held down: the press is reported once, the frames after it carry no press
            int actions = 0;
            for (int frame = 0; frame < 120; frame++)
            {
                var h = frame == 0 ? open : new OzRallyKeyCore.Frame { keyDown = false, armed = true, armedWas = true };
                var r = OzRallyKeyCore.Decide(h); if (r != OzRallyKeyCore.Route.None) actions++;
            }
            Check(actions == 1, $"7 held 120 frames: {actions} actions, wanted 1");
            sb.AppendLine($"  {(actions == 1 ? "PASS" : "FAIL")} 7 the key held 120 frames with a menu open: {actions} action (SET RALLY once; the held key sends no new press)");
            sb.AppendLine("  INFO 8 a modal screen (pause menu, its CONTROLS, a confirm box, ARRANGE INTERFACE): PlayerInput is off (OpenZHud gates it), the rule is not asked; the testing panel and the cheat shuttle read no R; a match has no text field");
            var g9 = open; g9.tapped = true;
            Case("9", "the gamepad's Y (it tapped NEXT ROBOT) with a menu open: unchanged, as before", g9, OzRallyKeyCore.Route.Today, false);
            sb.AppendLine("  INFO 9b the touch REPAIR key and the gamepad's Y call the repair order directly (not through the key): unchanged");
            var g10 = open; g10.gameOver = true;
            Case("10", "after the match is over, a menu still open: unchanged, as before", g10, OzRallyKeyCore.Route.Today, false);
            checks = n; violations = bad;
            sb.AppendLine($"{Tag}MATRIX: {n} checks, {bad} violations");
            return sb.ToString();
        }
    }
}
