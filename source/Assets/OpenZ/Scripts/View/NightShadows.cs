using System.Collections.Generic;
using OpenZ.Sim;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace OpenZ
{
    /// <summary>
    /// Answer: on purpose: only the 3 searchlights cast shadows. Per the no-silent-cuts rule this is now an
    /// Options row NIGHT LIGHT SHADOWS: OFF / SEARCHLIGHTS / NEARBY LIGHTS (oz_night_shadows, default NEARBY, saved, live). It is THE
    /// NEARBY = the searchlights + up to <see cref="NearbySlots"/> real SPOT lights (street lamps, floods, work lights) nearest the
    /// screen centre that have something to shadow in their pool, ranked by NightLightBudget with hysteresis (a slot is kept at least
    /// <see cref="HoldSeconds"/> s; a held slot changes hands only on a clear score lead <see cref="SwapLead"/>), cross-faded
    /// (shadowStrength 0 -> 0.88 over 0.35 s with LightShadows.Soft kept on during the fade: no pops). Point lights never get one
    /// (6 atlas slices each). Per shadowed light: soft quality MEDIUM on the light (the global / sun setting is untouched), shadow
    /// map tier HIGH for the 2 best, MEDIUM for the rest, depth / normal bias set per light (no acne, no peter-panning), and the
    /// rule; NightLightBudget keeps HeatLevel 0). Vehicle headlights stay unshadowed in this version (see the package README).
    /// </summary>
    public static class NightShadows
    {
        public const string Pref = "oz_night_shadows";
        public const string Label = "NIGHT LIGHT SHADOWS";
        public const string Tip = "NIGHT LIGHT SHADOWS|Off: no shadows from night lights. Searchlights: only the fort searchlights cast shadows. Nearby lights (default): the searchlights plus the street lamps and floodlights nearest the middle of the screen. Costs a little frame rate at night.|";
        public static readonly string[] Names = { "OFF", "SEARCHLIGHTS", "NEARBY LIGHTS" };
        public static int NearbySlots = 5, HighTierCount = 2;
        public static float HoldSeconds = 1.5f, SwapLead = 1.5f, FadeSeconds = 0.35f, Strength = 0.88f;
        public static float DepthBias = 0.06f, NormalBias = 0.35f, CasterRadius = 7f;
        static int mode = -1;

        public static int Mode
        {
            get { if (mode < 0) mode = Mathf.Clamp(PlayerPrefs.GetInt(Pref, 2), 0, 2); return mode; }
            set
            {
                mode = Mathf.Clamp(value, 0, 2); PlayerPrefs.SetInt(Pref, mode); PlayerPrefs.Save();
                Debug.Log($"[OpenZ Options] night light shadows: {Names[mode]} ({Pref} {mode}, saved, live)");
            }
        }
        public static string Name => Names[Mode];
        public static void Step(int dir) => Mode = (Mode + (dir < 0 ? 2 : 1)) % 3;

        static readonly System.Reflection.FieldInfo TierField = typeof(UniversalAdditionalLightData).GetField("m_AdditionalLightsShadowResolutionTier",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        /// <summary>Per light, while its shadow fades in / is on: soft MEDIUM on the light, tier, bias. Cached per light (no garbage).</summary>
        static readonly Dictionary<Light, int> tierSet = new Dictionary<Light, int>();
        static int tierSceneSeen = int.MinValue;
        public static void Configure(Light l, bool high)
        {
            int tier = high ? UniversalAdditionalLightData.AdditionalLightsShadowResolutionTierHigh : UniversalAdditionalLightData.AdditionalLightsShadowResolutionTierMedium;
            if (tierSet.Count > 0 && tierSceneSeen != UnityEngine.SceneManagement.SceneManager.GetActiveScene().handle) tierSet.Clear();
            tierSceneSeen = UnityEngine.SceneManagement.SceneManager.GetActiveScene().handle;
            if (tierSet.TryGetValue(l, out int t) && t == tier) return;
            tierSet[l] = tier;
            var ald = l.GetUniversalAdditionalLightData();
            if (!ald) return;
            try { if (TierField != null) TierField.SetValue(ald, tier); } catch { }
            ald.softShadowQuality = SoftShadowQuality.Medium;                                   // this light only (m_SoftShadowQuality 3 stays for the sun)
            ald.usePipelineSettings = false;
            l.shadowBias = DepthBias; l.shadowNormalBias = NormalBias;
        }

        /// <summary>Something to shadow in the light's pool (units, wrecks, buildings, map items, or a Props / Buildings collider).</summary>
        /// <summary>Where the light's axis meets the ground (its pool), capped at its range.</summary>
        public static Vector3 PoolCentre(NightFixture f)
        {
            var lp = f.LightPos; if (!f.Light) return lp;
            var fw = f.Light.transform.forward; float gy = ZGame.I ? ZGame.I.GroundY(lp.x, lp.z) : 0f;
            float t = fw.y < -0.05f ? (lp.y - gy) / -fw.y : f.Range * 0.5f;
            var c = lp + fw * Mathf.Min(t, f.Range); c.y = ZGame.I ? ZGame.I.GroundY(c.x, c.z) : c.y;
            return c;
        }

        public static bool HasCasters(NightFixture f)
        {
            var c = PoolCentre(f);
            float r = Mathf.Min(CasterRadius, Mathf.Max(2f, f.Range * 0.45f));
            var g = ZGame.I;
            if (g != null && g.World != null)
                foreach (var o in g.World.Objects)
                {
                    float x = (float)(o.PosX + o.WidthPix * 0.5) / 8f, z = -(float)(o.PosY + o.HeightPix * 0.5) / 8f;
                    if ((x - c.x) * (x - c.x) + (z - c.z) * (z - c.z) < r * r) return true;
                }
            return Physics.CheckSphere(new Vector3(c.x, c.y + 1f, c.z), r, PropMask, QueryTriggerInteraction.Ignore);
        }
        static int propMask = -1;
        static int PropMask { get { if (propMask < 0) propMask = LayerMask.GetMask("Props", "Buildings", "Units"); return propMask; } }

        public static string Describe() => $"night light shadows {Name}";
    }
}
