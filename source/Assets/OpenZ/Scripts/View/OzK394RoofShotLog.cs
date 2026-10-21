using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// K394 Play check (menus OpenZ/V1.1/K394 ...): the player's flying shuttle patrols 25 m off the nearest enemy fort (blue first) at its
    /// cruise height, kept at full health and the fort kept alarmed, until its roof defender has fired 20 rounds (or 5 minutes). Every
    /// straight round drawn from a fort's roof deck is logged as the drawer emits it (OpenZFxPlayer.EmitRound -> OzK394RoofShots.Note):
    /// where it leaves against the defender's drawn muzzle (WeaponsV2Runner.DrawnMuzzle, the same frame), whether it is one straight leg or
    /// two (the second leg's start: its height over the muzzle and its distance off the straight muzzle -> target line), the range, and
    /// how far its end is from the ship's hull. Pass per round: it leaves within 0.10 m of his muzzle; at the flying ship one straight leg
    /// (or a bend over the parapet at most 1.0 m off the line); no second leg starting more than 1.05 m over his muzzle (unless it lies on
    /// his line). Pictures: the first three rounds at
    /// 70 m camera height, rounds 11-13 close (25 m). Test only: the ship and the fort's alarm are staged, nothing is saved.
    /// </summary>
    public partial class OzK394RoofShotLog : MonoBehaviour                                                    // K419b: + View/OzK394RoofShotLog.K419.cs (the staging)
    {
        public static string Report = "idle";
        public const int Want = 20;
        public const float PassM = 0.10f, AirKinkM = 1.0f, Leg2OverM = 1.05f, FarH = 70f, CloseH = 25f, HoverM = 25f, SwingM = 14f, TimeoutS = 300f;

        string outDir, runTag; bool before, prevOn;
        ZGame g; ZWorld W; RtsCamera cam; ZObject ship, fort; Team me;
        readonly StringBuilder rows = new StringBuilder();
        readonly StringBuilder notes = new StringBuilder();
        int n, snipers, passes, air, ground, bentN, pics; float worstStart, worstAirKink, worstLeg2Over = float.MinValue;
        int snapAt = -1; string snapName;
        Vector2Int goalA, goalB; bool toA = true; float nextOrder, camH = -1f;

        /// <summary>before = the old drawn path (OzK394RoofShots.On off for this run, put back after).</summary>
        public static void Run(string outDir, string runTag, bool before)
        {
            if (Report == "running") { Debug.LogWarning("[OpenZ K394] a check is running already"); return; }
            Report = "running";
            var p = new GameObject("OzK394RoofShotLog").AddComponent<OzK394RoofShotLog>();
            p.outDir = outDir; p.runTag = runTag; p.before = before;
        }

        void Start() { StartCoroutine(Main()); }

        void Note(string s) { notes.AppendLine(s); Debug.Log("[OpenZ K394] " + s); }

        IEnumerator Main()
        {
            g = ZGame.I; W = g ? g.World : null; cam = FindFirstObjectByType<RtsCamera>();
            if (W == null) { Report = "no level running"; Destroy(gameObject); yield break; }
            me = g.player;
            prevOn = OzK394RoofShots.On; OzK394RoofShots.On = !before;
            fort = PickFort();
            if (fort == null) { Finish("no enemy fort on this level"); yield break; }
            ship = PickShip();
            if (ship == null) { Finish("no shuttle could be placed (the shuttle rule off, or no room)"); yield break; }
            Note($"{runTag}: {(before ? "BEFORE (the old path)" : "AFTER (the kit)")}; fort #{fort.Id} {fort.Type} {fort.Owner}; shuttle #{ship.Id} {ship.Owner}; level {g.level}");
            K419PlanPatrol();                                                                      // K419b: the fort's best side within the threat ring and the sniper's reach; the ship guarded
            OzK394RoofShots.Log = this;
            float t0 = Time.time;
            while (snipers < Want && Time.time - t0 < TimeoutS)
            {
                if (ship == null || ship.Destroyed || fort == null || fort.Destroyed) { Note("the ship or the fort is gone"); break; }
                if (ship.Health < ship.MaxHealth) ship.SetHealth(ship.MaxHealth);                    // staged: the ship stays up for the 20 rounds
                K419Tick();                                                                           // K419b: no staged hit (it kept the side unset: nobody came up); the threat scan calls him
                Patrol();
                Frame(snipers < Want / 2 ? FarH : CloseH);
                if (snapAt >= 0 && Time.frameCount >= snapAt) { snapAt = -1; Picture(snapName); }
                yield return null;
            }
            OzK394RoofShots.Log = null;
            yield return null;
            Finish(snipers >= Want ? "done" : $"stopped after {snipers} sniper rounds");
        }

        ZObject PickFort()
        {
            ZObject best = null; int score = int.MinValue;
            foreach (var o in W.Objects)
            {
                if (o == null || !o.IsFort || o.Destroyed || o.Owner == Team.Neutral || o.Owner == me) continue;
                int s = o.Owner == Team.Blue ? 1 : 0;
                if (s > score) { score = s; best = o; }
            }
            return best;
        }

        ZObject PickShip()
        {
            foreach (var o in W.Objects) if (o != null && o.IsShuttle && !o.Destroyed && o.Owner == me) return o;   // level 16: the player's own ship
            var c = ZGame.PxToWorld(fort.CenterX, fort.CenterY); var m = ZGame.PxToWorld(W.Map.width * 8, W.Map.height * 8);
            var dir = new Vector3(m.x - c.x, 0f, m.z - c.z); dir = dir.sqrMagnitude > 1f ? dir.normalized : Vector3.right;
            for (float r = 40f; r <= 90f; r += 6f)
            {
                var p = c + dir * r; var px = ZGame.WorldToPx(p);
                var o = W.SpawnShuttle(px.x, px.y, me, 0f, out string why);
                if (o == null) continue;
                OzPlayableShuttle.SpawnView(g, o);
                Note($"placed a shuttle #{o.Id} for {me} at px {o.CenterX},{o.CenterY}");
                return o;
            }
            return null;
        }

        /// <summary>Two goals 25 m off the fort's centre (toward the map's middle), 14 m each side: the ship flies back and forth between
        /// them at its cruise height and never arrives (it would land), so it hovers by the fort.</summary>
        void PlanPatrol()
        {
            var c = ZGame.PxToWorld(fort.CenterX, fort.CenterY); var m = ZGame.PxToWorld(W.Map.width * 8, W.Map.height * 8);
            var dir = new Vector3(m.x - c.x, 0f, m.z - c.z); dir = dir.sqrMagnitude > 1f ? dir.normalized : Vector3.right;
            var side = new Vector3(dir.z, 0f, -dir.x); var mid = c + dir * HoverM;
            var a = W.ShuttleClamp(ZGame.WorldToPx(mid + side * SwingM).x, ZGame.WorldToPx(mid + side * SwingM).y);
            var b = W.ShuttleClamp(ZGame.WorldToPx(mid - side * SwingM).x, ZGame.WorldToPx(mid - side * SwingM).y);
            goalA = new Vector2Int(a.x, a.y); goalB = new Vector2Int(b.x, b.y);
            Note($"patrol between px {goalA.x},{goalA.y} and {goalB.x},{goalB.y} ({HoverM:0} m off the fort, {SwingM:0} m each side)");
        }

        void Patrol()
        {
            var goal = toA ? goalA : goalB;
            float d = Vector2.Distance(new Vector2(ship.CenterX, ship.CenterY), goal) / 8f;
            if (d < 6f) { toA = !toA; nextOrder = 0f; goal = toA ? goalA : goalB; }
            if (Time.time < nextOrder) return;
            nextOrder = Time.time + 2f;
            W.SendWaypoints(me, ship, new[] { new ZWaypoint { mode = WpMode.Move, x = goal.x, y = goal.y, playerGiven = true } });
        }

        void Frame(float h)
        {
            if (!cam) return;
            var a = ZGame.PxToWorld(fort.CenterX, fort.CenterY); var b = ZGame.PxToWorld(ship.CenterX, ship.CenterY);
            if (!Mathf.Approximately(h, camH)) { camH = h; cam.FreeLookOff(); cam.SetHeightImmediate(h); }
            cam.focus = Vector3.Lerp(a, b, 0.4f);
        }

        /// <summary>OzK394RoofShots.Note: one straight round drawn this frame (after WeaponHold posed the guns).</summary>
        public void Round(Vector3 from, Vector3 to, bool bent, Vector3 w)
        {
            bool onDeck = OzK394RoofShots.RoofFort(from, out var fv, out var lm);
            UnitView shooter = null; float sd = onDeck ? 2.5f : 0.5f;
            foreach (var kv in g.Views)
            {
                var v = kv.Value; if (!v || !v.IsFortDefender || v.Obj == null) continue;
                float d = Vector3.Distance(WeaponsV2Runner.DrawnMuzzle(g, v.Obj), from);
                if (d < sd) { sd = d; shooter = v; }
            }
            if (!onDeck)
            {
                if (!shooter) return;                                                                // not a fort roof round
                lm = g.StaticViews.TryGetValue(shooter.Obj.DefenderFortId, out var fgo) && fgo ? fgo.transform.InverseTransformPoint(from) : from;
                Note($"round {n + 1}: the defender's muzzle (fort-local {lm.x:0.00}, {lm.y:0.00}, {lm.z:0.00}) is outside the kit's deck band: the old bend applies (FAIL)");
            }
            n++;
            bool sniper = shooter && shooter.Obj.Type == "sniper";
            if (sniper) snipers++;
            var muzzle = shooter ? WeaponsV2Runner.DrawnMuzzle(g, shooter.Obj) : from;
            float start = shooter ? Vector3.Distance(from, muzzle) : 9.99f;
            bool isAir = to.y - g.GroundY(to.x, to.z) > 3f;
            if (isAir) air++; else ground++;
            float over = bent ? w.y - muzzle.y : 0f, kink = bent ? OffLine(w, from, to) : 0f;
            if (bent) bentN++;
            var flat = to - from; flat.y = 0f;
            float hull = OzShuttleAim.HullDist(to);
            bool pass = onDeck && start <= PassM &&(!isAir || !bent || kink <= AirKinkM) && (!bent || over <= Leg2OverM || kink <= 0.05f);
            if (pass) passes++;
            worstStart = Mathf.Max(worstStart, start);
            if (isAir) worstAirKink = Mathf.Max(worstAirKink, kink);
            if (bent) worstLeg2Over = Mathf.Max(worstLeg2Over, over);
            rows.AppendLine($"| {n} | {Time.frameCount} | {(shooter ? "#" + shooter.Obj.Id + " " + shooter.Obj.Type : "none within 2.5 m")} | {(isAir ? "air" : "ground")} | " +
                            $"{lm.y:0.00} | {start * 100f:0.0} | {(bent ? "2 legs" : "1 leg")} | {(bent ? over.ToString("+0.00;-0.00") : "-")} | {(bent ? kink.ToString("0.00") : "-")} | " +
                            $"{flat.magnitude:0.0} | {(isAir ? hull.ToString("0.00") : "-")} | {(pass ? "PASS" : "FAIL")} |");
            if (sniper && (snipers <= 3 || (snipers >= 11 && snipers <= 13)))
            { snapAt = Time.frameCount + 2; snapName = $"K394_{runTag}_round{snipers:00}_{(snipers <= 3 ? "h70" : "close")}"; }
        }

        static float OffLine(Vector3 p, Vector3 a, Vector3 b)
        {
            var ab = b - a; float t = ab.sqrMagnitude > 1e-6f ? Mathf.Clamp01(Vector3.Dot(p - a, ab) / ab.sqrMagnitude) : 0f;
            return Vector3.Distance(p, a + ab * t);
        }

        void Picture(string name)
        {
            try { Directory.CreateDirectory(outDir); ScreenCapture.CaptureScreenshot(Path.Combine(outDir, name + ".png")); pics++; }
            catch (System.Exception e) { Note("picture " + name + ": " + e.Message); }
        }

        void Finish(string why)
        {
            OzK394RoofShots.Log = null; OzK394RoofShots.On = prevOn;
            var sb = new StringBuilder();
            sb.AppendLine($"# K394 roof rounds {runTag} ({(before ? "BEFORE: the old path" : "AFTER: the kit")}) {g?.level}");
            sb.AppendLine();
            sb.Append(notes);
            sb.AppendLine();
            sb.AppendLine("| # | frame | shooter | target | muzzle height in the fort (m) | leaves vs his muzzle (cm) | legs | 2nd leg starts over the muzzle (m) | 2nd leg off the line (m) | range (m) | end from the hull (m) | |");
            sb.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|---|");
            sb.Append(rows);
            sb.AppendLine();
            bool ok = snipers >= Want && passes == n && air >= Want / 2;
            sb.AppendLine($"{why}: {n} roof rounds ({snipers} sniper), air {air}, ground {ground}; PASS {passes}/{n}; bent {bentN}; worst start {worstStart * 100f:0.0} cm (limit {PassM * 100f:0} cm); " +
                          $"worst air bend {worstAirKink:0.00} m (limit {AirKinkM:0.0}); highest 2nd leg over the muzzle {(bentN > 0 ? worstLeg2Over.ToString("+0.00;-0.00") + " m" : "-")} (limit {Leg2OverM:0.0}); pictures {pics}");
            sb.AppendLine($"counts: roof rounds {OzK394RoofShots.RoofRounds}, over the wall {OzK394RoofShots.OverWall}, straight {OzK394RoofShots.Straight}");
            sb.AppendLine(before ? "BEFORE run: expected FAIL (rounds bent 1.2-8 m over his muzzle)" : $"RESULT {(ok ? "PASS" : "FAIL")} (20 sniper rounds, at least 10 at the flying ship, every round PASS)");
            try { Directory.CreateDirectory(outDir); File.WriteAllText(Path.Combine(outDir, $"k394_roof_rounds_{runTag}.md"), sb.ToString()); }
            catch (System.Exception e) { Debug.LogWarning("[OpenZ K394] report: " + e.Message); }
            Report = before ? $"before: {passes}/{n} pass, bent {bentN}" : $"{(ok ? "PASS" : "FAIL")} {passes}/{n}, air {air}, bent {bentN}";
            Debug.Log("[OpenZ K394] " + Report + " -> " + outDir);
            Destroy(gameObject);
        }

        void OnDestroy()
        {
            if (OzK394RoofShots.Log == this) OzK394RoofShots.Log = null;
            if (Report == "running") { OzK394RoofShots.On = prevOn; Report = "stopped"; }
        }
    }
}
