using UnityEditor;
using UnityEngine;

namespace OpenZ.EditorTools
{
    /// <summary>weather_l17_v1: OpenZ/Weather menu for the Play proofs (L17 in Play): force a shower (jumps the clock to night
    /// when needed), fire single flashes, stop the shower, log the status line. View only: nothing here touches the sim.</summary>
    public static class OzWeatherMenu
    {
        static OzWeather W()
        {
            if (!Application.isPlaying) { Debug.LogWarning("[OzWeather] Play L17 first"); return null; }
            if (!OzWeather.I) Debug.LogWarning("[OzWeather] no weather in this level (only L17 has it; ZGame adds it)");
            return OzWeather.I;
        }

        [MenuItem("OpenZ/Weather/Force Storm 70 s (Play, L17)")] static void Storm70() { var w = W(); if (w) w.ForceStorm(70f); }
        [MenuItem("OpenZ/Weather/Force Storm 40 s (Play, L17)")] static void Storm40() { var w = W(); if (w) w.ForceStorm(40f); }
        [MenuItem("OpenZ/Weather/Flash Close")] static void FlashClose() { var w = W(); if (w) w.ForceFlash(OzStormPlan.Close); }
        [MenuItem("OpenZ/Weather/Flash Mid")] static void FlashMid() { var w = W(); if (w) w.ForceFlash(OzStormPlan.Mid); }
        [MenuItem("OpenZ/Weather/Flash Far")] static void FlashFar() { var w = W(); if (w) w.ForceFlash(OzStormPlan.Far); }
        [MenuItem("OpenZ/Weather/Stop Storm (3 s fade)")] static void Stop() { var w = W(); if (w) w.StopStorm(3f); }
        [MenuItem("OpenZ/Weather/Log Status")] static void Status() { var w = W(); if (w) Debug.Log(w.Status()); }
        [MenuItem("OpenZ/Weather/Toggle WEATHER Option")] static void Toggle() { OzWeather.Enabled = !OzWeather.Enabled; Debug.Log($"[OzWeather] option {(OzWeather.Enabled ? "ON" : "OFF")}"); }
        [MenuItem("OpenZ/Weather/Level 16: Force Rain Spell 60 s (Play, L16)")] static void JungleSpell() { if (!OzJungleWeather.Force(60f)) Debug.LogWarning("[OzJungleWeather] Play level 16 first"); }
        [MenuItem("OpenZ/Weather/Level 16: Stop Rain Spell (3 s fade)")] static void JungleStop() { if (OzJungleWeather.I) OzJungleWeather.I.StopSpell(3f); }
        [MenuItem("OpenZ/Weather/Level 16: Log Status")] static void JungleStatus() { if (OzJungleWeather.I) Debug.Log(OzJungleWeather.I.Status() + "\n" + OzFloraWind.Status()); else Debug.LogWarning("[OzJungleWeather] Play level 16 first"); }
    }
}
