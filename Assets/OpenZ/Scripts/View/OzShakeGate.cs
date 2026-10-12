using System.Collections.Generic;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// ONE gate for both shake paths (RtsCamera.Shake and OpenZFxShake.Impact /
    /// Rumble). An event kicks only when its centre is in the camera frustum and no kick happened in the last Cooldown s, unless it
    /// is clearly stronger (OverrideRatio x the last peak). Peaks are in screen px at a 1080 px reference height. Counts every
    /// request (kick / cooldown / off screen) for the shake log; LogToConsole prints one line per request.
    /// </summary>
    /// <summary>
    /// The CALM SCREEN setting (OzMotion.Reduced,
    /// PlayerPrefs oz_reduced_motion, default 0 = OFF). When ON: no camera shake anywhere (RtsCamera, OpenZFxShake impacts / rumbles,
    /// the flyby kick), lightning = a soft slow glow without the bolt, the nuke's white-out and exposure flash a slow soft fade, no hit
    /// flash, steady neon / lamps / loose headlights (no stutter), FX flash lights softer and never shorter than 0.15 s (no strobing),
    /// transmission static and the Zod HQ kicks toned down (ZodComms / ZodStage / OpenZHud). OFF = everything as before.
    /// Every suppressed effect is counted per kind (Log) for the proof.</summary>
    public static class OzCalm
    {
        public static bool On => OzMotion.Reduced;
        public static readonly Dictionary<string, int> Suppressed = new Dictionary<string, int>();
        public static void Note(string kind) { Suppressed.TryGetValue(kind, out int n); Suppressed[kind] = n + 1; }
        public static string Log() { var sb = new System.Text.StringBuilder(); foreach (var kv in Suppressed) sb.Append(kv.Key).Append(' ').Append(kv.Value).Append(", "); return sb.Length > 0 ? sb.ToString().TrimEnd(',', ' ') : "none"; }
        public static void Reset() => Suppressed.Clear();
    }

    public static class OzShakeGate
    {
        /// <summary>The player's switches: SCREEN SHAKE on and CALM SCREEN off (both shake paths ask this; was only RtsCamera via the pause menu).</summary>
        public static bool SettingOn => PlayerPrefs.GetInt("oz_shake", 1) == 1 && !OzMotion.Reduced;
        public const float Cooldown = 2.5f, OverrideRatio = 1.5f;
        public static int Kicks, Blocked, OffScreen;
        public static bool LogToConsole;
        public static readonly List<string> Log = new List<string>();
        static float lastTime = -100f, lastPx;

        public static bool OnScreen(Vector3 at)
        {
            var c = Camera.main; if (!c) return false;
            var vp = c.WorldToViewportPoint(at);
            return vp.z > c.nearClipPlane && vp.z < c.farClipPlane && vp.x > 0f && vp.x < 1f && vp.y > 0f && vp.y < 1f;
        }

        /// <summary>May this event shake the camera? px = its peak (1080 px reference). always = not held back by the cooldown
        /// (a building or a bridge blowing up); the two switches and the on-screen test still apply.</summary>
        public static bool Allow(string src, Vector3 at, float px, bool checkScreen = true, bool always = false)
        {
            float now = Time.unscaledTime;
            string verdict; bool ok = false;
            if (!SettingOn) { Blocked++; verdict = OzMotion.Reduced ? "calm screen" : "shake off"; if (OzMotion.Reduced) OzCalm.Note("shake"); }
            else if (checkScreen && !OnScreen(at)) { OffScreen++; verdict = "off-screen"; }
            else if (!always && now - lastTime < Cooldown && px < lastPx * OverrideRatio) { Blocked++; verdict = "cooldown"; }
            else { ok = true; Kicks++; lastTime = now; lastPx = px; verdict = "KICK"; }
            string line = string.Format("[shake] t={0:0.00} src={1} px={2:0.0} on={3} -> {4}", now, src, px, checkScreen ? (verdict == "off-screen" ? "no" : "yes") : "ui", verdict);
            if (Log.Count >= 400) Log.RemoveAt(0);
            Log.Add(line);
            if (LogToConsole) Debug.Log(line);
            return ok;
        }

        public static void ResetCounts() { Kicks = Blocked = OffScreen = 0; Log.Clear(); }
    }
}
