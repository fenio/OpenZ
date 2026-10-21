using System;
using System.Reflection;
using System.Text;
using OpenZ.Sim;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.UI;

namespace OpenZ
{
    /// <summary>Colour tokens (UI_STYLE_GUIDE.md 2.1 / 2.4 and the kit's LED clock), sRGB.</summary>
    public static class OzUi
    {
        public static readonly Color32 GM950 = new Color32(11, 13, 12, 255), GM900 = new Color32(18, 21, 19, 255), GM850 = new Color32(26, 30, 28, 255), GM800 = new Color32(34, 39, 36, 255),
            GM700 = new Color32(43, 49, 45, 255), GM600 = new Color32(53, 60, 55, 255), GM500 = new Color32(67, 75, 69, 255), GM400 = new Color32(86, 96, 90, 255),
            OD700 = new Color32(48, 52, 42, 255), OD600 = new Color32(59, 64, 47, 255), OD500 = new Color32(79, 85, 64, 255), Khaki = new Color32(214, 207, 174, 255),
            Ink = new Color32(244, 242, 234, 255), Ink2 = new Color32(200, 202, 191, 255), Muted = new Color32(141, 146, 134, 255), Dim = new Color32(92, 97, 90, 255),
            AmberLo = new Color32(185, 130, 48, 255), Amber = new Color32(242, 184, 75, 255), AmberHi = new Color32(255, 217, 131, 255), Signal = new Color32(232, 102, 42, 255),
            Alert = new Color32(255, 90, 60, 255), AlertText = new Color32(255, 180, 163, 255), Go = new Color32(126, 217, 87, 255), GoLo = new Color32(79, 168, 58, 255), HpLost = new Color32(247, 203, 107, 255),
            RedDeep = new Color32(110, 23, 16, 255), Red = new Color32(176, 40, 30, 255), RedHi = new Color32(217, 69, 47, 255), RedText = new Color32(240, 138, 116, 255),
            BlueDeep = new Color32(28, 48, 112, 255), Blue = new Color32(47, 79, 168, 255), BlueHi = new Color32(77, 115, 217, 255), BlueText = new Color32(157, 180, 255, 255),
            NeutralDeep = new Color32(95, 91, 83, 255), Neutral = new Color32(156, 150, 138, 255), NeutralText = new Color32(200, 194, 180, 255), HazardInk = new Color32(20, 22, 24, 255),
            BuildingChip = new Color32(46, 90, 38, 255), BuildingText = new Color32(189, 245, 156, 255),
            Led = new Color32(255, 199, 102, 255), LedGlow = new Color32(255, 143, 42, 255);

        public static Color A(Color32 c, float a) { Color k = c; k.a = a; return k; }
        /// <summary>UI mark colour of a team (brackets, dots, minimap): RED_HI / BLUE_HI / NEUTRAL.</summary>
        public static Color Mark(Team t) => (t = TeamColors.Shown(t)) == Team.Red ? RedHi : t == Team.Blue ? BlueHi : Neutral;
        /// <summary>Team paint for chips and strength bars: RED / BLUE / NEUTRAL_DEEP.</summary>
        public static Color TeamPaint(Team t) => (t = TeamColors.Shown(t)) == Team.Red ? Red : t == Team.Blue ? Blue : NeutralDeep;
        /// <summary>Team-coloured text (only for type >= 14 u, guide 2.1).</summary>
        public static Color TeamText(Team t) => (t = TeamColors.Shown(t)) == Team.Red ? RedText : t == Team.Blue ? BlueText : NeutralText;
        public static string Hex(Color32 c) => $"#{c.r:X2}{c.g:X2}{c.b:X2}";
    }

    /// <summary>Motion tokens (guide 7.1): unscaled time, "reduced motion" option.</summary>
    public static class OzMotion
    {
        public const float Micro = 0.08f, Short = 0.16f, Medium = 0.28f, Long = 0.6f, Stamp = 0.22f, BlinkFast = 0.15f, BlinkSlow = 0.3f;
        static int reduced = -1;
        public static bool Reduced
        {
            get { if (reduced < 0) reduced = PlayerPrefs.GetInt("oz_reduced_motion", 0); return reduced == 1; }
            set { reduced = value ? 1 : 0; PlayerPrefs.SetInt("oz_reduced_motion", reduced); }
        }
        public static float Now => Time.unscaledTime;
        /// <summary>Duration with the reduced-motion rule (0, fades capped at 120 ms).</summary>
        public static float Dur(float d, bool fade = false) => !Reduced ? d : fade ? Mathf.Min(d, 0.12f) : 0f;
        /// <summary>0..1 progress of an animation that started at `start` (unscaled), 1 when the duration is 0.</summary>
        public static float T(float start, float dur, float delay = 0f) => dur <= 0f ? (Now >= start + delay ? 1f : 0f) : Mathf.Clamp01((Now - start - delay) / dur);
        public static bool Blink(float period) => Mathf.Repeat(Now, period * 2f) < period;
    }

    /// <summary>Easing curves (guide 7.1).</summary>
    public static class OzEase
    {
        public static float OutQuad(float t) => 1f - (1f - t) * (1f - t);
        public static float OutCubic(float t) { t = 1f - t; return 1f - t * t * t; }
        public static float InCubic(float t) => t * t * t;
        public static float OutQuart(float t) { t = 1f - t; return 1f - t * t * t * t; }
        public static float InOutSine(float t) => -(Mathf.Cos(Mathf.PI * t) - 1f) * 0.5f;
        public static float OutBack(float t, float s = 1.4f) { t -= 1f; return t * t * ((s + 1f) * t + s) + 1f; }
    }

    /// <summary>Crisp hairlines at any screen height (guide 8.2): 1 u = 1 px at 1080p and 1 px (not 1.48) at 1600p.</summary>
    public static class OzPx
    {
        public static float K => Mathf.Max(0.01f, Screen.height / OzLayout.RefH * OzText.UiScale);
        public static float Line(float u) => Mathf.Max(1f, Mathf.Round(u * K)) / K;
    }

    public enum OzH { L, C, R }
    public enum OzStyle { Plain, Underlay, Glow, Outline }

    /// <summary>
    /// The HUD's one text element: TextMeshPro with Inter Tight (SDF, tracking, fixed-width digits, underlay / glow
    /// materials: guide 3.2-3.3) when TMP Essential Resources and the font are in the project, else legacy uGUI Text with the
    /// same Inter Tight file (no tracking, proportional digits) so the HUD never breaks. No OS fonts anywhere.
    /// Positions follow the guide: "text at y" = cap top at y (At(..., mid: false)) or caps centred on y (mid: true).
    /// </summary>
    public sealed class OzText
    {
        public const string FontAssetPath = OzKit.Root + "Fonts/OZ_InterTight_SDF";
        public readonly RectTransform rectTransform;
        public readonly MaskableGraphic graphic;
        readonly TextMeshProUGUI tmp; readonly Text leg;
        string cur = "";
        float size; OzH h = OzH.L;
        float fitW;

        static int mode = -1;                 // 0 legacy Text, 1 TextMeshPro
        static TMP_FontAsset tmpFont;
        static Font legacyFont;
        static readonly Material[] mats = new Material[4];
        public static float UiScale = 1f;
        /// <summary></summary>
        public const float MinSize = 11f;
        /// <summary>HUD scale presets and the default: 1.35 on screens 1440 px tall or more, else 1.0 (PlayerPrefs oz_ui_scale wins).</summary>
        public static readonly float[] ScalePresets = { 0.9f, 1f, 1.15f, 1.3f, 1.45f, 1.6f };
        public const float ScaleMin = 0.9f, ScaleMax = 1.6f;
        public static float DefaultScale => OzSettings.DefaultHudScale(Screen.width, Screen.height);   // menu_exe_v1: 109 % at 2560x1600 (was 135 %), 100 % at 1080p
        public static float SavedScale => Mathf.Clamp(PlayerPrefs.HasKey("oz_ui_scale") ? PlayerPrefs.GetFloat("oz_ui_scale", 1f) : DefaultScale, ScaleMin, ScaleMax);      // options: UI SCALE (90..125 %), read by OzPx

        public static bool UsesTmp { get { Resolve(); return mode == 1; } }
        public GameObject gameObject => graphic.gameObject;
        public string text => cur;
        public float Size => size;

        static void Resolve()
        {
            if (mode >= 0) return;
            mode = 0;
            legacyFont = OzKit.Font ? OzKit.Font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (!OzKit.Font) Debug.LogWarning("[OzText] Inter Tight missing from Resources/" + OzKit.Root + "Fonts (UI kit not imported): Unity's built-in font stands in. Import the kit before a build (the release ships no OS fonts).");
            try
            {
                bool settings = Resources.Load<TMP_Settings>("TMP Settings") != null;   // TMP Essential Resources imported (never touch TMP_Settings.instance: it opens the importer)
                if (settings) tmpFont = Resources.Load<TMP_FontAsset>(FontAssetPath);
                if (settings && tmpFont == null && OzKit.Font != null && Shader.Find("TextMeshPro/Mobile/Distance Field") != null)
                {
                    tmpFont = TMP_FontAsset.CreateFontAsset(OzKit.Font, 90, 9, GlyphRenderMode.SDFAA, 2048, 2048, AtlasPopulationMode.Dynamic, false);
                    if (tmpFont) tmpFont.name = "OZ_InterTight_SDF (runtime)";
                }
                if (settings && tmpFont != null && tmpFont.material != null)
                {
                    var full = Shader.Find("TextMeshPro/Distance Field");
                    for (int i = 0; i < 4; i++)
                    {
                        var m = new Material(tmpFont.material) { name = "OZ_Text_" + (OzStyle)i };
                        if (full && m.shader != full) m.shader = full;
                        Style(m, (OzStyle)i);
                        mats[i] = m;
                    }
                    mode = 1;
                }
            }
            catch (Exception e) { Debug.LogWarning("[OzText] TextMeshPro set-up failed, using legacy Text: " + e.Message); mode = 0; }
            Debug.Log($"[OzText] {(mode == 1 ? "TextMeshPro + " + tmpFont.name : "legacy uGUI Text + " + (legacyFont ? legacyFont.name : "none"))}");
        }

        static void Style(Material m, OzStyle s)
        {
            m.DisableKeyword("UNDERLAY_ON"); m.DisableKeyword("GLOW_ON"); m.DisableKeyword("OUTLINE_ON");
            switch (s)
            {
                case OzStyle.Underlay:        // OZ_Text_Underlay: #000 60 %, offset 0.6/-0.6, softness 0.15 (guide 3.2)
                    m.EnableKeyword("UNDERLAY_ON");
                    m.SetColor("_UnderlayColor", new Color(0, 0, 0, 0.6f)); m.SetFloat("_UnderlayOffsetX", 0.6f); m.SetFloat("_UnderlayOffsetY", -0.6f);
                    m.SetFloat("_UnderlayDilate", 0f); m.SetFloat("_UnderlaySoftness", 0.15f);
                    break;
                case OzStyle.Glow:            // OZ_Text_Glow: the LED clock and wordmark fallbacks
                    m.EnableKeyword("GLOW_ON");
                    m.SetColor("_GlowColor", OzUi.A(OzUi.LedGlow, 0.75f)); m.SetFloat("_GlowOffset", 0f); m.SetFloat("_GlowInner", 0.05f);
                    m.SetFloat("_GlowOuter", 0.35f); m.SetFloat("_GlowPower", 0.5f);
                    break;
                case OzStyle.Outline:         // OZ_Text_Outline: rare text over the world
                    m.EnableKeyword("OUTLINE_ON");
                    m.SetColor("_OutlineColor", (Color)OzUi.GM950); m.SetFloat("_OutlineWidth", 0.14f);
                    break;
            }
            try { ShaderUtilities.UpdateShaderRatios(m); } catch (Exception) { }
        }

        OzText(RectTransform rt, MaskableGraphic g, TextMeshProUGUI t, Text l) { rectTransform = rt; graphic = g; tmp = t; leg = l; }

        /// <summary>New label. tracking in em (guide column "Tracking"); style = material preset.</summary>
        public static OzText Make(Transform parent, string name, string text, float size, Color color, float tracking = 0f, OzStyle style = OzStyle.Underlay)
        {
            size = Mathf.Max(size, MinSize);
            Resolve();
            var go = new GameObject(name, typeof(RectTransform)) { layer = 5 };
            var rt = (RectTransform)go.transform; rt.SetParent(parent, false);
            OzText t;
            if (mode == 1)
            {
                var x = go.AddComponent<TextMeshProUGUI>();
                x.font = tmpFont; x.fontSharedMaterial = mats[(int)style];
                x.fontSize = size; x.characterSpacing = tracking * 100f;
                x.textWrappingMode = TextWrappingModes.NoWrap; x.overflowMode = TextOverflowModes.Overflow;
                x.richText = true; x.raycastTarget = false; x.enableAutoSizing = false; x.color = color;
                x.alignment = TextAlignmentOptions.CaplineLeft;
                t = new OzText(rt, x, x, null);
            }
            else
            {
                var x = go.AddComponent<Text>();
                x.font = legacyFont; x.fontSize = Mathf.Max(1, Mathf.RoundToInt(size)); x.color = color; x.alignment = TextAnchor.MiddleLeft;
                x.horizontalOverflow = HorizontalWrapMode.Overflow; x.verticalOverflow = VerticalWrapMode.Overflow; x.raycastTarget = false; x.supportRichText = true;
                if (style == OzStyle.Underlay) { var s = go.AddComponent<Shadow>(); s.effectColor = new Color(0, 0, 0, 0.6f); s.effectDistance = new Vector2(1f, -1f); }
                if (style == OzStyle.Glow) { var o = go.AddComponent<Outline>(); o.effectColor = OzUi.A(OzUi.LedGlow, 0.4f); o.effectDistance = new Vector2(1.5f, -1.5f); }
                if (style == OzStyle.Outline) { var o = go.AddComponent<Outline>(); o.effectColor = OzUi.A(OzUi.GM950, 0.9f); o.effectDistance = new Vector2(1f, -1f); }
                t = new OzText(rt, x, null, x);
            }
            t.size = size;
            t.Set(text ?? "");
            return t;
        }

        /// <summary>Place the label: x = left / centre / right edge (h), y = cap top (mid false) or caps centre (mid true), in the
        /// parent's top-left coordinates. width = box width (0 = generous).</summary>
        public OzText At(float x, float y, OzH align = OzH.L, bool mid = false, float width = 0f)
        {
            h = align;
            float bh = Mathf.Max(size * 1.8f, 10f), w = width > 0 ? width : Mathf.Max(64f, size * 40f);
            float midY = mid ? y : y + 0.3635f * size;                                   // Inter Tight cap height 0.727 em
            float left = align == OzH.L ? x : align == OzH.C ? x - w * 0.5f : x - w;
            rectTransform.anchorMin = rectTransform.anchorMax = new Vector2(0, 1); rectTransform.pivot = new Vector2(0, 1);
            rectTransform.anchoredPosition = new Vector2(left, -(midY - bh * 0.5f)); rectTransform.sizeDelta = new Vector2(w, bh);
            ApplyAlign(false);
            fitW = width > 0 ? width : 0f; Fit();
            return this;
        }

        /// <summary>Move the pivot without moving the box (e.g. to scale a right-aligned number around its right edge).</summary>
        public OzText Pivot(Vector2 p)
        {
            var size = rectTransform.rect.size; var d = p - rectTransform.pivot;
            rectTransform.pivot = p; rectTransform.anchoredPosition += new Vector2(d.x * size.x, d.y * size.y);
            return this;
        }

        /// <summary>Fill the parent with insets (l, b, r, t) and centre the caps vertically (buttons, chips).</summary>
        public OzText FillParent(OzH align = OzH.C, float l = 0, float b = 0, float r = 0, float t = 0)
        {
            h = align;
            rectTransform.anchorMin = Vector2.zero; rectTransform.anchorMax = Vector2.one; rectTransform.pivot = new Vector2(0.5f, 0.5f);
            rectTransform.offsetMin = new Vector2(l, b); rectTransform.offsetMax = new Vector2(-r, -t);
            ApplyAlign(false);
            return this;
        }

        /// <summary>Multi-line body text (tooltips, controls list): wraps at `width`, line height 1.35, top-aligned at y.</summary>
        public OzText Body(float x, float y, float width)
        {
            rectTransform.anchorMin = rectTransform.anchorMax = new Vector2(0, 1); rectTransform.pivot = new Vector2(0, 1);
            rectTransform.anchoredPosition = new Vector2(x, -y); rectTransform.sizeDelta = new Vector2(width, size * 6f);
            if (tmp) { tmp.textWrappingMode = TextWrappingModes.Normal; tmp.lineSpacing = (1.35f - 1.211f) * 100f; }
            else { leg.horizontalOverflow = HorizontalWrapMode.Wrap; leg.lineSpacing = 1.35f / 1.211f; }
            ApplyAlign(true);
            return this;
        }

        void ApplyAlign(bool top)
        {
            if (tmp)
                tmp.alignment = top ? (h == OzH.L ? TextAlignmentOptions.TopLeft : h == OzH.C ? TextAlignmentOptions.Top : TextAlignmentOptions.TopRight)
                                    : (h == OzH.L ? TextAlignmentOptions.CaplineLeft : h == OzH.C ? TextAlignmentOptions.Capline : TextAlignmentOptions.CaplineRight);
            else
                leg.alignment = top ? (h == OzH.L ? TextAnchor.UpperLeft : h == OzH.C ? TextAnchor.UpperCenter : TextAnchor.UpperRight)
                                    : (h == OzH.L ? TextAnchor.MiddleLeft : h == OzH.C ? TextAnchor.MiddleCenter : TextAnchor.MiddleRight);
        }

        /// <summary>Assign only when the string changed (both renderers rebuild the mesh on every assignment).</summary>
        public void Set(string s)
        {
            s ??= "";
            if (s == cur) return;
            cur = s;
            if (tmp) tmp.text = s; else leg.text = mode == 0 ? StripTmpTags(s) : s;
            Fit();
        }

        /// <summary>Keep the text inside its box (the width given to At): shrink to at most 70 % of the size, then ellipsis.</summary>
        void Fit()
        {
            if (fitW <= 0f || string.IsNullOrEmpty(cur)) return;
            if (tmp)
            {
                tmp.fontSize = size; tmp.overflowMode = TextOverflowModes.Overflow;
                float pw = tmp.GetPreferredValues(cur, 99999f, 0f).x;
                if (pw > fitW) { tmp.fontSize = Mathf.Max(size * 0.7f, size * fitW / pw * 0.98f); if (tmp.GetPreferredValues(cur, 99999f, 0f).x > fitW) tmp.overflowMode = TextOverflowModes.Ellipsis; }
            }
            else
            {
                leg.fontSize = Mathf.Max(1, Mathf.RoundToInt(size)); leg.horizontalOverflow = HorizontalWrapMode.Overflow;
                float pw = leg.preferredWidth;
                if (pw > fitW) { leg.fontSize = Mathf.Max(1, Mathf.RoundToInt(Mathf.Max(size * 0.7f, size * fitW / pw * 0.98f))); if (leg.preferredWidth > fitW) leg.horizontalOverflow = HorizontalWrapMode.Wrap; }
            }
        }

        public Color color { get => graphic.color; set { if (graphic.color != value) graphic.color = value; } }
        public float Alpha { set { var c = graphic.color; if (Mathf.Abs(c.a - value) > 0.003f) { c.a = value; graphic.color = c; } } }
        public float FontSize
        {
            get => size;
            set { if (Mathf.Approximately(size, value)) return; size = value; if (tmp) tmp.fontSize = value; else leg.fontSize = Mathf.Max(1, Mathf.RoundToInt(value)); Fit(); }
        }
        public float Tracking { set { if (tmp) tmp.characterSpacing = value * 100f; } }
        public OzStyle Material { set { if (tmp) tmp.fontSharedMaterial = mats[(int)value]; } }
        public float preferredWidth => tmp ? tmp.preferredWidth : leg.preferredWidth;
        public float preferredHeight => tmp ? tmp.GetPreferredValues(cur, rectTransform.rect.width, 0f).y : leg.preferredHeight;
        public void SetActive(bool on) { if (graphic && graphic.gameObject.activeSelf != on) graphic.gameObject.SetActive(on); }

        // ------------------------------------------------------------------ number formatting (guide 3.3)
        static readonly StringBuilder sb = new StringBuilder(64);

        /// <summary>Wraps every digit run in &lt;mspace=0.62em&gt; (TMP only; colons, slashes and tags stay as they are), so
        /// clocks, counts and timers never jitter.</summary>
        public static string Tab(string s)
        {
            if (!UsesTmp || string.IsNullOrEmpty(s)) return s;
            sb.Clear(); bool inTag = false, inNum = false;
            foreach (var ch in s)
            {
                if (inTag) { sb.Append(ch); if (ch == '>') inTag = false; continue; }
                bool digit = ch >= '0' && ch <= '9';
                if (digit && !inNum) { sb.Append("<mspace=0.62em>"); inNum = true; }
                if (!digit && inNum) { sb.Append("</mspace>"); inNum = false; }
                if (ch == '<') inTag = true;
                sb.Append(ch);
            }
            if (inNum) sb.Append("</mspace>");
            return sb.ToString();
        }

        /// <summary>Legacy Text cannot parse mspace / cspace / voffset: strip those tags, keep color / size / b.</summary>
        static string StripTmpTags(string s)
        {
            if (s.IndexOf('<') < 0) return s;
            sb.Clear(); int i = 0;
            while (i < s.Length)
            {
                if (s[i] == '<')
                {
                    int e = s.IndexOf('>', i);
                    if (e < 0) { sb.Append(s, i, s.Length - i); break; }
                    string tag = s.Substring(i + 1, e - i - 1).TrimStart('/');
                    if (tag.StartsWith("mspace") || tag.StartsWith("cspace") || tag.StartsWith("voffset") || tag.StartsWith("space")) { i = e + 1; continue; }
                    sb.Append(s, i, e - i + 1); i = e + 1; continue;
                }
                sb.Append(s[i++]);
            }
            return sb.ToString();
        }
    }

    /// <summary>UI sound hooks (guide 10): the audio track's AudioDirector (found by name, like the other helpers) when it is in
    /// the project, else CombatAudio's stand-in click. Never louder than Rachel; all 2D.</summary>
    public static class OzSfx
    {
        static MethodInfo ui; static bool looked;
        public static void Ui(string id, float vol = 1f)
        {
            if (!looked)
            {
                looked = true;
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    var t = asm.GetType("OpenZ.AudioDirector"); if (t == null) continue;
                    ui = t.GetMethod("UI", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(string), typeof(float), typeof(float) }, null);
                    break;
                }
            }
            if (ui != null) { try { ui.Invoke(null, new object[] { id, vol, 1f }); return; } catch (Exception) { ui = null; } }
            if (id == "ui_click") OpenZHud.UiClickSound();
        }
    }

    /// <summary>Runtime URP post volume for the pause menu and the end screens (guide 9.12 / 9.13): saturation, exposure and
    /// vignette blended in by weight. Global, priority above the level's own volume, created on demand.</summary>
    public sealed class OzPost
    {
        readonly Volume vol; readonly VolumeProfile prof;
        public OzPost(string name, float saturation, float exposure, float vignette, int priority = 60)
        {
            var go = new GameObject(name) { hideFlags = HideFlags.DontSave };
            vol = go.AddComponent<Volume>(); vol.isGlobal = true; vol.priority = priority; vol.weight = 0f;
            prof = ScriptableObject.CreateInstance<VolumeProfile>(); prof.name = name;
            var ca = prof.Add<ColorAdjustments>(true); ca.saturation.Override(saturation); ca.postExposure.Override(exposure);
            var vg = prof.Add<Vignette>(true); vg.intensity.Override(vignette); vg.smoothness.Override(0.45f); vg.color.Override(Color.black);
            vol.sharedProfile = prof;
        }
        public float Weight { get => vol ? vol.weight : 0f; set { if (vol) vol.weight = Mathf.Clamp01(value); } }
        public void Destroy() { if (vol) UnityEngine.Object.Destroy(vol.gameObject); if (prof) UnityEngine.Object.Destroy(prof); }
    }

    /// <summary>Scene routing for the end screen and the pause menu (restart, next level, main menu).</summary>
    public static class OzScenes
    {
        public static readonly string[] MenuNames = { "MainMenu", "OpenZ_Menu", "Menu", "FrontEnd" };
        public static void Restart() { Time.timeScale = 1f; SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex); }
        /// <summary>L02 -> L17 -> main menu (or back to the first level when the front end is not in the build yet).</summary>
        public static void Next()
        {
            Time.timeScale = 1f;
            int i = SceneManager.GetActiveScene().buildIndex, n = SceneManager.sceneCountInBuildSettings;
            string next = null;
            for (int k = i + 1; k < n; k++) { var p = SceneUtilityPath(k); if (p.Contains("L17") || p.Contains("L16") || p.Contains("L02")) { next = p; break; } }
            if (next != null) SceneManager.LoadScene(next);
            else { PlayerPrefs.SetInt("oz_menu_open_select", 1); MainMenu(); }   // after the last level: back to the level select (guide 9.13)
        }
        public static bool HasMenu { get { foreach (var m in MenuNames) if (Application.CanStreamedLevelBeLoaded(m)) return true; return false; } }
        public static void MainMenu()
        {
            Time.timeScale = 1f;
            foreach (var m in MenuNames) if (Application.CanStreamedLevelBeLoaded(m)) { SceneManager.LoadScene(m); return; }
            Debug.Log("[OpenZ] no main menu scene in the build yet: loading the first level");
            SceneManager.LoadScene(0);
        }
        static string SceneUtilityPath(int i) => UnityEngine.SceneManagement.SceneUtility.GetScenePathByBuildIndex(i) ?? "";
    }
}
