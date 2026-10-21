using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using OpenZ.Sim;
using UnityEditor;
using UnityEngine;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// Version 1.1 kit KC5 checks (menu OpenZ/V1.1/KC5 ...).
    /// <list type="bullet">
    /// <item>Count (not in Play, headless): DV110 a new veteran heals 15 % (rule on / off) and DV111 the fort's roof sniper (rule on / off).</item>
    /// <item>Blast deaths timing (Play): four test blasts, 3 s apart, each on the enemy robot group nearest the view centre, made by a
    /// live enemy unit of that group (so the view's blast-death timing applies exactly as for a real shell): the sizes of a grenade
    /// (36 px), a tank shell / rocket (40 px), a heavy shell (50 px) and an MLRS rocket (80 px). The death trace (OzDeathTrace) is armed
    /// set BlastWave.ImpactFrame.</item>
    /// <item>Fort cannon landing (Play): tosses the fort cannon nearest the view centre (FortGunView.TestToss, view only) and when it
    /// has settled prints the landing's numbers (OzHeavyLanding): sound, dust, bounce, skid, the fit by its real vertices, the gap of
    /// its lowest vertex to the exact ground under it, the height step on the settle frame.</item>
    /// </list>
    /// Test tools only: the blasts go through the sim's own missile path; nothing is saved.
    /// </summary>
    public static class OzKC5Check
    {
        const string M = "OpenZ/V1.1/KC5 ";

        [MenuItem(M + "Count Veteran Heal + Fort Sniper (DV110, DV111)")]
        static void Count() => OzV11Kit.RunMenu("KC5 veteran heal + fort sniper", Report);

        public static string Report()
        {
            var sb = new StringBuilder(); int fails = 0;
            void Line(bool ok, string what) { sb.AppendLine(OzV11Kit.Verdict(ok, what)); if (!ok) fails++; }
            string a = OpenZParity.Kc5HealCase(true, out bool pa); Line(pa, "DV110 rule on: " + a);
            string b = OpenZParity.Kc5HealCase(false, out bool pb); Line(pb, "DV110 rule off: " + b);
            var set = ZSettings.Parse(OzV11Kit.SettingsText); var sn = set.Unit("sniper"); var gr = set.Unit("grunt");
            string c = OpenZParity.Kc5SniperCase(true, out string tc, out int rc, out int dc, out double gc, out int scn);
            Line(tc == "sniper" && rc == sn.attack_radius && dc == sn.Damage && Math.Abs(gc - sn.attack_speed) < 0.011 && scn >= 10, "DV111 rule on: " + c);
            string d = OpenZParity.Kc5SniperCase(false, out string td, out int rd, out int dd, out double gd, out int sd);
            Line(td == "grunt" && rd == gr.attack_radius && dd == gr.Damage * ZWorld.CrewRifleMul && (sd == 0 || Math.Abs(gd - gr.attack_speed) < 0.011), "DV111 rule off: " + d + (sd == 0 ? " (the tank stands beyond his 120 px)" : ""));
            sb.AppendLine(fails == 0 ? "KC5 COUNT PASS" : "KC5 COUNT FAIL " + fails);
            return sb.ToString();
        }

        // ------------------------------------------------------------------ Play: blast deaths timing (item 169)
        [MenuItem(M + "Blast Deaths: BEFORE (old shock front, Play)")]
        static void Before() { BlastWave.ImpactFrame = false; Debug.Log("[OpenZ KC5] BlastWave.ImpactFrame = false (the old shock front: 16 m/s + 0-0.09 s per robot)"); }

        [MenuItem(M + "Blast Deaths: AFTER (on the impact frame, Play)")]
        static void After() { BlastWave.ImpactFrame = true; Debug.Log("[OpenZ KC5] BlastWave.ImpactFrame = true (thrown on the explosion's own frame)"); }

        static readonly int[] Radii = { 36, 40, 50, 80 };
        static readonly string[] RadiusNames = { "grenade 36 px", "tank shell / rocket 40 px", "heavy shell 50 px", "MLRS rocket 80 px" };
        static int step; static float nextAt; static int t0Delayed, t0Impact;

        [MenuItem(M + "Blast Deaths Timing x4 (Play)")]
        static void Timing()
        {
            var g = ZGame.I;
            if (!Application.isPlaying || !g || g.World == null) { Debug.LogWarning("[OpenZ KC5] enter Play first, the camera on an enemy group"); return; }
            OzDeathTrace.Arm(16f);
            step = 0; nextAt = Time.realtimeSinceStartup; t0Delayed = BlastWave.Delayed; t0Impact = BlastWave.OnImpactFrame;
            EditorApplication.update -= Tick; EditorApplication.update += Tick;
            Debug.Log($"[OpenZ KC5] blast deaths timing: 4 test blasts, ImpactFrame {BlastWave.ImpactFrame}; the death trace writes work/records/qc/death_trace_*.md in 16 s");
        }

        static void Tick()
        {
            var g = ZGame.I;
            if (!Application.isPlaying || !g || g.World == null) { EditorApplication.update -= Tick; return; }
            if (Time.realtimeSinceStartup < nextAt) return;
            if (step >= Radii.Length)
            {
                EditorApplication.update -= Tick;
                Debug.Log($"[OpenZ KC5] blast deaths: thrown on the impact frame {BlastWave.OnImpactFrame - t0Impact}, held for the front / a ripple rocket {BlastWave.Delayed - t0Delayed}, " +
                          $"largest hold this session {BlastWave.MaxSeen:0.00} s (ImpactFrame {BlastWave.ImpactFrame}); per robot: the death trace .md");
                return;
            }
            var cam = Camera.main; var c = cam && g.GroundPoint(cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f)), out var gp) ? gp : Vector3.zero;
            var victim = g.World.Objects.Where(o => o.Kind == ZKind.Robot && !o.Destroyed && o.KillAt < 0 && o.Owner != g.player && o.Owner != Team.Neutral && !o.IsFortDefender)
                .OrderBy(o => Vector3.Distance(g.CenterWorld(o), c)).FirstOrDefault();
            if (victim == null) { Debug.LogWarning("[OpenZ KC5] no enemy robot left"); EditorApplication.update -= Tick; return; }
            var killer = g.World.Objects.FirstOrDefault(o => o.IsUnit && !o.Destroyed && o.KillAt < 0 && o.Owner == g.player);
            int r = Radii[step];
            int dx = (victim.Leader ?? victim).Minions.Count > 0 ? 6 : 0;
            g.World.AddMissileForTest(new ZMissile { x = victim.CenterX + dx, y = victim.CenterY, damage = 4000, radius = r, team = killer != null ? killer.Owner : Team.Neutral, attackerId = killer != null ? killer.Id : -1, targetId = victim.Id, explodeTime = g.World.Time + 0.02 });
            Debug.Log($"[OpenZ KC5] test blast {step + 1}/4: {RadiusNames[step]} on {victim.Type} #{victim.Id} (killer {killer?.Type} #{killer?.Id})");
            step++; nextAt = Time.realtimeSinceStartup + 3f;
        }

        // ------------------------------------------------------------------ Play: the fort cannon's landing (item 172)
        static float landCheckAt; static int land0, sound0, dust0, settled0;

        [MenuItem(M + "Toss The Nearest Fort Cannon (Play)")]
        static void Toss()
        {
            var g = ZGame.I;
            if (!Application.isPlaying || !g) { Debug.LogWarning("[OpenZ KC5] enter Play first, the camera on a fort"); return; }
            var cam = Camera.main; var c = cam && g.GroundPoint(cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f)), out var gp) ? gp : Vector3.zero;
            var gun = UnityEngine.Object.FindObjectsByType<FortGunView>(FindObjectsSortMode.None).OrderBy(v => Vector3.Distance(v.transform.position, c)).FirstOrDefault();
            if (!gun) { Debug.LogWarning("[OpenZ KC5] no fort cannon in the scene"); return; }
            land0 = OzHeavyLanding.Landings; sound0 = OzHeavyLanding.Sounds; dust0 = OzHeavyLanding.DustBursts; settled0 = OzHeavyLanding.Settled;
            gun.TestToss();
            landCheckAt = Time.realtimeSinceStartup + 20f;
            EditorApplication.update -= LandTick; EditorApplication.update += LandTick;
            Debug.Log("[OpenZ KC5] fort cannon tossed (view only); its landing numbers when it has settled (about 3 s; a pause holds it)");
        }

        static void LandTick()
        {
            if (!Application.isPlaying) { EditorApplication.update -= LandTick; return; }
            bool settled = OzHeavyLanding.Settled > settled0 && OzHeavyLanding.LastKind == "fort cannon";
            if (!settled && Time.realtimeSinceStartup < landCheckAt) return;
            EditorApplication.update -= LandTick;
            var sb = new StringBuilder("[OpenZ KC5] fort cannon landing\n");
            bool landed = OzHeavyLanding.Landings > land0 && OzHeavyLanding.LastKind == "fort cannon";
            sb.AppendLine(OzV11Kit.Verdict(landed, $"the heavy landing ran (landings +{OzHeavyLanding.Landings - land0}, touch-down speed {OzHeavyLanding.LastSpeed:0.0} m/s)"));
            sb.AppendLine(OzV11Kit.Verdict(OzHeavyLanding.Sounds > sound0, $"the heavy concrete landing sound played on its landing frame (+{OzHeavyLanding.Sounds - sound0})"));
            sb.AppendLine(OzV11Kit.Verdict(OzHeavyLanding.DustBursts > dust0, $"a dust burst on impact (+{OzHeavyLanding.DustBursts - dust0})"));
            sb.AppendLine(OzV11Kit.Verdict(OzHeavyLanding.LastBounce <= 0.25f && OzHeavyLanding.LastSkid <= 1.25f, $"heavy: one low bounce {OzHeavyLanding.LastBounce:0.00} m, skid {OzHeavyLanding.LastSkid:0.00} m"));
            bool lies = settled && OzHeavyLanding.LastContactVertex                                     // its resting contact is a real vertex (a part fitted by box corners, if any, is not the lowest)
                        && Mathf.Abs(OzHeavyLanding.LastGap + OzHeavyLanding.Sink) <= 0.03f && OzHeavyLanding.LastPop <= 0.02f;
            sb.AppendLine(OzV11Kit.Verdict(lies, $"it lies ON the ground, fitted by its real mesh vertices (fit: {OzHeavyLanding.LastFit}; resting on a {(OzHeavyLanding.LastContactVertex ? "vertex" : "box corner")}): " +
                $"its lowest point {OzHeavyLanding.LastGap * 100f:0.0} cm from the ground under it by the exact ground probe (wanted -5 +- 3 cm), height step on the settle frame {OzHeavyLanding.LastPop * 100f:0.0} cm (<= 2), " +
                $"settled in {OzHeavyLanding.LastSettle:0.00} s{(settled ? "" : " (NOT settled in 20 s)")}; for comparison the renderer box's bottom sat {OzHeavyLanding.LastBoxGap * 100f:0} cm under its lowest vertex (what the box fit hovered)"));
            Debug.Log(sb.ToString());
        }
    }
}
