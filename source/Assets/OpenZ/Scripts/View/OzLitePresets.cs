// OpenZ (GPL-3.0)
// HIGH, the default, is not touched: nothing in this file runs while the preset is HIGH. The game reads OzLite.Active at three
// places (OzQuality.Apply, once per preset change; NightLightBudget.Tick and its GrantNearby, five times a second) and takes its
// old line when it is false. OzLite has no static constructor and no field with a start value, so reading that flag runs no code either.
// While the preset is MEDIUM or LOW one small object (OZ_LitePresets) lowers seven things, each of them through a number or a
// switch the game already had, and puts every one of them back the moment the preset returns to HIGH or the game ends:
//   shadow reach         the sun's shadows follow the view between the preset's own limits (OzLiteTable.ReachMin / ReachMax)
//   shadow steps         LOW draws the sun's shadows in one step instead of two
//   night lights         fewer lamps, floods and headlights light the ground at once; the others keep head, beam and painted pool
//   night light shadows  fewer night lights cast shadows (the NIGHT LIGHT SHADOWS row still switches them)
//   rain                 fewer drops and splashes (wet ground, sound and thunder as before); on Level 16 the far, mid and near
//                        streak layers (OzJungleWeather.RainLayers.cs) are thinned by the same share, so the depth stays as on HIGH
//   lamp glow            LOW: the soft glow around each lamp head is not drawn (lens, bulb, painted pool and real light stay)
//   lamp beams           LOW: the cone of light in the air under each lamp is not drawn
// The engine-wide distance factor of far shapes (plants, rocks, sandbag walls) is NOT touched: lowering it hides things.
// View only: no rule of the game reads any of it. Units, health bars, range rings, selection, flags, shots and sounds are as on HIGH.
// OzLite.Off (never saved, 0 in a game) lets the measuring menu switch single cuts off to count what each one pays.
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace OpenZ
{
    public static class OzLite
    {
        // No start values and no static constructor here, on purpose (see the top of the file).
        /// <summary>True only while GRAPHICS QUALITY is MEDIUM or LOW.</summary>
        public static bool Active;
        /// <summary>1 = MEDIUM, 2 = LOW while Active, else 0.</summary>
        public static int Level;
        /// <summary>One bit per cut that the measuring menu has switched off (OzLiteTable.Cut*). Never saved; 0 in a game.</summary>
        public static int Off;
        /// <summary>How often code of this file was entered since the program started. The check wants it to stand still on HIGH.</summary>
        public static int Runs;
        static OzLiteDriver driver;
        static bool quitHooked;

        /// <summary>Is this cut in force now?</summary>
        public static bool Cut(int cut) => Active && (Off & (1 << cut)) == 0;

        /// <summary>From OzQuality.Apply, and only when the preset is MEDIUM / LOW or a lighter preset is being left.</summary>
        public static void Apply(int level)
        {
            Runs++;
            if (level <= 0) { Leave(); return; }
            if (!Active) Off = 0;                                                               // a lighter preset always starts with every cut in force
            Level = level > 2 ? 2 : level; Active = true;
            if (!quitHooked) { quitHooked = true; Application.quitting += Leave; }                 // the editor: Play stop puts everything back
            if (!driver)
            {
                var go = new GameObject("OZ_LitePresets"); Object.DontDestroyOnLoad(go);
                driver = go.AddComponent<OzLiteDriver>();
            }
            driver.Refresh(true);
            Debug.Log($"[OpenZ Quality] lighter {OzQuality.Names[Level]}: {Describe()}");
        }

        /// <summary>Back to HIGH (or the game ends): every number this file changed gets the value it had.</summary>
        public static void Leave()
        {
            Runs++;
            if (driver) { driver.Restore(); Object.Destroy(driver.gameObject); driver = null; }
            bool was = Active;
            Active = false; Level = 0; Off = 0;
            if (was) Debug.Log("[OpenZ Quality] lighter presets off: every changed number is back");
        }

        /// <summary>The measuring menu: switch cuts off (a bit each) or all on again (0). Not saved.</summary>
        public static void SetOff(int mask)
        {
            Runs++;
            Off = mask;
            if (driver) driver.Refresh(true);
        }

        /// <summary>NightLightBudget.Tick, MEDIUM / LOW only: the budget's limit with this preset's floor.</summary>
        public static int NightMax(int max, int maxLights)
        {
            Runs++;
            return OzLiteTable.NightMax(Level, Cut(OzLiteTable.CutNightLights), max, maxLights);
        }

        /// <summary>NightLightBudget.GrantNearby, MEDIUM / LOW only: nearby lights that already hold a shadow give it up when the preset
        /// allows fewer (the list is in order, best first, from the tick before; a shadow that is given up fades out like any other).
        /// With the cut off the list never holds more than the game's own number, so nothing is taken.</summary>
        public static void TrimNearby(System.Collections.Generic.List<NightFixture> held)
        {
            Runs++;
            int keep = NightShadows.NearbySlots < 0 ? 0 : NightShadows.NearbySlots;
            while (held.Count > keep) held.RemoveAt(held.Count - 1);
        }

        /// <summary>One line for the log and the checks: what is lowered right now.</summary>
        public static string Describe()
        {
            if (!Active) return "off (HIGH)";
            int l = Level;
            return (Cut(OzLiteTable.CutSunReach) ? $"shadow reach {OzLiteTable.ReachMin[l]:0}-{OzLiteTable.ReachMax[l]:0} m" : "shadow reach as HIGH")
                 + (Cut(OzLiteTable.CutSunSteps) ? $", {OzLiteTable.SunSteps[l]} shadow step(s)" : $", {OzLiteTable.SunStepsBefore[l]} shadow step(s)")
                 + (Cut(OzLiteTable.CutNightLights) ? $", night lights {OzLiteTable.NightReal[l]} (headlights {OzLiteTable.Headlights[l]})" : ", night lights as HIGH")
                 + (Cut(OzLiteTable.CutNightShadows) ? $", night light shadows {OzLiteTable.SearchShadows[l]} + {OzLiteTable.NearbyShadows[l]}" : ", night light shadows as HIGH")
                 + (Cut(OzLiteTable.CutRain) ? $", rain {OzLiteTable.Rain[l] * 100f:0} %" : ", rain as HIGH")
                 + (Cut(OzLiteTable.CutLampGlow) && !OzLiteTable.Halos[l] ? ", no lamp glow" : ", lamp glow as HIGH")
                 + (Cut(OzLiteTable.CutLampBeams) && !OzLiteTable.Beams[l] ? ", no lamp beams" : ", lamp beams as HIGH");
        }
    }

    /// <summary>Lives only while the preset is MEDIUM or LOW. Sets the lighter numbers once per preset change and once per new
    /// level object (it looks for those twice a second), remembers what each number was, and drives the sun's shadow reach on
    /// the preset's own settings file with the game's own rule (ShadowDistanceByHeight stands down meanwhile).</summary>
    [DefaultExecutionOrder(900)]
    public sealed class OzLiteDriver : MonoBehaviour
    {
        static readonly Vector3[] Top = { new Vector3(0f, 1f, 0f), new Vector3(1f, 1f, 0f), new Vector3(0.5f, 1f, 0f) };

        bool staticsKept, reachWasOn; int nearby0, sharp0;
        NightLights nl; int nlReal0, nlShadow0; bool nlHalos0, nlBeams0;
        VehicleLights vl; int vlReal0, vlCones0;
        OzWeather wx; int wxDrops0; float wxSplash0;
        OzJungleWeather jw; int jwDrops0, jwMid0, jwNear0; float jwSplash0, jwDrip0;
        UniversalRenderPipelineAsset urp; float urpReach0; int urpSteps0;
        float nextLook, reach;
        bool restored;

        /// <summary>The reach in force (m) and the settings file it is written to, for the checks.</summary>
        public float Reach => reach;
        public UniversalRenderPipelineAsset Asset => urp;

        /// <summary>force: the preset or the measuring switches changed, set every number again. Otherwise only new level objects.</summary>
        public void Refresh(bool force)
        {
            OzLite.Runs++;
            int l = OzLite.Level;
            if (restored || l < 1 || l > 2) return;

            // ---- three statics that belong to no object
            if (!staticsKept)
            {
                staticsKept = true;
                nearby0 = NightShadows.NearbySlots; sharp0 = NightShadows.HighTierCount; reachWasOn = ShadowDistanceByHeight.On;
            }
            if (force)
            {
                bool ns = OzLite.Cut(OzLiteTable.CutNightShadows);
                NightShadows.NearbySlots = ns ? Mathf.Min(nearby0, OzLiteTable.NearbyShadows[l]) : nearby0;
                NightShadows.HighTierCount = ns ? Mathf.Min(sharp0, OzLiteTable.SharpShadows[l]) : sharp0;
                ShadowDistanceByHeight.On = false;                                              // this object sets the reach on the preset's own file
            }

            // ---- the settings file of the preset in force
            var cur = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (cur != urp)
            {
                if (urp) { urp.shadowDistance = urpReach0; urp.shadowCascadeCount = urpSteps0; }   // the file that was left gets its own values back
                urp = cur; reach = 0f; force = true;
                if (cur) { urpReach0 = cur.shadowDistance; urpSteps0 = cur.shadowCascadeCount; }
            }
            if (urp && force)
            {
                int steps = OzLite.Cut(OzLiteTable.CutSunSteps) ? OzLiteTable.SunSteps[l] : OzLiteTable.SunStepsBefore[l];
                if (urp.shadowCascadeCount != steps) urp.shadowCascadeCount = steps;
                reach = 0f;
            }

            // ---- the level's own objects (a new level makes new ones with their own numbers)
            var n = NightLights.I;
            if (n != nl) { nl = n; if (n) { nlReal0 = n.maxRealLights; nlShadow0 = n.maxShadowed; nlHalos0 = n.halos; nlBeams0 = n.beams; } force = true; }
            if (nl && force)
            {
                nl.maxRealLights = OzLite.Cut(OzLiteTable.CutNightLights) ? Mathf.Min(nlReal0, OzLiteTable.NightReal[l]) : nlReal0;
                nl.maxShadowed = OzLite.Cut(OzLiteTable.CutNightShadows) ? Mathf.Min(nlShadow0, OzLiteTable.SearchShadows[l]) : nlShadow0;
                // the game's own two switches (NightLights draws no glow / no cone while they are off); the painted pools stay: they
                // are the light of every lamp that has no real light
                nl.halos = nlHalos0 && (OzLiteTable.Halos[l] || !OzLite.Cut(OzLiteTable.CutLampGlow));
                nl.beams = nlBeams0 && (OzLiteTable.Beams[l] || !OzLite.Cut(OzLiteTable.CutLampBeams));
            }
            var v = VehicleLights.I;
            if (v != vl) { vl = v; if (v) { vlReal0 = v.maxRealLights; vlCones0 = v.maxEffectVehicles; } force = true; }
            if (vl && force)
            {
                bool c = OzLite.Cut(OzLiteTable.CutNightLights);
                vl.maxRealLights = c ? Mathf.Min(vlReal0, OzLiteTable.Headlights[l]) : vlReal0;
                vl.maxEffectVehicles = c ? Mathf.Min(vlCones0, OzLiteTable.HeadlightCones[l]) : vlCones0;
            }
            float rain = OzLite.Cut(OzLiteTable.CutRain) ? OzLiteTable.Rain[l] : 1f;
            var w = OzWeather.I;
            if (w != wx) { wx = w; if (w) { wxDrops0 = w.dropsAtFull; wxSplash0 = w.splashesPerSecond; } force = true; }
            if (wx && force) { wx.dropsAtFull = Mathf.RoundToInt(wxDrops0 * rain); wx.splashesPerSecond = wxSplash0 * rain; }
            var j = OzJungleWeather.I;
            if (j != jw) { jw = j; if (j) { jwDrops0 = j.dropsAtFull; jwMid0 = j.midDrops; jwNear0 = j.nearDrops; jwSplash0 = j.splashesPerSecond; jwDrip0 = j.dripsPerSecond; } force = true; }
            if (jw && force)
            {
                jw.dropsAtFull = Mathf.RoundToInt(jwDrops0 * rain); jw.splashesPerSecond = jwSplash0 * rain; jw.dripsPerSecond = jwDrip0 * rain;
                jw.midDrops = Mathf.RoundToInt(jwMid0 * rain); jw.nearDrops = Mathf.RoundToInt(jwNear0 * rain);   // the three streak layers keep their HIGH proportions (far 1500 : mid 850 : near 250)
            }
        }

        /// <summary>Every number back to what it was. Safe to call twice.</summary>
        public void Restore()
        {
            OzLite.Runs++;
            if (restored) return; restored = true;
            if (staticsKept)
            {
                NightShadows.NearbySlots = nearby0; NightShadows.HighTierCount = sharp0;
                ShadowDistanceByHeight.On = reachWasOn;
                staticsKept = false;
            }
            if (urp) { urp.shadowDistance = urpReach0; urp.shadowCascadeCount = urpSteps0; }
            if (nl) { nl.maxRealLights = nlReal0; nl.maxShadowed = nlShadow0; nl.halos = nlHalos0; nl.beams = nlBeams0; }
            if (vl) { vl.maxRealLights = vlReal0; vl.maxEffectVehicles = vlCones0; }
            if (wx) { wx.dropsAtFull = wxDrops0; wx.splashesPerSecond = wxSplash0; }
            if (jw) { jw.dropsAtFull = jwDrops0; jw.midDrops = jwMid0; jw.nearDrops = jwNear0; jw.splashesPerSecond = jwSplash0; jw.dripsPerSecond = jwDrip0; }
            urp = null; nl = null; vl = null; wx = null; jw = null; reach = 0f;
        }

        void OnDestroy() { if (!restored) Restore(); }

        void LateUpdate()
        {
            OzLite.Runs++;
            if (restored || !OzLite.Active) return;                                             // the preset went back to HIGH in this frame: this object goes at the frame's end
            if (Time.unscaledTime >= nextLook) { nextLook = Time.unscaledTime + 0.5f; Refresh(false); }
            var cam = Camera.main;
            if (!reachWasOn || !urp || !cam || ZGame.I == null) return;
            // the farthest ground in view, exactly as ShadowDistanceByHeight looks for it: the viewport's top edge on the ground plane
            float far = 0f; var plane = new Plane(Vector3.up, Vector3.zero);
            for (int i = 0; i < Top.Length; i++)
            {
                var ray = cam.ViewportPointToRay(Top[i]);
                far = Mathf.Max(far, plane.Raycast(ray, out float d) && d > 0f ? d : cam.farClipPlane * 0.3f);
            }
            reach = OzLiteTable.Reach(OzLite.Level, OzLite.Cut(OzLiteTable.CutSunReach), far, reach,
                                      ShadowDistanceByHeight.Min, ShadowDistanceByHeight.Max, ShadowDistanceByHeight.Margin, ShadowDistanceByHeight.Step);
            if (Mathf.Abs(urp.shadowDistance - reach) > 0.5f) urp.shadowDistance = reach;
        }
    }
}
