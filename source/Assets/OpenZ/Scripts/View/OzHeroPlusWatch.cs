using System.Collections.Generic;
using System.Text;
using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// K442 Play watch: ALAN &amp; BRAD +15 % hit points and damage on top of DV112 (rule DV133, Sim/ZHeroSquad.K442.cs). Started from the menu
    /// OpenZ/V1.1/Hero Squad: K442 Watch (Level 16, Play) (Editor/OzV11HeroK442Count.cs) in Level 16, any time. It only reads the running game
    /// (no input, no order, no picture) and judges at once:
    ///  W1 the rules at work as the game set them (DV108, DV112 and DV133 on) and the hero robots of the level found;
    ///  W2 every hero on foot has the new full health (a grunt's x 3, x 1.20, x 1.15, each rounded half up: 4476), DV112's range and the shot
    ///     pause unchanged;
    ///  W3 the live table gives the new hits: the strong shot on a grunt and on a hull, and a thrown grenade of theirs with the grenade rule as
    ///     the game set it (direct and area hit), each DV112's number x 1.15.
    /// Then, as information only, for up to 5 minutes: the self-repair rate of a hurt hero on foot (wanted: 20 % of his full health a minute),
    /// measured over 20 s of steady repair. Output: <see cref="Folder"/> (the work tree's records, milestones/heroes_stronger_k442, found
    /// upward at run time). Test only.
    /// </summary>
    public sealed class OzHeroPlusWatch : MonoBehaviour
    {
        public static string Report = "idle";
        public const double RepairWindow = 20.0, Latest = 300.0;

        public static string Folder()
        {
            var d = new System.IO.DirectoryInfo(Application.dataPath);
            for (int i = 0; i < 8 && d != null; i++, d = d.Parent)
                if (System.IO.Directory.Exists(System.IO.Path.Combine(d.FullName, "work", "records")))
                    return System.IO.Path.Combine(d.FullName, "work", "records", "milestones", "heroes_stronger_k442");
            return System.IO.Path.Combine(Application.persistentDataPath, "heroes_stronger_k442");
        }

        public static void Run()
        {
            Report = "running";
            new GameObject("OzHeroPlusWatch").AddComponent<OzHeroPlusWatch>();
        }

        ZWorld W; double startedAt; bool judged, done; int passes, fails;
        readonly StringBuilder sb = new StringBuilder();
        readonly Dictionary<ZObject, (double t0, int h0, int last)> runs = new Dictionary<ZObject, (double, int, int)>();
        readonly HashSet<ZObject> measured = new HashSet<ZObject>();

        void Log(string t) { sb.AppendLine(t); Debug.Log("[OzHeroPlusWatch] " + t); }
        void Check(string what, bool ok) { if (ok) passes++; else fails++; Log((ok ? "PASS " : "FAIL ") + what); }
        static int Times(int v, decimal f) => (int)System.Math.Round(v * f, System.MidpointRounding.AwayFromZero);

        void Start()
        {
            var g = ZGame.I; W = g ? g.World : null;
            startedAt = W != null ? W.Time : -1;
            Log($"K442 watch: started at game time {startedAt:0.0} s");
        }

        void Update()
        {
            if (done) return;
            if (W == null) { Judge(); Finish(); return; }
            if (!judged) { Judge(); return; }
            foreach (var o in W.Objects)
            {
                if (o.Hero == null || o.Kind != ZKind.Robot || o.Destroyed || o.KillAt >= 0 || measured.Contains(o)) continue;
                if (o.Health >= o.MaxHealth || !runs.TryGetValue(o, out var r) || o.Health < r.last) { runs[o] = (W.Time, o.Health, o.Health); continue; }
                runs[o] = (r.t0, r.h0, o.Health);
                if (W.Time - r.t0 < RepairWindow) continue;
                double perMin = (o.Health - r.h0) / (W.Time - r.t0) * 60.0, want = o.MaxHealth * ZHeroBoost.RegenPct(o.Hero) / 100.0;
                measured.Add(o);
                Log($"INFO self-repair of {o.HeroMemberName}: {perMin:0.0} HP a minute over {W.Time - r.t0:0.0} s ({o.Health}/{o.MaxHealth}); wanted {want:0.0} (20 % of his full health); {(System.Math.Abs(perMin - want) <= want * 0.03 + 2 ? "as wanted" : "NOT as wanted")}");
            }
            if (W.Time - startedAt >= Latest || measured.Count >= 2) Finish();
        }

        void Judge()
        {
            judged = true;
            var heroes = new List<ZObject>();
            if (W != null) foreach (var o in W.Objects) if (o.Hero != null && o.Kind == ZKind.Robot && !o.Destroyed && o.KillAt < 0) heroes.Add(o);
            Check($"W1 rules at work: hero squads {ZWorld.HeroSquadDeviation}, DV112 {ZWorld.HeroBoostDeviation}, DV133 {ZWorld.HeroPlusDeviation}; hero robots on foot {heroes.Count}{(W == null ? " (no game running: start Level 16)" : "")}",
                  W != null && ZWorld.HeroSquadDeviation && ZWorld.HeroBoostDeviation && ZWorld.HeroPlusDeviation && heroes.Count > 0);
            bool ok2 = heroes.Count > 0; var parts = new List<string>();
            foreach (var o in heroes)
            {
                var k = o.Hero; var u = o.U;
                if (u == null) { ok2 = false; continue; }
                int want = Times(Times(u.MaxHealth * k.HealthMul, 1.20m), 1.15m);
                int range = Times((int)System.Math.Round(u.attack_radius * ZHeroRange.MulOf(k, o.HeroMember)), 1.15m);
                double pause = u.attack_speed * k.CycleMul;
                bool good = o.MaxHealth == want && o.AttackRadius == range && System.Math.Abs(o.AttackSpeed - pause) < 1e-9;
                ok2 &= good;
                parts.Add($"{o.HeroMemberName} {o.Health}/{o.MaxHealth} HP (wanted full {want}), {o.AttackRadius} px (wanted {range}), shot pause {o.AttackSpeed:0.000} s (wanted {pause:0.000})");
            }
            Check("W2 the heroes' numbers: " + (parts.Count > 0 ? string.Join("; ", parts) : "no hero on foot"), ok2);
            bool ok3 = false; string line = "no game";
            if (W != null)
            {
                var kind = ZHeroSquads.Get(ZHeroSquads.AlanBrad);
                var grunt = W.S.Unit("grunt");
                int pair = ZWorld.RiflePairDeviation && ZWorld.RiflePair.TryGetValue(("grunt", "grunt"), out int p) && p > grunt.Damage ? p : grunt.Damage;
                int onGrunt = ZHeroBoost.Damage(pair * kind.DamageMul, kind), onHull = ZHeroBoost.Damage(grunt.Damage * kind.DamageMul, kind);
                int nade = ZHeroBoost.Grenade(W.ThrownGrenadeDamage, kind), area = ZHeroBoost.Grenade(W.ThrownGrenadeAreaDamage, kind);
                int wGrunt = Times(Times(pair * kind.DamageMul, 1.20m), 1.15m), wHull = Times(Times(grunt.Damage * kind.DamageMul, 1.20m), 1.15m);
                int wNade = Times(Times(W.ThrownGrenadeDamage, 1.20m), 1.15m), wArea = Times(Times(W.ThrownGrenadeAreaDamage, 1.20m), 1.15m);
                ok3 = onGrunt == wGrunt && onHull == wHull && nade == wNade && area == wArea;
                line = $"strong shot on a grunt {onGrunt} HP (wanted {wGrunt}), on a hull {onHull} (wanted {wHull}); their grenade {nade} on a direct hit, {area} on an area hit (wanted {wNade} / {wArea}; the game's grenade {W.ThrownGrenadeDamage} / {W.ThrownGrenadeAreaDamage}, grenade rule {(ZWorld.GrenadeBoostDeviation ? "on" : "off")})";
            }
            Check("W3 the live table: " + line, ok3);
            Log("RESULT " + passes + " PASS " + fails + " FAIL (the self-repair INFO lines follow when a hurt hero repairs for 20 s; the watch ends after 5 min or both measured)");
            Save();
        }

        void Finish()
        {
            done = true;
            Log($"K442 watch done at game time {(W != null ? W.Time : 0):0.0} s: {passes} PASS {fails} FAIL; self-repair measured for {measured.Count} hero(es)");
            Report = (fails == 0 && passes > 0 ? "PASS " : "FAIL ") + passes + "/" + (passes + fails);
            Save();
            Destroy(gameObject);
        }

        void Save()
        {
            try
            {
                string dir = Folder(); System.IO.Directory.CreateDirectory(dir);
                System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "hero_plus_watch_log.txt"), sb.ToString());
            }
            catch (System.Exception e) { Debug.LogWarning("[OzHeroPlusWatch] log not saved: " + e.Message); }
        }
    }
}
