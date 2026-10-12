using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// paint texels to a target khaki (keeping each texel's luminance, so the albedo's gradient and soft AO survive): these are the
    /// targets, set as globals. Choice in PlayerPrefs oz_khaki: 0 off (the old toy paint), 1 target #B08850, 2 A, 3 B, 4 C.
    /// </summary>
    public static class PaintKhaki
    {
        public static readonly string[] Names = { "off (toy paint)", "target #B08850", "A olive-khaki #A08C58", "B sand-khaki #B89A6A", "C darker tan #9A7448" };
        static readonly Color32[] Srgb = { new Color32(0, 0, 0, 0), new Color32(176, 136, 80, 255), new Color32(160, 140, 88, 255), new Color32(184, 154, 106, 255), new Color32(154, 116, 72, 255) };
        /// <summary>Linear luminance of the toy albedos' usual paint (218, 152, 68): a texel this bright takes the target exactly.</summary>
        public const float RefLum = 0.378f;
        public static int Current { get; private set; } = -1;
        /// <summary>Level of the remap (1 = the target at the reference luminance). Tuned against the rendered jeep in Play.</summary>
        public static float Level = 1f;
        /// <summary>
        /// (the albedo target itself renders #AE7B43, a little orange): B is the default.</summary>
        public const int Default = 3;

        static readonly int IdK = Shader.PropertyToID("_OZToyKhaki"), IdRef = Shader.PropertyToID("_OZToyKhakiRef");

        public static void ApplySaved()
        {
            int k = Default;
            try { k = PlayerPrefs.GetInt("oz_khaki", Default); } catch { }
            Apply(k);
        }

        public static void Apply(int k)
        {
            k = Mathf.Clamp(k, 0, Srgb.Length - 1); Current = k;
            if (k == 0) { Shader.SetGlobalVector(IdK, Vector4.zero); return; }
            var c = ((Color)Srgb[k]).linear * Level;
            Shader.SetGlobalVector(IdK, new Vector4(c.r, c.g, c.b, 1f));
            Shader.SetGlobalFloat(IdRef, RefLum);
        }

        /// <summary>
        /// </summary>
        public static Color TargetLinear
        {
            get
            {
                int k = Current < 0 ? Default : Current;
                if (k <= 0) return Color.clear;
                var c = ((Color)Srgb[k]).linear * Level; c.a = 1f; return c;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot() => ApplySaved();
    }
}
