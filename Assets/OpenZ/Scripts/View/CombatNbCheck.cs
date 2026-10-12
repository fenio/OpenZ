using System.Collections;
using System.Collections.Generic;
using System.Text;
using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// Play proof for combat_nb_v1 on L02 in the live game (the CPU bots are removed for the run; the harness issues player-style commands):
    ///  NB3 blast wave (MLRS into 3 blue squads: BlastWave counters), NB12 (a heavy on a jeep stands and fires, a jeep on a jeep manoeuvres),
    ///  NB11 (rear hits on a jeep counted), NB27 (fort mortar: one lob every 30 s, landings, shakes, the gun shot off by tanks), NB14
    ///  (tanks attacking the blue fort fire from their range to the WALL), NB31 (no fort alert for a parked enemy, alert within ~4 s of a
    ///  real hit), NB28 (the pointer over each red tower pad picks that pad's Zod spot; a gatling placed on it), NB30 + NB29 (red loses
    ///  its last unit without a death: the fort is made to fall, the nuke bursts on the roof; frames around the impact).
    /// </summary>
    [DefaultExecutionOrder(40)]
    public class CombatNbCheck : MonoBehaviour
    {
        public static string Report = "idle";
        /// <summary>Stages to run (null = all), e.g. "NB11 NB12 NB27 NB14".</summary>
        public static string Only;
        static bool Do(string k) => Only == null || Only.Contains(k);
        ZGame g; ZWorld W; readonly StringBuilder sb = new StringBuilder(); int pass, total;
        readonly List<ZEvent> fired = new List<ZEvent>();
        readonly List<float> firedAt = new List<float>();
        readonly List<ZObject> keep = new List<ZObject>();
        static string Records => System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "../../../../work/records"));
        static string Frames => Records + "/milestones/combat_nb";

        public static void Run() { Report = "running"; new GameObject("CombatNbCheck").AddComponent<CombatNbCheck>(); }

        void Start() { g = ZGame.I; W = g.World; System.IO.Directory.CreateDirectory(Frames); StartCoroutine(All()); }

        void Update()
        {
            if (W == null) return;
            foreach (var o in keep) if (o != null && !o.Destroyed) { o.SetHealth(o.MaxHealth); foreach (var m in o.Minions) if (!m.Destroyed) m.SetHealth(m.MaxHealth); }
            foreach (var e in W.Events) if (e.type == ZEventType.MissileFired || e.type == ZEventType.Hitscan) { fired.Add(e); firedAt.Add(Time.time); }
        }

        ZObject Squad(string type, Team t, int tx, int ty)
        {
            OzCheatSpawner.FreeTile(W, tx, ty, 1, true, out tx, out ty);
            var o = W.CreateRobotGroup(type, tx * 16, ty * 16, t); g.SpawnUnitView(o); foreach (var m in o.Minions) g.SpawnUnitView(m);
            return o;
        }
        ZObject Thing(string cat, string type, Team t, int tx, int ty)
        {
            OzCheatSpawner.FreeTile(W, tx, ty, 2, false, out tx, out ty);
            var o = W.CreateObject(cat, type, tx * 16, ty * 16, t); g.SpawnUnitView(o); return o;
        }
        void Kill(ZObject o)
        {
            if (o == null || o.Destroyed) return;
            keep.Remove(o);
            foreach (var m in new List<ZObject>(o.Minions)) { m.SetHealth(0); W.UpdateObjectHealth(m); }
            o.SetHealth(0); W.UpdateObjectHealth(o);
        }
        void Attack(ZObject a, ZObject t) => W.SendWaypoints(a.Owner, a, new List<ZWaypoint> { new ZWaypoint { mode = WpMode.Attack, refId = t.Id, x = t.CenterX, y = t.CenterY } });
        void Look(ZObject o, float h) { var cam = FindFirstObjectByType<RtsCamera>(); if (cam) { cam.focus = ZGame.PxToWorld(o.CenterX, o.CenterY); cam.focus.y = 0; cam.SetHeightImmediate(h); } }
        void Shot(string name) => ScreenCapture.CaptureScreenshot(Frames + "/" + name + ".png");
        void Row(string test, bool ok, string measured) { total++; if (ok) pass++; sb.AppendLine($"| {test} | {(ok ? "PASS" : "FAIL")} | {measured} |"); }
        int FirstShot(int id, int from) { for (int i = from; i < fired.Count; i++) if (fired[i].objId == id) return i; return -1; }
        static float RectDist(ZObject a, ZObject b)
        {
            float dx = Mathf.Max(0, Mathf.Max(b.LocX - a.CenterX, a.CenterX - (b.LocX + b.WidthPix)));
            float dy = Mathf.Max(0, Mathf.Max(b.LocY - a.CenterY, a.CenterY - (b.LocY + b.HeightPix)));
            return Mathf.Sqrt(dx * dx + dy * dy);
        }
        (int shots, int land, int shakes) Mortar()
        {
            int s = 0, l = 0, k = 0;
            foreach (var v in FindObjectsByType<FortGunView>(FindObjectsSortMode.None)) { s += v.MortarShots; l += v.MortarLandings; k += v.MortarShakes; }
            return (s, l, k);
        }

        IEnumerator All()
        {
            yield return null;
            W.Bots.Clear();
            sb.AppendLine("# CombatNbCheck (combat_nb_v1 Play proof)\n");
            sb.AppendLine($"{System.DateTime.Now:yyyy-MM-dd HH:mm}, level {g.level}; mortar {ZWorld.FortMortarDeviation}, reach {ZWorld.BuildingReachDeviation}, exposed {ZWorld.ExposedDriverDeviation}, approach {ZWorld.ApproachDeviation}, chip {ZWorld.ArmourChipDeviation}, no tank duel vs jeep {ZWorld.NoTankDuelVsJeep}; CPU bots removed for the run\n");
            sb.AppendLine("| test | result | measured |\n|---|---|---|");
            ZObject blueFort = null, redFort = null;
            foreach (var o in W.Objects) if (o.IsFort && !o.Destroyed && o.IsProducer) { if (o.Owner == Team.Blue) blueFort = o; else if (o.Owner == Team.Red) redFort = o; }
            if (blueFort == null || redFort == null) { Report = "forts missing"; yield break; }

            // ---- NB3 blast wave: MLRS into three blue squads in a line (open ground north-middle)
            if (Do("NB3")) {
            int d0 = BlastWave.Delayed, r0 = BlastWave.ByRipple;
            var mlrs = Thing("vehicle", "missile_launcher", Team.Red, 44, 24); keep.Add(mlrs);
            var sqs = new List<ZObject> { Squad("grunt", Team.Blue, 42, 15), Squad("grunt", Team.Blue, 45, 15), Squad("grunt", Team.Blue, 48, 15) };
            foreach (var s in sqs) { s.Damage = 0; foreach (var m in s.Minions) m.Damage = 0; }
            Look(sqs[1], 34f);
            Attack(mlrs, sqs[1]);
            float t0 = Time.time; bool snap = false;
            while (Time.time - t0 < 30f && (BlastWave.Delayed - d0 < 6 || BlastWave.ByRipple - r0 < 1))
            {
                if (!snap && BlastWave.Delayed > d0) { Shot("NB3_blastwave"); snap = true; }
                yield return null;
            }
            Row("NB3 robots thrown when the blast front / their ripple rocket reaches them", BlastWave.Delayed > d0 && BlastWave.ByRipple > r0 && BlastWave.MaxSeen <= 1.6f,
                $"delayed deaths {BlastWave.Delayed - d0}, by a ripple rocket {BlastWave.ByRipple - r0}, max delay {BlastWave.MaxSeen:F2} s (cap 1.6)");
            Kill(mlrs); foreach (var s in sqs) Kill(s);
            yield return new WaitForSeconds(1f);
            }

            // ---- NB12 a heavy on a jeep stands and fires; a jeep on a jeep manoeuvres
            if (Do("NB12")) {
            var hv = Thing("vehicle", "heavy", Team.Red, 40, 22); var bj = Thing("vehicle", "jeep", Team.Blue, 47, 16);
            keep.Add(hv); keep.Add(bj); bj.Damage = 0; bj.MoveSpeed = 0;                  // a parked target: any movement measured is the attacker's own
            Look(hv, 34f); Attack(hv, bj);
            int f12 = fired.Count; float t12 = Time.time; int hx = -1, hy = -1; float hvPath = 0; int shots12 = 0, hvDuel = 0, jDuel = 0;
            int bjx = bj.CenterX, bjy = bj.CenterY, hvSeq0 = hv.DuelSeq;
            while (Time.time - t12 < 16f)
            {
                if (hv.DuelKind != ZDuelKind.None) hvDuel++;
                if (hx < 0 && FirstShot(hv.Id, f12) >= 0) { hx = hv.CenterX; hy = hv.CenterY; }
                else if (hx >= 0) { hvPath += Mathf.Sqrt((hv.CenterX - hx) * (hv.CenterX - hx) + (hv.CenterY - hy) * (hv.CenterY - hy)); hx = hv.CenterX; hy = hv.CenterY; }
                yield return null;
            }
            for (int i = f12; i < fired.Count; i++) if (fired[i].objId == hv.Id) shots12++;
            Shot("NB12_heavy_vs_jeep");
            Kill(hv);
            var rj = Thing("vehicle", "jeep", Team.Red, 40, 22); keep.Add(rj); Attack(rj, bj);
            int hvSeq = hv.DuelSeq - hvSeq0; int rjSeq0 = rj.DuelSeq;
            int f12b = fired.Count; float t12b = Time.time; int jx = -1, jy = -1; float jPath = 0; int shotsJ = 0;
            while (Time.time - t12b < 14f)
            {
                if (rj.DuelKind != ZDuelKind.None) jDuel++;
                if (jx < 0 && FirstShot(rj.Id, f12b) >= 0) { jx = rj.CenterX; jy = rj.CenterY; }
                else if (jx >= 0) { jPath += Mathf.Sqrt((rj.CenterX - jx) * (rj.CenterX - jx) + (rj.CenterY - jy) * (rj.CenterY - jy)); jx = rj.CenterX; jy = rj.CenterY; }
                yield return null;
            }
            for (int i = f12b; i < fired.Count; i++) if (fired[i].objId == rj.Id) shotsJ++;
            Shot("NB12_jeep_vs_jeep");
            int rjSeq = rj.DuelSeq - rjSeq0;
            float bjMoved = Mathf.Sqrt((bj.CenterX - bjx) * (bj.CenterX - bjx) + (bj.CenterY - bjy) * (bj.CenterY - bjy));
            Row("NB12 heavy tank vs a jeep: no duel manoeuvre (Zod stand and fire); jeep vs jeep still manoeuvres", shots12 > 0 && hvDuel == 0 && hvSeq == 0 && shotsJ > 0 && (jDuel > 0 || rjSeq > 0),
                $"heavy: {shots12} shots, duel-manoeuvre frames {hvDuel}, manoeuvres started {hvSeq}, moved {hvPath:F0} px after its first shot (the target jeep moved {bjMoved:F0} px: dodges); red jeep: {shotsJ} shots, duel-manoeuvre frames {jDuel}, manoeuvres started {rjSeq}, moved {jPath:F0} px");
            Kill(rj); Kill(bj);
            yield return new WaitForSeconds(1f);
            }

            // ---- NB11 rear hits: a blue jeep between two red grunt squads
            if (Do("NB11")) {
            int rear0 = ZWorld.RearHitsCounted;
            var jt = Thing("vehicle", "jeep", Team.Blue, 45, 19); jt.MoveSpeed = 0;           // it faces (fires at) the front squad; the back squad sits in its rear cone
            var front = Squad("grunt", Team.Red, 45, 14); var back = Squad("grunt", Team.Red, 45, 24);
            keep.Add(front); keep.Add(back);
            Look(jt, 34f); Attack(jt, front); Attack(front, jt); Attack(back, jt);
            float t11 = Time.time; float sniped = -1;
            while (Time.time - t11 < 40f && sniped < 0) { if (jt.Owner == Team.Neutral || jt.Destroyed) sniped = Time.time - t11; yield return null; }
            Shot("NB11_rear_hits");
            Row("NB11 hits from behind roll the snipe at 2x (RearHitsCounted rises); the driver falls", ZWorld.RearHitsCounted > rear0,
                $"rear hits counted {ZWorld.RearHitsCounted - rear0}; driver {(sniped >= 0 ? $"sniped after {sniped:F1} s" : "not sniped in 40 s")}; jeep hull {jt.Health}/{jt.MaxHealth}");
            Kill(front); Kill(back); Kill(jt);
            yield return new WaitForSeconds(1f);
            }

            // ---- NB27 fort mortar: a red light tank parked ~10 tiles off the blue fort, no orders, 65 s
            int bfx = blueFort.CenterX / 16, bfy = blueFort.CenterY / 16;
            if (Do("NB27")) {
            var m0 = Mortar();
            var bait = Thing("vehicle", "light", Team.Red, bfx, bfy + 11); keep.Add(bait);      // due south: inside the gun's 200 px, >= 64 px off the 160x192 px fort
            Look(bait, 46f);
            float t27 = Time.time; bool frameBarrel = false, frameLand = false; int land0 = m0.land;
            while (Time.time - t27 < 65f)
            {
                if (!frameBarrel && Time.time - t27 > 12f) { Look(blueFort, 30f); yield return null; Shot("NB27_mortar_barrel"); frameBarrel = true; Look(bait, 46f); }
                if (!frameLand && Mortar().land > land0) { Shot("NB27_mortar_landing"); frameLand = true; }
                yield return null;
            }
            var m1 = Mortar();
            Row("NB27 fort mortar: about one slow lob per 30 s at a parked tank, landings shake the camera", m1.shots - m0.shots >= 1 && m1.shots - m0.shots <= 3 && m1.land > m0.land,
                $"65 s: mortar shots {m1.shots - m0.shots}, landings {m1.land - m0.land}, shakes {m1.shakes - m0.shakes}; bait {Mathf.Sqrt((bait.CenterX - blueFort.CenterX) * (bait.CenterX - blueFort.CenterX) + (bait.CenterY - blueFort.CenterY) * (bait.CenterY - blueFort.CenterY)):F0} px from the fort centre, hull kept full; fort gun HP {blueFort.FortGunHealth}");
            Kill(bait);
            }

            // ---- NB14 + NB27b: light, medium, heavy ordered onto the blue fort from ~16 tiles; range measured to the wall; the gun shot off
            if (Do("NB14")) {
            var tanks = new List<ZObject> { Thing("vehicle", "light", Team.Red, bfx + 12, bfy + 12), Thing("vehicle", "medium", Team.Red, bfx + 15, bfy + 10), Thing("vehicle", "heavy", Team.Red, bfx + 10, bfy + 9) };   // this side of the river
            foreach (var tk in tanks) { keep.Add(tk); Attack(tk, blueFort); }
            Look(blueFort, 50f);
            int f14 = fired.Count; float t14 = Time.time; var firstDist = new Dictionary<int, float>();
            float gunKilledAfter = -1f;
            while (Time.time - t14 < 70f && (firstDist.Count < 3 || blueFort.FortGunKilledAt < 0))
            {
                foreach (var tk in tanks) if (!firstDist.ContainsKey(tk.Id) && FirstShot(tk.Id, f14) >= 0) firstDist[tk.Id] = RectDist(tk, blueFort);
                if (gunKilledAfter < 0 && blueFort.FortGunKilledAt >= 0) gunKilledAfter = Time.time - t14;
                yield return null;
            }
            Shot("NB14_tanks_on_fort");
            var parts = new List<string>(); bool reachOk = firstDist.Count == 3;
            foreach (var tk in tanks) if (firstDist.TryGetValue(tk.Id, out float dd)) { parts.Add($"{tk.Type} {dd:F0} px to the wall (range {tk.AttackRadius})"); if (dd < tk.AttackRadius * 0.6f) reachOk = false; } else parts.Add($"{tk.Type} no shot");
            Row("NB14 tanks attacking the fort fire from their range to the wall", reachOk, string.Join("; ", parts));
            Row("NB27 the fort gun can be shot off by tanks", blueFort.FortGunKilledAt >= 0, $"gun HP {blueFort.FortGunHealth}, killed at sim {blueFort.FortGunKilledAt:F1} s ({gunKilledAfter:F1} s after the order)");
            foreach (var tk in tanks) Kill(tk);
            yield return new WaitForSeconds(1f);
            }

            // ---- NB31 fort alert: a blue tank parked by the red fort (no alert), then ordered onto it (alert within ~4 s of the first hit)
            int rfx = redFort.CenterX / 16, rfy = redFort.CenterY / 16;
            if (Do("NB31")) {
            int al0 = FortAlert.Fired;
            var bt = Thing("vehicle", "medium", Team.Blue, rfx - 9, rfy - 7); keep.Add(bt);
            Look(redFort, 50f);
            yield return new WaitForSeconds(25f);
            int idleAlerts = FortAlert.Fired - al0;
            Attack(bt, redFort);
            double hit0 = redFort.LastEnemyHit; float t31 = Time.time, firstHit = -1, alertAt = -1; int al1 = FortAlert.Fired;
            while (Time.time - t31 < 40f && alertAt < 0)
            {
                if (firstHit < 0 && redFort.LastEnemyHit > hit0) firstHit = Time.time;
                if (FortAlert.Fired > al1) alertAt = Time.time;
                yield return null;
            }
            Row("NB31 no fort alert for a parked enemy; alert on a real hit", idleAlerts == 0 && firstHit > 0 && alertAt > 0 && alertAt - firstHit <= 4.5f,
                $"alerts while parked 25 s: {idleAlerts}; first enemy hit {(firstHit > 0 ? (firstHit - t31).ToString("F1") + " s" : "none")} after the order, alert {(alertAt > 0 && firstHit > 0 ? (alertAt - firstHit).ToString("F1") + " s after the hit" : "none")}");
            Kill(bt);
            yield return new WaitForSeconds(1f);
            }

            // ---- NB28 tower pads: the pointer over each red pad picks that pad's spot; place a gatling there
            if (Do("NB28")) {
            var cam = Camera.main; Look(redFort, 40f); yield return null; yield return null;
            int picked = 0, placedN = 0, pads = 0; var padLog = new List<string>();
            if (g.StaticViews.TryGetValue(redFort.Id, out var fgo) && fgo)
                foreach (var (lx, ly) in TowerMounts.TurretSpots)
                {
                    pads++;
                    int pad = TowerMounts.PadForSpot(fgo.transform, lx, ly);
                    var pw = TowerMounts.PadWorld(fgo.transform, pad);
                    var ray = cam.ScreenPointToRay(cam.WorldToScreenPoint(pw));
                    var want = new Vector2Int((redFort.LocX + lx) >> 4, (redFort.LocY + ly) >> 4);
                    bool ok = TowerMounts.TurretSpotUnderRay(g, ray, "gatling", out var tile, out _, out _) && tile == want;
                    if (ok) picked++;
                    redFort.StoredCannons.Add("gatling");
                    var c = ok ? W.PlaceCannon(Team.Red, redFort, tile.x, tile.y, redFort.StoredCannons.Count - 1) : null;
                    if (c != null) { placedN++; g.SpawnUnitView(c); } else redFort.StoredCannons.RemoveAt(redFort.StoredCannons.Count - 1);
                    padLog.Add($"pad {pad} spot ({lx},{ly}) -> {(ok ? "picked" : "MISSED " + tile)} {(c != null ? "placed" : "occupied/not placed")}");
                }
            yield return new WaitForSeconds(1.5f);
            Shot("NB28_tower_pads");
            Row("NB28 pointer over each tower pad picks its Zod turret spot; free spots take a gatling", pads == 4 && picked == 4 && placedN >= 1, string.Join("; ", padLog));
            }

            // ---- NB30 + NB29: red loses its last unit without a death -> the fort falls, the nuke bursts on the roof
            if (Do("NB30")) {
            int inj0 = FortFallAtGameOver.Injected;
            var last = Thing("vehicle", "jeep", Team.Red, rfx + 8, rfy - 8);
            foreach (var o in new List<ZObject>(W.Objects)) if (o != last && o.IsUnit && o.Owner == Team.Red && !o.Destroyed) { keep.Remove(o); o.SetHealth(0); W.UpdateObjectHealth(o); }
            last.DamageDriverHealth(100000);
            Look(redFort, 60f);
            float t30 = Time.time; int nukeFrames = 0; float nextFrame = 0;
            while (Time.time - t30 < 25f && nukeFrames < 10)
            {
                float s2i = OpenZNukeStrike.SecondsToImpact;
                bool near = (s2i >= 0 && s2i < 0.35f) || (OpenZNukeStrike.LastImpactTime > t30 && Time.time - OpenZNukeStrike.LastImpactTime < 0.6f);
                if (near && Time.unscaledTime >= nextFrame) { Shot($"NB29_nuke_{nukeFrames:00}"); nukeFrames++; nextFrame = Time.unscaledTime + 0.12f; }
                yield return null;
            }
            Row("NB30 the losing fort falls when the last unit is lost without a death", FortFallAtGameOver.Injected > inj0 && W.GameOver,
                $"game over {W.GameOver} (winner {W.Winner}), injected fort deaths {FortFallAtGameOver.Injected - inj0}, red fort destroyed {redFort.Destroyed}");
            Row("NB29 nuke impact frames saved (check: the bomb bursts on the roof)", nukeFrames > 0 && OpenZNukeStrike.LastImpactTime > t30,
                $"{nukeFrames} frames NB29_nuke_*.png around the impact; impact point {OpenZNukeStrike.LastImpactPoint}");
            }

            sb.AppendLine($"\n**{pass}/{total} PASS**");
            System.IO.File.WriteAllText(Records + "/combat_nb_check.md", sb.ToString());
            Report = $"{pass}/{total}";
            Destroy(gameObject);
        }
    }
}
