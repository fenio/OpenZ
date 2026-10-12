using System.Collections.Generic;
using OpenZ.Sim;
using UnityEngine;
using UnityEngine.UI;

namespace OpenZ
{
    /// <summary>
    /// One badge per destroyed bridge that is waiting for its auto repair
    /// (OpenZ rule: 120 s), screen-space so it stays crisp at any zoom, fading out when the rebuild starts. Drawn with the UI
    /// kit (guide 9.9 world overlays): the small production ring (track + segmented fill + cap), the kit wrench glyph in INK
    /// and a GM_950 time chip (m:ss, tabular); no amber in world space.
    /// </summary>
    [DefaultExecutionOrder(200)]   // world-linked badges reproject after the camera moved (RtsCamera, Update)
    public class BridgeRepairBadges : MonoBehaviour
    {
        class Badge { public RectTransform rt; public CanvasGroup cg; public Image arc, cap; public OzText secs; public float alpha; public int shownSecs = int.MinValue; }
        readonly Dictionary<int, Badge> badges = new Dictionary<int, Badge>();
        Canvas canvas; Sprite ringSprite; CanvasScaler scaler;                               // endgame_ui_v1: the canvas follows the UI SCALE
        static Vector2 RefRes => new Vector2(1920f, 1080f) / Mathf.Clamp(OzText.UiScale, OzText.ScaleMin, OzText.ScaleMax);
        const int Segments = 12;
        /// <summary>ZF23 proof counter: repair-station countdown badges shown.</summary>
        public static int RepairCountdowns;

        void Start()
        {
            var go = new GameObject("BridgeRepairBadges", typeof(Canvas), typeof(CanvasScaler));
            canvas = go.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 5;   // under the HUD
            var cs = go.GetComponent<CanvasScaler>(); cs.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; cs.referenceResolution = RefRes; cs.matchWidthOrHeight = 1;   // endgame_ui_v1: was 1920x1080 at any UI scale
            scaler = cs;
            if (!OzKit.Available) ringSprite = MakeRing();
        }

        void LateUpdate()
        {
            var g = ZGame.I; if (g == null || g.World == null || !canvas) return;
            var cam = Camera.main; if (!cam) return;
            var hud = OpenZHud.I;
            canvas.enabled = hud == null || (hud.hudVisible && !hud.Modal);
            if (scaler) { var rr = RefRes; if (scaler.referenceResolution != rr) scaler.referenceResolution = rr; }   // endgame_ui_v1: options UI SCALE
            var W = g.World;
            foreach (var o in W.Objects)
            {
                // ZF23: the repair station's countdown (Zod's flat 5 s, ZObject.RepairTime) uses the same badge while it repairs
                bool station = o.Type == "repair" && o.Kind == ZKind.Building;
                if (!o.IsBridge && !station) continue;
                bool down = station ? o.RepairingUnit && !o.Destroyed : o.Destroyed && o.DoAutoRepair;
                badges.TryGetValue(o.Id, out var b);
                if (!down && b == null) continue;
                if (b == null) { badges[o.Id] = b = Make(); if (station) RepairCountdowns++; }
                double left = (station ? o.RepairTime : o.NextAutoRepairTime) - W.Time;
                float total = station ? 5f : (float)(W.BridgeRepairSeconds > 0 ? W.BridgeRepairSeconds : 660);
                float want = down && left > (station ? 0.05 : 3.5) ? 1f : 0f;                // fades out with the rebuild effect
                b.alpha = Mathf.MoveTowards(b.alpha, want, Time.unscaledDeltaTime * 2.5f);
                b.cg.alpha = b.alpha * 0.9f;
                if (b.alpha <= 0.001f) { b.rt.gameObject.SetActive(false); if (!down) { Destroy(b.rt.gameObject); badges.Remove(o.Id); } continue; }
                b.rt.gameObject.SetActive(true);
                var c = g.CenterWorld(o); c.y = station ? g.GroundY(c.x, c.z) + 7.4f : (g.level == "L17" ? 0.3f : 1.2f) + 3.2f;   // ZF23: over the gantry
                var sp = cam.WorldToScreenPoint(c);
                if (sp.z < 0) { b.rt.gameObject.SetActive(false); continue; }
                RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)canvas.transform, new Vector2(Mathf.Round(sp.x), Mathf.Round(sp.y)), null, out var lp);   // ui_quality_v1: whole pixels
                b.rt.anchoredPosition = lp;
                float frac = Mathf.Clamp01((float)(left / total));
                float seg = Mathf.Ceil(frac * Segments) / Segments;                           // segmented: one tick per 10 s at 120 s
                b.arc.fillAmount = seg;
                if (b.cap) b.cap.rectTransform.localRotation = Quaternion.Euler(0, 0, -360f * seg);
                int secs = Mathf.CeilToInt((float)System.Math.Max(0, left));                   // the string only when the second changes
                if (secs != b.shownSecs) { b.shownSecs = secs; b.secs.Set(OzText.Tab(UnitInfo.Mmss(secs))); }
            }
        }

        Badge Make()
        {
            var root = new GameObject("Badge", typeof(RectTransform), typeof(CanvasGroup)); var rt = (RectTransform)root.transform;
            rt.SetParent(canvas.transform, false); rt.sizeDelta = new Vector2(110, 40); rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            var b = new Badge { rt = rt, cg = root.GetComponent<CanvasGroup>() }; b.cg.blocksRaycasts = false; b.cg.alpha = 0;
            // ring: track, segmented fill (time left), cap; the wrench glyph inside
            var track = Hud.Kit(rt, "Track", "Production/ring_sm_track", new OzRect(0, 0, 40, 40), null, Image.Type.Simple);
            if (!OzKit.Available) { track.sprite = ringSprite; track.color = OzUi.A(OzUi.Muted, 0.25f); }
            if (OzKit.Available) b.arc = OzKit.RingFill(rt, "Arc", true);
            else { b.arc = Hud.Img(rt, "Arc", OzUi.Ink); b.arc.sprite = ringSprite; b.arc.type = Image.Type.Filled; b.arc.fillMethod = Image.FillMethod.Radial360; b.arc.fillOrigin = (int)Image.Origin360.Top; }
            b.arc.rectTransform.Place(new OzRect(0, 0, 40, 40));
            b.arc.color = OzUi.A(OzUi.Ink, 0.9f);                                           // INK: world marks never use amber
            if (OzKit.Available)
            {
                b.cap = Hud.Kit(rt, "Cap", "Production/ring_sm_cap", new OzRect(0, 0, 40, 40), OzUi.Ink, Image.Type.Simple);
                b.cap.rectTransform.pivot = new Vector2(0.5f, 0.5f); b.cap.rectTransform.anchoredPosition = new Vector2(20, -20);
            }
            Hud.Icon(rt, "Wrench", "wrench", 11, 11, 18, OzUi.Ink);
            var chip = Hud.Chip(rt, "Time", new OzRect(44, 8, 62, 24), "", OzUi.A(OzUi.GM950, 0.9f), OzUi.Ink, OpenZUnitOverlay.TimerDigits, 0f, OzUi.A(OzUi.Khaki, 0.24f));   // endgame_ui_v1: was 48 x 18, 11 px
            b.secs = chip.text;
            OzBadgeText.Apply(b.secs);                                                              // endgame_ui_v1: bold digits, dark outline + drop
            return b;
        }

        /// <summary>Thin anti-aliased ring with 12 gaps (fallback before the UI kit is imported).</summary>
        static Sprite MakeRing()
        {
            int N = 128; var tex = new Texture2D(N, N, TextureFormat.RGBA32, false); var px = new Color32[N * N];
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float dx = x - N / 2f + 0.5f, dy = y - N / 2f + 0.5f, r = Mathf.Sqrt(dx * dx + dy * dy) / (N / 2f);
                    float a = Mathf.Clamp01((0.98f - r) * 40f) * Mathf.Clamp01((r - 0.82f) * 40f);
                    float ang = Mathf.Repeat(Mathf.Atan2(dy, dx) * Mathf.Rad2Deg + 90f, 360f) % (360f / Segments);
                    if (ang < 4f) a = 0;                                                              // segment gap
                    px[y * N + x] = new Color32(255, 255, 255, (byte)(a * 255));
                }
            tex.SetPixels32(px); tex.Apply(); tex.wrapMode = TextureWrapMode.Clamp;
            return Sprite.Create(tex, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f));
        }
    }
}
