#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using OpenZ.Sim;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// Menu OpenZ / UI / Close-up captures, in Play
    /// with the Game view at 2560 x 1600 (it captures at any size and says so). It opens what it needs for a few frames (the production
    /// window of one of your producers, the cheat overlay, the software cursor's gallery of every cursor state), takes ONE full-frame
    /// screenshot (ScreenCapture, overlay UI included), crops the cursor row, the first production card (+ the whole window), a
    /// production timer badge, the minimap and the top of the cheat overlay, and saves each crop 4x enlarged with nearest-neighbour
    /// (every screen pixel = a 4 x 4 block, so shimmer, stair steps and blur are visible) plus a 1:1 copy to
    /// texture size, on-screen size, source/screen ratio, filter and mip count (portraits / icons / cursors must be &gt;= 2x, never
    /// Point, mip-mapped when drawn smaller). Nothing in the scene is saved or changed for good.
    /// </summary>
    public static class OzUiCloseups
    {
        enum Step { Idle, Settle, Shot, Wait }
        static Step step = Step.Idle;
        static int waitUntilFrame; static double waitStart;
        static string dir, shotPath;
        static bool openedProd, shownCheat, prevGallery; static string prevForce;
        static readonly List<(string name, Rect r)> crops = new List<(string, Rect)>();

        [MenuItem("OpenZ/UI/Close-up captures (Play, 2560x1600)")]
        static void Start()
        {
            if (!Application.isPlaying) { Debug.LogError("[OzUiCloseups] enter Play (a level with the HUD) first"); return; }
            if (step != Step.Idle) { Debug.LogWarning("[OzUiCloseups] a capture is running"); return; }
            var hud = OpenZHud.I;
            if (!hud || !hud.Root) { Debug.LogError("[OzUiCloseups] no HUD in this scene"); return; }
            var root = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "..", ".."));
            dir = Path.Combine(root, "work", "records", "milestones", "ui_closeups_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"));
            Directory.CreateDirectory(dir);
            shotPath = Path.Combine(dir, "full_" + Screen.width + "x" + Screen.height + ".png");

            openedProd = false;
            if (hud.Production && !hud.Production.IsOpen)
            {
                ZObject pick = null;
                foreach (var o in hud.W.Objects)
                    if (o.IsProducer && o.Owner == hud.Player && !o.Destroyed) { pick = o; if (o.CurUnit != null) break; }
                if (pick != null) { hud.Production.Open(pick); openedProd = true; }
            }
            shownCheat = false;
            if (OzCheatSpawner.I && !OzCheatSpawner.Visible) { OzCheatSpawner.I.Show(true); shownCheat = true; }
            prevGallery = OzSoftCursor.Gallery; prevForce = OzSoftCursor.ForceName;
            OzSoftCursor.GalleryAt = new Vector2(Mathf.Round(Screen.width * 0.36f), Mathf.Round(Screen.height * 0.62f));
            OzSoftCursor.Gallery = true;
            waitUntilFrame = Time.frameCount + 20; waitStart = EditorApplication.timeSinceStartup;
            step = Step.Settle;
            EditorApplication.update += Tick;
            Debug.Log("[OzUiCloseups] preparing (production window " + (openedProd ? "opened" : hud.Production && hud.Production.IsOpen ? "already open" : "n/a")
                      + ", cheat overlay " + (OzCheatSpawner.I ? "on" : "n/a") + ", cursor gallery on), screen " + Screen.width + "x" + Screen.height
                      + (Screen.width == 2560 && Screen.height == 1600 ? "" : "  (NOT 2560x1600: set the Game view to 2560x1600 for the check)"));
        }

        static void Tick()
        {
            try
            {
                if (!Application.isPlaying) { Finish("left Play mode"); return; }
                if (EditorApplication.timeSinceStartup - waitStart > 30) { Finish("timed out"); return; }
                switch (step)
                {
                    case Step.Settle:
                        if (Time.frameCount < waitUntilFrame) return;
                        CollectCrops();
                        ScreenCapture.CaptureScreenshot(shotPath, 1);
                        waitUntilFrame = Time.frameCount + 3; step = Step.Shot; return;
                    case Step.Shot:
                        if (Time.frameCount < waitUntilFrame || !File.Exists(shotPath)) return;
                        step = Step.Wait; waitUntilFrame = Time.frameCount + 3; return;
                    case Step.Wait:
                        if (Time.frameCount < waitUntilFrame) return;
                        SaveCrops();
                        Finish(null); return;
                }
            }
            catch (Exception e) { Finish(e.ToString()); }
        }

        static void Finish(string err)
        {
            EditorApplication.update -= Tick;
            step = Step.Idle;
            OzSoftCursor.Gallery = prevGallery; OzSoftCursor.ForceName = prevForce;
            var hud = OpenZHud.I;
            if (Application.isPlaying)
            {
                if (openedProd && hud && hud.Production) hud.Production.Close();
                if (shownCheat && OzCheatSpawner.I) OzCheatSpawner.I.Show(false);
            }
            if (err != null) Debug.LogError("[OzUiCloseups] " + err);
            else Debug.Log("[OzUiCloseups] saved " + crops.Count + " close-ups + audit.md -> " + dir);
        }

        // ------------------------------------------------------------------ what to crop (screen px, bottom-left origin)
        static Rect ScreenRectOf(RectTransform rt)
        {
            var c = new Vector3[4]; rt.GetWorldCorners(c);                                             // overlay canvases: world = screen px
            float x0 = Mathf.Min(c[0].x, c[2].x), x1 = Mathf.Max(c[0].x, c[2].x), y0 = Mathf.Min(c[0].y, c[2].y), y1 = Mathf.Max(c[0].y, c[2].y);
            return Rect.MinMaxRect(x0, y0, x1, y1);
        }

        static Rect Union(Transform t)
        {
            bool any = false; Rect u = default;
            foreach (var g in t.GetComponentsInChildren<Graphic>(false))
            {
                if (!g.enabled || !g.gameObject.activeInHierarchy) continue;
                var r = ScreenRectOf(g.rectTransform);
                u = any ? Rect.MinMaxRect(Mathf.Min(u.xMin, r.xMin), Mathf.Min(u.yMin, r.yMin), Mathf.Max(u.xMax, r.xMax), Mathf.Max(u.yMax, r.yMax)) : r; any = true;
            }
            return any ? u : default;
        }

        static Transform FindActive(Transform root, Func<Transform, bool> pred)
        {
            if (!root) return null;
            foreach (var t in root.GetComponentsInChildren<Transform>(false)) if (t.gameObject.activeInHierarchy && pred(t)) return t;
            return null;
        }

        static void CollectCrops()
        {
            crops.Clear();
            var hud = OpenZHud.I;
            if (OzSoftCursor.Showing || OzSoftCursor.Gallery)
            {
                int s = Mathf.Max(16, OzSoftCursor.SizePx);
                crops.Add(("cursors", new Rect(OzSoftCursor.GalleryAt.x - 8, OzSoftCursor.GalleryAt.y - 8, OzSoftCursor.Names.Length * (s + 12) + 16, s + 16)));
                if (OzSoftCursor.Showing) { var r = OzSoftCursor.ScreenRect; crops.Add(("cursor_live", new Rect(r.x - 8, r.y - 8, r.width + 16, r.height + 16))); }
            }
            if (hud.Production && hud.Production.IsOpen && hud.Production.WindowRect)
            {
                var tile = FindActive(hud.Production.WindowRect, t => t.name.StartsWith("Tile_", StringComparison.Ordinal));
                if (tile) crops.Add(("production_card", Pad(ScreenRectOf((RectTransform)tile), 6)));
                crops.Add(("production_window", Pad(ScreenRectOf(hud.Production.WindowRect), 4)));
            }
            var ov = GameObject.Find("OpenZ_UnitOverlay");
            var timer = ov ? FindActive(ov.transform, t => t.name == "Timer") : null;
            if (timer) crops.Add(("timer_badge", Pad(Union(timer), 8)));
            if (hud.Minimap && hud.Minimap.Panel) crops.Add(("minimap", Pad(ScreenRectOf(hud.Minimap.Panel), 4)));
            var cheat = FindActive(hud.Root, t => t.name == "CheatRed");
            if (cheat)
            {
                var r = ScreenRectOf((RectTransform)cheat);
                crops.Add(("cheat_overlay_top", Rect.MinMaxRect(r.xMin - 4, r.yMax - Mathf.Min(r.height, 420f * Screen.height / 1600f), r.xMax + 4, r.yMax + 4)));
            }
            var port = FindActive(hud.Root, t => t.name == "Portrait" && t.GetComponentInChildren<RawImage>(false));   // unit panel portrait bay (OpenZHud)
            if (port) crops.Add(("unit_portrait", Pad(ScreenRectOf((RectTransform)port), 6)));
            WriteAudit();
        }

        static Rect Pad(Rect r, float p) => new Rect(r.x - p, r.y - p, r.width + 2 * p, r.height + 2 * p);

        static void SaveCrops()
        {
            var full = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            full.LoadImage(File.ReadAllBytes(shotPath));                                                   // row 0 = bottom, like screen space
            float sx = full.width / (float)Screen.width, sy = full.height / (float)Screen.height;
            foreach (var (name, r0) in crops)
            {
                int x0 = Mathf.Clamp(Mathf.FloorToInt(r0.xMin * sx), 0, full.width - 1), y0 = Mathf.Clamp(Mathf.FloorToInt(r0.yMin * sy), 0, full.height - 1);
                int x1 = Mathf.Clamp(Mathf.CeilToInt(r0.xMax * sx), x0 + 1, full.width), y1 = Mathf.Clamp(Mathf.CeilToInt(r0.yMax * sy), y0 + 1, full.height);
                int w = x1 - x0, h = y1 - y0;
                var px = full.GetPixels32(); var crop = new Color32[w * h];
                for (int y = 0; y < h; y++) Array.Copy(px, (y0 + y) * full.width + x0, crop, y * w, w);
                Save(Path.Combine(dir, name + "_1x.png"), crop, w, h, 1);
                int k = w * 4 > 4096 ? Mathf.Max(1, 4096 / w) : 4;
                Save(Path.Combine(dir, name + "_" + k + "x.png"), crop, w, h, k);
            }
            UnityEngine.Object.DestroyImmediate(full);
        }

        static void Save(string path, Color32[] src, int w, int h, int k)
        {
            var t = new Texture2D(w * k, h * k, TextureFormat.RGBA32, false);
            var o = new Color32[w * k * h * k];
            for (int y = 0; y < h * k; y++) { int sy = y / k; for (int x = 0; x < w * k; x++) { var c = src[sy * w + x / k]; c.a = 255; o[y * w * k + x] = c; } }
            t.SetPixels32(o); t.Apply(false);
            File.WriteAllBytes(path, t.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(t);
        }

        // ------------------------------------------------------------------ audit: texture vs on-screen size of every visible UI image
        static void WriteAudit()
        {
            var sb = new StringBuilder();
            sb.AppendLine("# OpenZ UI image audit (" + Screen.width + "x" + Screen.height + ", UI scale " + OzText.UiScale.ToString("0.00") + ", " + DateTime.Now.ToString("HH:mm") + ")");
            sb.AppendLine();
            sb.AppendLine("ratio = source px / on-screen px (min of both axes). Needs: portraits, unit icons, glyphs and cursors >= 2.0; kit chrome >= 1.0 (drawn 1:1 at 1600p / 1.35);");
            sb.AppendLine("Point filter never; no mips + ratio > 1.5 = shimmer. Sliced / tiled images are listed as chrome (their ratio is per slice).");
            sb.AppendLine();
            sb.AppendLine("| Status | Object | Texture | Tex px | Screen px | Ratio | Filter | Mips |"); sb.AppendLine("|---|---|---|---|---|---|---|---|");
            int bad = 0, n = 0; var seen = new HashSet<string>();
            foreach (var g in UnityEngine.Object.FindObjectsByType<Graphic>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (!g.enabled || !g.gameObject.activeInHierarchy || !g.canvas || g.canvas.renderMode == RenderMode.WorldSpace) continue;
                Texture tex = null; Vector2 texPx = default; bool chrome = false;
                if (g is RawImage ri && ri.texture) { tex = ri.texture; texPx = new Vector2(tex.width * ri.uvRect.width, tex.height * ri.uvRect.height); }
                else if (g is Image im && im.sprite) { tex = im.sprite.texture; texPx = im.sprite.rect.size; chrome = im.type == Image.Type.Sliced || im.type == Image.Type.Tiled; }
                if (!tex) continue;
                var r = ScreenRectOf(g.rectTransform); if (r.width < 2 || r.height < 2) continue;
                if (r.xMax < 0 || r.yMax < 0 || r.xMin > Screen.width || r.yMin > Screen.height) continue;
                string path = AssetDatabase.GetAssetPath(tex); if (string.IsNullOrEmpty(path)) path = "(runtime) " + tex.name;
                var key = path + "|" + Mathf.RoundToInt(r.width) + "x" + Mathf.RoundToInt(r.height); if (!seen.Add(key)) continue;
                float ratio = Mathf.Min(texPx.x / r.width, texPx.y / r.height);
                bool content = path.Contains("/UI/") || path.Contains("/Icons/") || path.Contains("/Cursors/HD/") || path.Contains("/Menu/");
                int mips = tex.mipmapCount;
                string status = "ok";
                if (tex.filterMode == FilterMode.Point) status = "POINT";
                else if (!chrome && content && ratio < 1.99f) status = "SOURCE < 2x";
                else if (!chrome && !content && ratio < 0.99f && !path.StartsWith("(runtime)")) status = "magnified";
                else if (!chrome && mips <= 1 && ratio > 1.5f) status = "NO MIPS";
                if (status != "ok") bad++;
                n++;
                sb.AppendLine("| " + status + " | " + g.name + " | " + path.Replace("Assets/OpenZ/Resources/OpenZ/", "") + " | " + Mathf.RoundToInt(texPx.x) + "x" + Mathf.RoundToInt(texPx.y)
                              + " | " + Mathf.RoundToInt(r.width) + "x" + Mathf.RoundToInt(r.height) + " | " + ratio.ToString("0.00") + (chrome ? " (chrome)" : "") + " | " + tex.filterMode + " | " + mips + " |");
            }
            sb.Insert(0, "");
            File.WriteAllText(Path.Combine(dir, "audit.md"), sb.ToString().Replace("| Status |", "Flagged: " + bad + " of " + n + "\n\n| Status |"));
        }
    }
}
#endif
