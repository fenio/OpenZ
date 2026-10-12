using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OpenZ.Sim;
using UnityEditor;
using UnityEngine;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// deaths_mech_v1 Play checks (test tools: they kill units through the sim's own death path, like GateBattery; never used in a
    /// game). Every test arms the death frame trace (OzDeathTrace) and can run in slow motion with a frame dump for the video.
    /// </summary>
    public static class OpenZDeathsMenu
    {
        const string M = "OpenZ/Deaths/";

        static bool Ready(out ZGame g)
        {
            g = ZGame.I;
            if (!Application.isPlaying || !g || g.World == null) { Debug.LogWarning("[OpenZ] deaths: enter Play on L02 or L17 first"); return false; }
            return true;
        }

        static Vector3 Focus(ZGame g)
        {
            var cam = Camera.main; if (!cam) return Vector3.zero;
            return g.GroundPoint(cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f)), out var p) ? p : cam.transform.position;
        }

        static List<ZObject> Near(ZGame g, System.Func<ZObject, bool> f, float r, int max)
        {
            var c = Focus(g);
            return g.World.Objects.Where(o => !o.Destroyed && o.KillAt < 0 && f(o))
                .Select(o => (o, d: Vector3.Distance(g.CenterWorld(o), c))).Where(x => x.d <= r).OrderBy(x => x.d).Take(max).Select(x => x.o).ToList();
        }

        static void Kill(ZGame g, ZObject o, int killer = -1)
        {
            o.LastFireHit = -100; o.LastMissileHit = -100;
            o.SetHealth(0); g.World.UpdateObjectHealth(o, killer);
        }

        [MenuItem(M + "Trace deaths 20 s (Play)")]
        static void Trace() { if (Ready(out _)) OzDeathTrace.Arm(20f); }

        [MenuItem(M + "Test: pop deaths x6 near the view centre (Play, slow)")]
        static void Pops()
        {
            if (!Ready(out var g)) return;
            OzDeathTrace.Arm(10f); Slow(6f, "pops");
            var list = Near(g, o => o.Kind == ZKind.Robot, 40f, 6);
            foreach (var o in list)
            {
                var enemy = g.World.Objects.FirstOrDefault(x => x.IsUnit && !x.Destroyed && x.Owner != o.Owner && x.Owner != Team.Neutral);
                Kill(g, o, enemy != null ? enemy.Id : -1);                                              // direct kill: Zod -> pop
            }
            Debug.Log($"[OpenZ] deaths: {list.Count} robots killed by a direct hit (Zod: pop, variant = hash % 4)");
        }

        [MenuItem(M + "Test: heavy blast at the view centre (Play, slow)")]
        static void Blast()
        {
            if (!Ready(out var g)) return;
            var c = Focus(g); var px = ZGame.WorldToPx(c);
            HitReactions.ForceHeavyUntil = Time.time + 3f;
            OzDeathTrace.Arm(12f); Slow(8f, "blast");
            g.World.AddMissileForTest(new ZMissile { x = px.x, y = px.y, damage = 3000, radius = 96, team = Team.Neutral, attackerId = -1, explodeTime = g.World.Time + 0.05 });
            Debug.Log($"[OpenZ] deaths: test blast (3000 HP, r 96 px = 12 m) at {c:F1}: close robots die (ragdoll + Zod flight), the rest are knocked down / stagger");
        }

        [MenuItem(M + "Test: kill vehicles + cannons near the view centre (Play, slow)")]
        static void Vehicles()
        {
            if (!Ready(out var g)) return;
            OzDeathTrace.Arm(12f); Slow(10f, "wrecks");
            var list = Near(g, o => o.Kind == ZKind.Vehicle || o.Kind == ZKind.Cannon, 45f, 8);
            foreach (var o in list) Kill(g, o);
            Debug.Log($"[OpenZ] deaths: {list.Count} vehicles / cannons killed (wreck outcome mode {WreckFx.Force}: 0 seeded, 1 always break / flip, 2 plain wrecks)");
        }

        [MenuItem(M + "Test: blow up buildings + bridges near the view, audit after 14 s (Play)")]
        static void Buildings()
        {
            if (!Ready(out var g)) return;
            var list = Near(g, o => (o.Kind == ZKind.Building && !o.IsFort) || o.IsBridge, 70f, 6);
            foreach (var o in list) Kill(g, o);
            DebrisAudit.Schedule(14f, $"{list.Count} buildings / bridges");
            Debug.Log($"[OpenZ] deaths: {list.Count} buildings / bridges destroyed; debris audit in 14 s (work/records/qc/debris_audit_*.md)");
        }

        [MenuItem(M + "Test: blow up the nearest fort, audit after 20 s (Play, ENDS THE MATCH)")]
        static void Fort()
        {
            if (!Ready(out var g)) return;
            var f = Near(g, o => o.IsFort, 200f, 1).FirstOrDefault();
            if (f == null) { Debug.LogWarning("[OpenZ] deaths: no fort near the view"); return; }
            Kill(g, f); DebrisAudit.Schedule(20f, "fort");
        }

        [MenuItem(M + "Wreck outcomes: seeded / always break+flip / never (cycle)")]
        static void CycleForce() { WreckFx.Force = (WreckFx.Force + 1) % 3; Debug.Log($"[OpenZ] wreck outcomes: {(WreckFx.Force == 0 ? "seeded (1 in 3)" : WreckFx.Force == 1 ? "always break / flip" : "never (plain burnt wrecks)")}"); }

        [MenuItem(M + "Robot death mix: Zod exact / Shares (toggle)")]
        static void Mix()
        {
            RobotDeathRule.Mix = RobotDeathRule.Mix == RobotDeathRule.DeathMix.Zod ? RobotDeathRule.DeathMix.Shares : RobotDeathRule.DeathMix.Zod;
            Debug.Log($"[OpenZ] robot death mix: {RobotDeathRule.Mix} ({(RobotDeathRule.Mix == RobotDeathRule.DeathMix.Zod ? "missile -> ragdoll flight, flame -> melt, every other kill -> pop (zrobot.cpp:369-378)" : "DV20: a share of each weapon class pops")})");
        }

        [MenuItem(M + "Slow motion 0.25x 8 s + frame dump (Play)")]
        static void SlowMenu() { if (Ready(out _)) Slow(8f, "manual"); }

        static void Slow(float realSeconds, string label)
        {
            var host = new GameObject("OZ_DeathsSlowMo") { hideFlags = HideFlags.DontSave }.AddComponent<SlowMo>();
            host.StartCoroutine(host.Run(realSeconds, label));
        }

        /// <summary>0.25x game time (the sim slows with it: still deterministic) and every 3rd frame saved as a half-size JPG into
        /// </summary>
        class SlowMo : MonoBehaviour
        {
            public IEnumerator Run(float realSeconds, string label)
            {
                float old = Time.timeScale; Time.timeScale = 0.25f;
                string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "../../../../work/records/milestones", $"deaths_{label}_{System.DateTime.Now:yyyyMMdd_HHmmss}"));
                Directory.CreateDirectory(dir);
                float end = Time.unscaledTime + realSeconds; int f = 0, saved = 0;
                while (Time.unscaledTime < end && saved < 150)
                {
                    yield return new WaitForEndOfFrame();
                    if (f++ % 3 != 0) continue;
                    ProofCapture.SaveJpg(Path.Combine(dir, $"f_{saved++:0000}.jpg"), Screen.width / 2, 88);
                }
                Time.timeScale = old;
                Debug.Log($"[OpenZ] slow motion done: {saved} frames in {dir}");
                Destroy(gameObject);
            }
        }
    }
}
