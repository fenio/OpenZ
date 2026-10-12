using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace OpenZ
{
    /// <summary>
    /// The look of the digits on the world timer badges (OpenZUnitOverlay factory / fort timers,
    /// BridgeRepairBadges). TextMeshPro: one shared material made from the label's own Inter Tight SDF material: face dilate
    /// (heavier strokes), a dark GM_950 outline and a soft drop underlay, plus TMP bold. Legacy uGUI Text: bold + a 1.5 px outline.
    /// The sizes themselves come from the badge (reference px); the overlay canvases scale with the UI SCALE (OzText.UiScale).
    /// </summary>
    public static class OzBadgeText
    {
        public static float FaceDilate = 0.2f, OutlineWidth = 0.22f, UnderlayAlpha = 0.55f, UnderlayOffset = 0.55f, UnderlaySoftness = 0.3f;
        public static Color OutlineColor = new Color(0.035f, 0.045f, 0.04f, 0.95f);                // GM_950, near black
        static Material mat; static Material madeFrom;

        public static void Apply(OzText t)
        {
            if (t == null || !t.graphic) return;
            if (t.graphic is TextMeshProUGUI tmp)
            {
                var src = tmp.fontSharedMaterial;
                if (src && src != mat && (!mat || madeFrom != src))
                {
                    mat = new Material(src) { name = "OZ_Text_Badge" };
                    madeFrom = src;
                    mat.EnableKeyword("OUTLINE_ON"); mat.EnableKeyword("UNDERLAY_ON");
                    mat.SetFloat("_FaceDilate", FaceDilate);
                    mat.SetColor("_OutlineColor", OutlineColor); mat.SetFloat("_OutlineWidth", OutlineWidth); mat.SetFloat("_OutlineSoftness", 0f);
                    mat.SetColor("_UnderlayColor", new Color(0f, 0f, 0f, UnderlayAlpha));
                    mat.SetFloat("_UnderlayOffsetX", UnderlayOffset); mat.SetFloat("_UnderlayOffsetY", -UnderlayOffset);
                    mat.SetFloat("_UnderlayDilate", 0.25f); mat.SetFloat("_UnderlaySoftness", UnderlaySoftness);
                    try { ShaderUtilities.UpdateShaderRatios(mat); } catch (System.Exception) { }
                }
                if (mat) tmp.fontSharedMaterial = mat;
                tmp.fontStyle |= FontStyles.Bold;
                tmp.extraPadding = true;                                                               // room for the outline + underlay
            }
            else if (t.graphic is Text leg)
            {
                leg.fontStyle = FontStyle.Bold;
                foreach (var s in leg.GetComponents<Shadow>()) Object.Destroy(s);
                var o = leg.gameObject.AddComponent<Outline>(); o.effectColor = OutlineColor; o.effectDistance = new Vector2(1.5f, -1.5f);
            }
        }
    }
}
