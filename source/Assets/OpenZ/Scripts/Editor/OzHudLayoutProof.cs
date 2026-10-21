using System.Text;
using UnityEditor;
using UnityEngine;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// Counted check of the HUD layout numbers (no scene, no Play mode): menu OpenZ/QC/HUD Layout Proof (numbers).
    ///  1. Every movable group at 9 places (the 4 corners, the 4 edge middles, the centre) on 5 screens x 3 UI scales lies fully
    ///     inside the canvas: 5 x 3 x 8 x 9 = 1080 rectangles, 1080 inside. (The eighth group is the transmission window: its
    ///     frame is the window's size divided by the UI scale, so the same check covers the window on its own canvas.)
    ///  2. The always-on panels at their DEFAULT places (status strip + territory tab, minimap, unit panel, command bar, MENU
    ///     button) do not overlap each other on the six test canvases (1920x1080, 2560x1600, 3840x2160, each at 100 % and 160 %):
    ///     6 x 10 pairs = 60 pairs, 0 overlaps.
    ///  3. For information (not part of the verdict): default places of the layout as it shipped where panels meet on the other
    ///     canvases (5:4 at 160 %), or where a plate that only shows now and then (messages, cannon plates, the unit panel's tabs,
    ///     the transmission window) reaches over an always-on panel.
    /// The live check on the real panels is OpenZHud.ArrangeProof() in Play mode.
    /// </summary>
    public static class OzHudLayoutProof
    {
        static readonly (int w, int h)[] Screens = { (1920, 1080), (2560, 1600), (3840, 2160), (2560, 1080), (1280, 1024) };
        static readonly float[] Scales = { 1f, 1.6f, 0.9f };
        /// <summary>The first TestScreens screens at the first TestScales scales are the six test canvases of the verdict.</summary>
        const int TestScreens = 3, TestScales = 2;

        [MenuItem("OpenZ/QC/HUD Layout Proof (numbers)")]
        public static void Run() { Debug.Log(Report()); }

        static Rect Proper(OzRect r, float cw, float ch) { var tl = OzHudLayout.TopLeft(r, cw, ch); return new Rect(tl.x, tl.y, r.w, r.h); }

        public static string Report()
        {
            int n = 0, ok = 0, pairs = 0, clash = 0, soft = 0; var bad = new StringBuilder(); var info = new StringBuilder();
            string[] alwaysName = { "status", "minimap", "unit panel", "command bar", "menu button" };
            string[] sometimesName = { "messages", "cannon plates", "unit panel tabs", "transmission window" };
            for (int si = 0; si < Screens.Length; si++)
                for (int ki = 0; ki < Scales.Length; ki++)
                {
                    int w = Screens[si].w, h = Screens[si].h; float s = Scales[ki];
                    float ch = OzLayout.RefH / s, cw = w * ch / h;
                    bool test = si < TestScreens && ki < TestScales;
                    for (int i = 0; i < OzHudLayout.Count; i++)
                        for (int ix = 0; ix <= 2; ix++)
                            for (int iy = 0; iy <= 2; iy++)
                            {
                                var r = OzHudLayout.BoxAt((OzPanel)i, new Vector2(ix * 0.5f, iy * 0.5f), cw, ch);
                                n++;
                                if (r.xMin >= -0.01f && r.yMin >= -0.01f && r.xMax <= cw + 0.01f && r.yMax <= ch + 0.01f) ok++;
                                else if (bad.Length < 800) bad.Append($" | {(OzPanel)i} {w}x{h} x{s:0.0} at {ix * 0.5f:0.0};{iy * 0.5f:0.0}: {r.xMin:0},{r.yMin:0} .. {r.xMax:0},{r.yMax:0} on {cw:0}x{ch:0}");
                            }
                    Rect[] always =
                    {
                        OzHudLayout.BoxDefault(OzPanel.Status, cw, ch), OzHudLayout.BoxDefault(OzPanel.Minimap, cw, ch), Proper(OzLayout.UnitPanelFor(cw), cw, ch),
                        OzHudLayout.BoxDefault(OzPanel.Command, cw, ch), OzHudLayout.BoxDefault(OzPanel.Menu, cw, ch),
                    };
                    for (int a = 0; a < always.Length; a++)
                        for (int b = a + 1; b < always.Length; b++)
                        {
                            bool hit = always[a].Overlaps(always[b]);
                            if (test) { pairs++; if (hit) { clash++; if (bad.Length < 800) bad.Append($" | default overlap {alwaysName[a]} / {alwaysName[b]} at {w}x{h} x{s:0.0}"); } }
                            else if (hit) { soft++; if (info.Length < 1200) info.Append($" | {alwaysName[a]} / {alwaysName[b]} at {w}x{h} x{s:0.0}"); }
                        }
                    Rect[] sometimes = { OzHudLayout.BoxDefault(OzPanel.Messages, cw, ch), OzHudLayout.BoxDefault(OzPanel.Cannons, cw, ch), OzHudLayout.BoxDefault(OzPanel.Unit, cw, ch), OzHudLayout.BoxDefault(OzPanel.Comms, cw, ch) };
                    for (int a = 0; a < sometimes.Length; a++)
                        for (int b = 0; b < always.Length; b++)
                        {
                            if (a == 2 && b == 2) continue;                                                // the tabs belong to the unit panel
                            if (sometimes[a].Overlaps(always[b])) { soft++; if (info.Length < 1200) info.Append($" | {sometimesName[a]} over {alwaysName[b]} at {w}x{h} x{s:0.0}"); }
                        }
                }
            bool pass = ok == n && clash == 0;
            return $"[OpenZ HUD layout proof] {ok}/{n} group rectangles inside the canvas ({Screens.Length} screens x {Scales.Length} UI scales x {OzHudLayout.Count} groups x 9 places); "
                   + $"default places of the always-on panels on the six test canvases: {clash} overlaps in {pairs} pairs. {(pass ? "PASS" : "FAIL")}{bad}\n"
                   + $"for information, the layout as shipped (not part of the verdict): {soft} default places where panels meet{info}";
        }
    }
}
