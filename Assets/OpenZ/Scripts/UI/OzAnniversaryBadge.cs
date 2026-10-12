using UnityEngine;
using UnityEngine.UI;

namespace OpenZ
{
    /// <summary>
    /// Z turns 30 (1996-2026): the "30TH ANNIVERSARY · OPEN-SOURCE 3D REMAKE" badge under the OPENZ logo and on the loading
    /// screen. Kit style: gold stencil capitals (Inter Tight, wide tracking) between two thin gold rules, a small gold "30" tab
    /// in front, and a soft shine that sweeps across the letters every few seconds (masked to the glyphs, so the dark
    /// background never flashes). Reduced motion (OzMotion.Reduced) keeps it still.
    /// </summary>
    public sealed class OzAnniversaryBadge : MonoBehaviour
    {
        public const string Line = "30TH ANNIVERSARY  ·  3D TRIBUTE REMAKE";
        static readonly Color Gold = new Color32(224, 180, 92, 255), GoldHi = new Color32(255, 230, 160, 255), GoldLo = new Color32(150, 110, 48, 255);

        RectTransform shine; float width, period = 6.5f, t0;
        CanvasGroup cg;

        /// <summary>Builds the badge at (x, y) in kit units (top-left anchored like Hud.Label). size = cap size of the line.</summary>
        public static OzAnniversaryBadge Build(Transform parent, float x, float y, float size = 13f)
        {
            var root = Hud.Node("Anniversary30", parent);
            float tabW = size * 2.9f, gap = size * 0.9f, h = size * 2.3f;
            var txt = Hud.Label(root, "Line", Line, size, Gold, 0.3f, OzStyle.Plain).At(tabW + gap, h * 0.5f, OzH.L, true, 900);
            float lineW = Mathf.Min(900f, txt.preferredWidth + 4f);
            float w = tabW + gap + lineW;
            root.Place(new OzRect(x, y, w, h));

            // the "30" tab: a gold chamfer-less plate with dark stencil digits
            var tab = Hud.Img(root, "Tab", Gold); tab.rectTransform.Place(new OzRect(0, h * 0.14f, tabW, h * 0.72f));
            Hud.Label(tab.transform, "N", "30", size * 1.3f, new Color32(20, 18, 14, 255), 0.06f, OzStyle.Plain).At(tabW * 0.5f, h * 0.36f, OzH.C, true, tabW);
            // thin rules above and below the line (the stencil plate look)
            var top = Hud.Img(root, "RuleTop", OzUi.A(GoldLo, 0.85f)); top.rectTransform.Place(new OzRect(tabW + gap, 1, lineW, 1));
            var bot = Hud.Img(root, "RuleBot", OzUi.A(GoldLo, 0.85f)); bot.rectTransform.Place(new OzRect(tabW + gap, h - 2, lineW, 1));

            // shine: a soft diagonal band, masked to the glyphs of the line
            var b = root.gameObject.AddComponent<OzAnniversaryBadge>();
            var mask = txt.gameObject.AddComponent<Mask>(); mask.showMaskGraphic = true;
            var s = new GameObject("Shine", typeof(RectTransform)).GetComponent<RectTransform>();
            s.SetParent(txt.rectTransform, false);
            var ri = s.gameObject.AddComponent<RawImage>(); ri.texture = ShineTex(); ri.color = OzUi.A(GoldHi, 0.9f); ri.raycastTarget = false;
            s.anchorMin = new Vector2(0f, 0f); s.anchorMax = new Vector2(0f, 1f); s.pivot = new Vector2(0.5f, 0.5f);
            s.sizeDelta = new Vector2(size * 4f, size * 1.2f); s.localRotation = Quaternion.Euler(0, 0, -18f);
            b.shine = s; b.width = lineW; b.t0 = Time.unscaledTime + 1.6f;
            b.cg = root.gameObject.AddComponent<CanvasGroup>(); b.cg.blocksRaycasts = false; b.cg.interactable = false;
            return b;
        }

        public float Alpha { get => cg ? cg.alpha : 1f; set { if (cg) cg.alpha = value; } }

        void Update()
        {
            if (!shine) return;
            if (OzMotion.Reduced) { shine.gameObject.SetActive(false); return; }
            float t = (Time.unscaledTime - t0) % period; if (Time.unscaledTime < t0) t = -1f;
            float k = t < 0f ? -1f : t / 1.3f;                                                   // 1.3 s sweep, then rest
            shine.gameObject.SetActive(k >= 0f && k <= 1f);
            if (k >= 0f && k <= 1f) shine.anchoredPosition = new Vector2(Mathf.Lerp(-40f, width + 40f, OzEase.InOutSine(k)), 0f);
        }

        static Texture2D shineTex;
        static Texture2D ShineTex()
        {
            if (shineTex) return shineTex;
            const int W = 64;
            shineTex = new Texture2D(W, 1, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "OzShine" };
            for (int i = 0; i < W; i++) { float u = i / (W - 1f) * 2f - 1f; float a = Mathf.Pow(Mathf.Max(0f, 1f - u * u), 2f); shineTex.SetPixel(i, 0, new Color(1f, 1f, 1f, a)); }
            shineTex.Apply(); return shineTex;
        }
    }
}
