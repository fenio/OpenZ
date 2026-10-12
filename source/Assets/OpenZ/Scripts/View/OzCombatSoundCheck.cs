using System.Collections;
using System.Collections.Generic;
using System.Text;
using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// In Play, near the camera focus: a small red and blue cast is spawned, then each combat event goes through
    /// the REAL path (sim events -> CombatView / LegacyMatch / AudioDirector -> CombatAudio): the attacker's shot event, the
    /// missile that explodes in the sim, the unit / building death. For each event it measures the master's momentary loudness
    /// (OzLoudnessMeter, after the mix = what the player hears) against the quiet floor measured just before, and lists the sound
    /// log lines of that window (played / dropped and why). Pause-safe: time only counts while the editor is not paused.
    /// </summary>
    public class OzCombatSoundCheck : MonoBehaviour
    {
        public static OzCombatSoundCheck Run() => ZGame.I ? ZGame.I.gameObject.AddComponent<OzCombatSoundCheck>() : null;
        public const float QuietBedDbfs = -25f, PassAboveFloorDb = 3f, PassMinLufs = -45f, PassMinDbfs = -42f;
        // meter's callback delivered nothing and every case read -120)
        readonly float[] bufL = new float[1024], bufR = new float[1024];
        float OutRmsDb() { AudioListener.GetOutputData(bufL, 0); AudioListener.GetOutputData(bufR, 1); double s = 0; for (int i = 0; i < bufL.Length; i++) s += bufL[i] * bufL[i] + bufR[i] * bufR[i]; float r = Mathf.Sqrt((float)(s / (2 * bufL.Length))); return r > 1e-6f ? 20f * Mathf.Log10(r) : -120f; }
        IEnumerator Measure(float seconds, System.Action<float, float> done)
        {   // median and max of the per-frame master RMS (~21 ms of audio each at 48 kHz) over `seconds` of unpaused time
            var v = new List<float>(); float t = 0;
            while (t < seconds) { if (!IsPaused()) { t += Time.unscaledDeltaTime; v.Add(OutRmsDb()); } yield return null; }
            if (v.Count == 0) { done(-120f, -120f); yield break; }
            v.Sort(); done(v[v.Count / 2], v[v.Count - 1]);
        }
        ZGame g; ZWorld W;
        readonly StringBuilder report = new StringBuilder();
        int pass, fail;

        void Start() { StartCoroutine(Main()); }

        IEnumerator Wait(float s) { float t = 0; while (t < s) { if (!Application.isEditor || !IsPaused()) t += Time.unscaledDeltaTime; yield return null; } }
        static bool IsPaused()
        {
#if UNITY_EDITOR
            return UnityEditor.EditorApplication.isPaused;
#else
            return false;
#endif
        }

        ZObject Mk(string cat, string type, int tx, int ty, Team t) { var o = W.CreateObject(cat, type, tx * 16, ty * 16, t); g.SpawnUnitView(o); Passive(o); return o; }
        /// <summary>
        /// test tank was already dead when its death case came): no damage, no attack radius, for the unit and its minions.</summary>
        static void Passive(ZObject o) { o.Damage = 0; o.AttackRadius = 0; foreach (var m in o.Minions) { m.Damage = 0; m.AttackRadius = 0; } }

        IEnumerator Main()
        {
            g = ZGame.I; W = g.World;
            var meter = OzLoudnessMeter.I;
            var rts = FindFirstObjectByType<RtsCamera>();
            var focus = rts ? rts.focus : Vector3.zero;
            int fx = Mathf.RoundToInt(focus.x * 8 / 16f), fy = Mathf.RoundToInt(-focus.z * 8 / 16f);
            if (rts) { rts.SetHeightImmediate(40f); rts.Place(); }
            report.AppendLine($"# Combat sound check, {g.level}, {System.DateTime.Now:yyyy-MM-dd HH:mm}");
            report.AppendLine($"Focus tile ({fx},{fy}); camera 40 m; PASS = master RMS max (per frame, ~21 ms) >= floor + {PassAboveFloorDb} dB and >= {PassMinDbfs} dBFS (AudioListener.GetOutputData).");
            report.AppendLine(meter ? "" : "**No OzLoudnessMeter on the listener: levels unknown, only the log.**");
            report.AppendLine();

            // cast: attackers west of the focus, targets east (all near the screen centre)
            var grunt = W.CreateRobotGroup("grunt", (fx - 4) * 16, fy * 16, Team.Red); g.SpawnUnitView(grunt); foreach (var m in grunt.Minions) g.SpawnUnitView(m); Passive(grunt);
            var pyro = Mk("robot", "pyro", fx - 4, fy + 2, Team.Red);
            var laser = Mk("robot", "laser", fx - 4, fy - 2, Team.Red);
            var tough = Mk("robot", "tough", fx - 5, fy + 1, Team.Red);
            var light = Mk("vehicle", "light", fx - 7, fy - 3, Team.Red);
            var heavy = Mk("vehicle", "heavy", fx - 7, fy + 3, Team.Red);
            var mlrs = Mk("vehicle", "missile_launcher", fx - 9, fy, Team.Red);
            var how = Mk("cannon", "howitzer", fx - 9, fy - 4, Team.Red);
            var mc = Mk("cannon", "missile_cannon", fx - 9, fy + 4, Team.Red);
            var tLight = Mk("vehicle", "light", fx + 4, fy - 2, Team.Blue);
            var tGrunt = W.CreateRobotGroup("grunt", (fx + 4) * 16, (fy + 2) * 16, Team.Blue); g.SpawnUnitView(tGrunt); foreach (var m in tGrunt.Minions) g.SpawnUnitView(m); Passive(tGrunt);
            var tGat = Mk("cannon", "gatling", fx + 5, fy + 5, Team.Blue);
            // already destroyed the blue gatling, its death case measured nothing of its own)
            bool fortGun = ZWorld.FortGunDeviation; ZWorld.FortGunDeviation = false;
            yield return Wait(2.5f);                                                                        // views settle, spawn sounds pass

            yield return Case("bolt (grunt rifle)", () => W.Emit(ZEventType.Hitscan, grunt, tGrunt, tGrunt.CenterX, tGrunt.CenterY, 0, 0, true), 1.2f);
            yield return Case("flame (pyro)", () => W.Emit(ZEventType.Hitscan, pyro, tGrunt, tGrunt.CenterX, tGrunt.CenterY, 0, 0, true), 1.2f);
            yield return Case("laser (fire zip + impact zap)", () => W.Emit(ZEventType.Hitscan, laser, tLight, tLight.CenterX, tLight.CenterY, 0, 0, true), 1.2f);
            yield return Case("light tank shell (launch + blast)", () => Missile(light, tLight, 40, false), 2.2f);
            yield return Case("heavy tank shell (launch + blast)", () => Missile(heavy, tLight, 48, false), 2.2f);
            yield return Case("bazooka rocket (tough)", () => Missile(tough, tLight, 40, false), 2.2f);
            yield return Case("MLRS rocket (launch + flight + blast)", () => Missile(mlrs, tGrunt, 56, false), 2.6f);
            yield return Case("howitzer shell", () => Missile(how, tGrunt, 40, false), 2.6f);
            yield return Case("missile cannon rocket", () => Missile(mc, tLight, 40, false), 2.4f);
            yield return Case("grenade (throw whoosh + bounce + blast)", () => Missile(grunt, tGrunt, 24, true), 2.4f);
            if (tGrunt.Destroyed) { tGrunt = W.CreateRobotGroup("grunt", (fx + 4) * 16, (fy + 2) * 16, Team.Blue); g.SpawnUnitView(tGrunt); foreach (var m in tGrunt.Minions) g.SpawnUnitView(m); Passive(tGrunt); report.AppendLine("(blue grunt squad was already dead: respawned)"); yield return Wait(1.5f); }
            // by the next step before CombatView read its Death event, so no death sound was ever requested)
            ZObject victim = null;
            yield return Case("robot death", () => { ZObject m = tGrunt; foreach (var x in tGrunt.Minions) if (!x.Destroyed) { m = x; break; } victim = m;
                W.AddMissileForTest(new ZMissile { team = grunt.Owner, attackerId = -1, targetId = m.Id, damage = 9999, radius = 4, x = m.CenterX, y = m.CenterY, explodeTime = W.Time + 0.05 }); }, 2.0f);
            bool deathHeard = false; foreach (var e in OzSoundLog.Since(Time.unscaledTime - 3.5f)) if ((e.key == "death" || (e.key != null && (e.key.StartsWith("death_robot") || e.key.StartsWith("death_cry_")))) && e.outcome.StartsWith("PLAY")) deathHeard = true;   // death_voices_v1
            report.AppendLine($"robot death verdict: target {(victim != null && victim.Destroyed ? "died" : "SURVIVED (case inconclusive)")}, death sound {(deathHeard ? "played" : "NOT played")}"); report.AppendLine();
            if (tGat.Destroyed) { tGat = Mk("cannon", "gatling", fx + 5, fy + 5, Team.Blue); report.AppendLine("(blue gatling was already dead: respawned)"); yield return Wait(1.5f); }
            yield return Case("cannon death (gatling)", () => Kill(tGat), 2.5f);
            if (tLight.Destroyed) { tLight = Mk("vehicle", "light", fx + 4, fy - 2, Team.Blue); report.AppendLine("(blue light tank was already dead: respawned)"); yield return Wait(1.5f); }
            yield return Case("vehicle death + turret toss (whoosh, crash)", () => Kill(tLight), 4.0f);
            var rock = Nearest(o => o.Type == "rock", focus, 60f);
            if (rock != null) yield return Case("rock destroyed (crash + chunks)", () => Kill(rock), 3.0f, rock);
            var bld = Nearest(o => o.Kind == ZKind.Building && !o.IsFort && !o.IsBridge && o.Owner == Team.Neutral, focus, 80f);
            if (bld != null) yield return Case($"building collapse ({bld.Type})", () => Kill(bld), 4.0f, bld);

            yield return PlacedCase();                                                                      // placepop_v1
            ZWorld.FortGunDeviation = fortGun;
            report.Insert(0, $"RESULT: {(fail == 0 ? "PASS" : "FAIL")} ({pass} pass, {fail} fail)\n\n");
            var dir = System.IO.Path.Combine(System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "../../../..")), "work", "records", "qc");
            System.IO.Directory.CreateDirectory(dir);
            var path = System.IO.Path.Combine(dir, $"combat_sound_check_{g.level}_{System.DateTime.Now:HHmm}.md");
            System.IO.File.WriteAllText(path, report.ToString());
            Debug.Log($"[OzCombatSoundCheck] {(fail == 0 ? "PASS" : "FAIL")} {pass}/{pass + fail} -> {path}");
            Destroy(this);
        }

        /// <summary>placepop_v1: a stored gatling placed from the player's own building through the real path (ZWorld.PlaceCannon ->
        /// player's zones nearest the camera focus; the camera looks at it first (world sounds off screen are dropped by design).</summary>
        IEnumerator PlacedCase()
        {
            var rts = FindFirstObjectByType<RtsCamera>(); var focus = rts ? rts.focus : Vector3.zero;
            ZObject host = null; int bx = -1, by = -1; float best = float.MaxValue;
            foreach (var o in W.Objects)
            {
                if (o.Destroyed || o.Kind != ZKind.Building || o.Owner != g.player || o.IsBridge) continue;
                var z = W.ZoneOf(o); if (z == null) continue;
                for (int ty = z.Y / 16; ty <= (z.Y + z.H) / 16; ty++)
                    for (int tx = z.X / 16; tx <= (z.X + z.W) / 16; tx++)
                    {
                        var d = ZGame.PxToWorld(tx * 16 + 16, ty * 16 + 16) - focus; d.y = 0f;
                        if (d.sqrMagnitude >= best || !W.CannonPlacable(o, tx, ty)) continue;
                        best = d.sqrMagnitude; host = o; bx = tx; by = ty;
                    }
            }
            if (host == null) { report.AppendLine("## n/a cannon placed: no free cannon spot in the player's zones"); report.AppendLine(); yield break; }
            if (rts) { var at = ZGame.PxToWorld(bx * 16 + 16, by * 16 + 16); at.y = g.GroundY(at.x, at.z); rts.focus = at; rts.Place(); }
            yield return Wait(1.2f);
            yield return Case($"cannon placed (turretbuilt + pop, {host.Type} zone, tile {bx},{by})", () =>
            {
                host.StoredCannons.Add("gatling");
                var c = W.PlaceCannon(g.player, host, bx, by, host.StoredCannons.Count - 1);
                if (c != null) Passive(c); else host.StoredCannons.RemoveAt(host.StoredCannons.Count - 1);
            }, 1.6f);
        }

        ZObject Nearest(System.Func<ZObject, bool> f, Vector3 at, float maxM)
        {
            ZObject best = null; float bd = maxM * maxM;
            foreach (var o in W.Objects) { if (o.Destroyed || !f(o)) continue; var d = g.CenterWorld(o) - at; d.y = 0; if (d.sqrMagnitude < bd) { bd = d.sqrMagnitude; best = o; } }
            return best;
        }

        /// <summary>The attacker's shot: MissileFired now, the sim missile explodes after its flight (damage 0: a sound check).</summary>
        void Missile(ZObject a, ZObject t, int radius, bool grenade)
        {
            int tx = t.CenterX, ty = t.CenterY;
            float flight = Mathf.Max(0.35f, Vector2.Distance(new Vector2(a.CenterX, a.CenterY), new Vector2(tx, ty)) / (grenade ? 90f : 140f));
            W.Emit(ZEventType.MissileFired, a, t, tx, ty, flight, radius, grenade);
            var m = new ZMissile { team = a.Owner, attackerId = a.Id, targetId = t.Id, damage = 0, radius = radius, x = tx, y = ty, grenade = grenade, explodeTime = W.Time + flight };
            W.AddMissileForTest(m);
        }

        /// <summary>
        /// case never reached RockPillar.Break, only the half-health crumble played).</summary>
        void Kill(ZObject o) { o.SetHealth(0); W.UpdateObjectHealth(o); }

        IEnumerator Case(string name, System.Action fire, float window, ZObject look = null)
        {
            // the player looks at what breaks (off-screen world sounds are dropped by design, Zod PlayWavRestricted)
            if (look != null) { var rts = FindFirstObjectByType<RtsCamera>(); if (rts) { rts.focus = g.CenterWorld(look); rts.Place(); } yield return Wait(1.2f); }
            // floor: 1 s of the current bed right before the event (median master RMS), then the event window (max). A busy bed
            float floor = -120f, peak = -120f, dummy;
            yield return Measure(1f, (med, mx) => { floor = med; dummy = mx; });
            for (int k = 0; k < 6 && floor > QuietBedDbfs; k++) yield return Measure(1f, (med, mx) => { floor = med; dummy = mx; });
            float t0 = Time.unscaledTime;
            fire();
            yield return Measure(window, (med, mx) => { peak = mx; });
            var log = OzSoundLog.Since(t0);
            int played = 0, dropped = 0; foreach (var e in log) { if (e.outcome.StartsWith("PLAY")) played++; else if (e.outcome.StartsWith("DROP")) dropped++; }
            bool ok = peak >= floor + PassAboveFloorDb && peak >= PassMinDbfs;
            if (ok) pass++; else fail++;
            report.AppendLine($"## {(ok ? "PASS" : "FAIL")} {name}");
            report.AppendLine($"floor {floor:F1} dBFS (master RMS median), event max {peak:F1} dBFS, +{peak - floor:F1} dB; log: {played} played, {dropped} dropped");
            report.AppendLine("```"); report.Append(log.Count > 0 ? OzSoundLog.Format(log) : "(no sound requested at all)\n"); report.AppendLine("```");
            report.AppendLine();
            yield return Wait(0.6f);
        }
    }
}
