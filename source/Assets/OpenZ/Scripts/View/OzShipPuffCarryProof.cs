using System.Collections;
using System.Collections.Generic;
using System.Text;
using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// K432 Play proof (a flying, burning shuttle's puffs appear on its hull: View/FxV3/OpenZDamageStates.ShipCarry.cs), on level 16 in
    /// Play (menu OpenZ/V1.1/Shuttle: K432 Puffs On The Hull Proof, Editor/OzShipPuffCarryMenu.cs). Set-up as the K413 proof: the CPU
    /// teams' orders removed, the other teams hold their fire, day clock 0.10 held, 60 fps, the full smoke set. Health is set straight in
    /// the rules (test only) and given back at the end.
    /// The probe (LateUpdate at order 207, after the smoke step at 205) reads the real particles of the three channels the ship's damage
    /// puffs use (FxCh_leg_smoke, FxCh_leg_spark, FxCh_leg_fire) near the flying ship (within 16 m of its middle, the ship 6 m or more over
    /// the ground, and never a particle more than 3 m under its drawn root, so no ground unit's smoke or fire counts). Each particle is
    /// taken once, the first frame it is half visible (its age past 15 % of
    /// its life: the channel's alpha sin(pi x 1.08 t) reaches 0.5 there), and its distance from the nearest damage point of THAT frame
    /// (the ship's points, the flame tongue 0.4 m under the fire point) is written down: how far from its point the puff APPEARS.
    /// <list type="bullet">
    /// <item>C0 the fix is on.</item>
    /// <item>C1 burning (20 %), 8 s at cruise: smoke appears within 1.2 m of its point (median; 90 % within 2.5 m), embers within 1.0 m
    /// (median), puffs laid with the hull's velocity. Picture K432_air_fire_70m.</item>
    /// <item>C2 battered (45 %), 8 s at cruise: smoke within 1.2 m (median). Picture K432_air_smoke_70m.</item>
    /// <item>C3 the old puffs (the test switch off), burning, 5 s at cruise: the smoke appears further out (median at least 1.2 m and 1.5 x
    /// C1's): the fault seen again, so the probe can see it. Picture K432_air_fire_before_70m.</item>
    /// <item>C4 the flame tongues in flight ride on the hull: within 1.0 m of the fire point (median), none more than 3 m.</item>
    /// <item>C5 standing burning: the old calls exactly (nothing carried while it stands), the beat still lays its puffs. Picture
    /// K432_ground_fire_70m. Plus, for the eye, K432_air_fire_night_70m (clock 0.62) and the item's own close view at night (the
    /// closest camera, 25 m): K432_air_fire_night_close (the fix) and K432_air_fire_night_close_before (the old puffs).</item>
    /// </list>
    /// <see cref="Folder"/>
    /// </summary>
    [DefaultExecutionOrder(207)]
    public sealed class OzShipPuffCarryProof : MonoBehaviour
    {
        public static string Report = "idle";
        public static string Folder()
        {
            var d = new System.IO.DirectoryInfo(Application.dataPath);
            for (int i = 0; i < 8 && d != null; i++, d = d.Parent)
                if (System.IO.Directory.Exists(System.IO.Path.Combine(d.FullName, "work", "records")))
                    return System.IO.Path.Combine(d.FullName, "work", "records", "milestones", "shuttle_puffs_on_hull_k432");
            return System.IO.Path.Combine(Application.persistentDataPath, "shuttle_puffs_on_hull_k432");
        }
        public static void Run()
        {
            Report = "running";
#if UNITY_EDITOR
            var gv = System.Type.GetType("UnityEditor.GameView,UnityEditor"); if (gv != null) UnityEditor.EditorWindow.FocusWindowIfItsOpen(gv);
#endif
            new GameObject("OzShipPuffCarryProof").AddComponent<OzShipPuffCarryProof>();
        }

        public const float SmokeMedianM = 1.2f, SmokeP90M = 2.5f, EmberMedianM = 1.0f, FlameMedianM = 1.0f, FlameMaxM = 3f, BeforeK = 1.5f;
        public const float CruiseSec = 8f, BeforeSec = 5f, NearM = 16f, HighM = 6f;
        /// <summary>The closest camera for the night pictures (the item's own close view), and how far under the drawn root (the pad soles) a
        /// particle may be and still count as the ship's (ground smoke, wreck fires and embers under a flying ship are not its puffs).</summary>
        public const float CloseM = 25f, UnderRootM = 3f;
        const float HalfSeen = 0.1543f, HalfSeenLate = 0.32f;   // sin(pi x 1.08 t) = 0.5 at t = 0.1543 of the life

        ZGame g; ZWorld W; PlayerInput input; RtsCamera rts; DayNight dn; Team me = Team.Red; ZObject s; OzPlayableShuttle ship;
        readonly StringBuilder sb = new StringBuilder(), csv = new StringBuilder("clock_s,phase,channel,appears_from_point_m,ship_speed_mps,ship_over_ground_m\n");
        int fails, passes; string dir; bool follow; float followH = 70f;
        readonly Dictionary<ZObject, int> heldFire = new Dictionary<ZObject, int>();

        // ------------------------------------------------------------------ the probe
        sealed class Tally
        {
            public string name; public float airSec, speedSum; public int speedN;
            public readonly List<float> smoke = new List<float>(), ember = new List<float>(), flame = new List<float>();
            public readonly HashSet<uint> seen = new HashSet<uint>();
        }
        Tally cur;
        ParticleSystem psSmoke, psSpark, psFire;
        ParticleSystem.Particle[] buf = new ParticleSystem.Particle[2048];
        readonly Vector3[] pts = new Vector3[4];

        void LateUpdate()
        {
            if (follow && ship) Cam(ship.transform.position, followH);
            if (cur == null || s == null || !ship) return;
            if (s.ShuttleAirborne && s.ShuttleState == ZShuttleState.Cruise) { cur.airSec += Time.deltaTime; cur.speedSum += ship.Velocity.magnitude; cur.speedN++; }
            if (!s.ShuttleAirborne || ship.ShownHeight < HighM || OpenZDamageStates.StageOf(s.Id) <= 0) return;
            int n = Mathf.Clamp(OpenZDamageStates.ShipPointsUsed, 0, 3);
            if (n == 0) return;
            for (int i = 0; i < n; i++) pts[i] = OpenZDamageStates.ShipLastPoints[i];
            pts[3] = OpenZDamageStates.ShipLastPoints[0] + Vector3.down * 0.4f;                         // the flame tongue's start
            var mid = ship.transform.TransformPoint(new Vector3(0f, OzShuttleAim.MidM, 0f));
            float spd = ship.Velocity.magnitude, og = ship.ShownHeight;
            Read(psSmoke, cur.smoke, "leg_smoke", n, false, mid, spd, og);
            Read(psSpark, cur.ember, "leg_spark", n, false, mid, spd, og);
            Read(psFire, cur.flame, "leg_fire", n, true, mid, spd, og);
        }

        void Read(ParticleSystem ps, List<float> into, string ch, int n, bool flame, Vector3 mid, float spd, float og)
        {
            if (!ps) return;
            int c = ps.particleCount; if (c == 0) return;
            if (buf.Length < c) buf = new ParticleSystem.Particle[c + 256];
            c = ps.GetParticles(buf);
            for (int k = 0; k < c; k++)
            {
                var p = buf[k]; float life = p.startLifetime; if (life <= 0f) continue;
                float t = 1f - p.remainingLifetime / life;
                if (t < HalfSeen || t > HalfSeenLate) continue;                                          // just half visible (a slow frame may skip a little)
                if ((p.position - mid).sqrMagnitude > NearM * NearM) continue;
                if (p.position.y < ship.transform.position.y - UnderRootM) continue;                    // under the flying ship: the ground's smoke and fires, not its puffs
                if (!cur.seen.Add(p.randomSeed ^ (uint)ch.Length * 2654435761u)) continue;
                float d = float.MaxValue;
                if (flame) d = Vector3.Distance(p.position, pts[3]);
                else for (int i = 0; i < n; i++) d = Mathf.Min(d, Vector3.Distance(p.position, pts[i]));
                into.Add(d);
                csv.Append(Time.time.ToString("F3")).Append(',').Append(cur.name).Append(',').Append(ch).Append(',').Append(d.ToString("F2")).Append(',')
                   .Append(spd.ToString("F2")).Append(',').Append(og.ToString("F1")).Append('\n');
            }
        }

        static float Pct(List<float> v, float q)
        {
            if (v.Count == 0) return float.NaN;
            var a = new List<float>(v); a.Sort();
            return a[Mathf.Clamp(Mathf.RoundToInt(q * (a.Count - 1)), 0, a.Count - 1)];
        }
        static float Max(List<float> v) { float m = 0f; foreach (var x in v) m = Mathf.Max(m, x); return m; }
        static string Of(Tally t) => $"{t.airSec:F1} s at cruise (mean {(t.speedN > 0 ? t.speedSum / t.speedN : 0f):F1} m/s); smoke {t.smoke.Count} puffs, appear {Pct(t.smoke, 0.5f):F2} m from their point (median), 90 % within {Pct(t.smoke, 0.9f):F2} m; embers {t.ember.Count}, median {Pct(t.ember, 0.5f):F2} m; flame tongues {t.flame.Count}, median {Pct(t.flame, 0.5f):F2} m, the farthest {Max(t.flame):F2} m";
        Tally Begin(string name) { cur = new Tally { name = name }; return cur; }
        Tally End() { var t = cur; cur = null; return t; }

        // ------------------------------------------------------------------ helpers (the K413 proof's own)
        void Start()
        {
            g = ZGame.I; W = g ? g.World : null; input = FindFirstObjectByType<PlayerInput>(); rts = FindFirstObjectByType<RtsCamera>();
            dir = Folder(); System.IO.Directory.CreateDirectory(dir);
            StartCoroutine(All());
        }
        void Log(string t) { sb.AppendLine(t); Debug.Log("[OzShipPuffCarryProof] " + t); }
        void Check(string what, bool ok) { if (ok) passes++; else fails++; Log((ok ? "PASS " : "FAIL ") + what); }
        void Shot(string name) { ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, name + ".png")); Log($"frame {name}.png"); }
        IEnumerator WaitFor(System.Func<bool> c, float max) { float t = 0f; while (t < max && !c()) { t += Time.deltaTime; yield return null; } }
        IEnumerator Wait(float s0) { float t = 0f; while (t < s0) { t += Time.deltaTime; yield return null; } }
        void Cam(Vector3 focus, float h) { if (!rts) return; rts.FreeLookOff(); rts.focus = new Vector3(focus.x, 0f, focus.z); rts.SetHeightImmediate(h); }
        bool Move(int x, int y) => W.SendWaypoints(me, s, new[] { new ZWaypoint { mode = WpMode.Move, x = x, y = y, playerGiven = true } });
        void Hp(ZObject o, float k) { if (o != null && !o.Destroyed) { o.SetHealth(Mathf.Max(1, Mathf.RoundToInt(o.MaxHealth * k))); W.UpdateObjectHealth(o); } }
        void SetClock(float t) { if (dn) { dn.timeOfDay = t; dn.Apply(t); } }
        bool Landed() => s.ShuttleState == ZShuttleState.Landed && !s.ShuttleAirborne && !ship.CrashOn;
        static ParticleSystem Channel(string name)
        {
            var fx = OpenZFxPlayer.I; if (!fx) return null;
            var t = fx.transform.Find("FxCh_" + name);
            return t ? t.GetComponent<ParticleSystem>() : null;
        }

        int[] Free(int cx, int cy, int minR, int maxR)
        {
            long best = long.MaxValue; int[] r = null; int e = ZWorld.ShuttleEdgeTiles;
            for (int ty = e; ty < W.Map.height - e; ty++)
                for (int tx = e; tx < W.Map.width - e; tx++)
                {
                    int x = tx * 16 + 8, y = ty * 16 + 8; long dd = (long)(x - cx) * (x - cx) + (long)(y - cy) * (y - cy);
                    if (dd >= best || dd < (long)minR * minR || dd > (long)maxR * maxR) continue;
                    if (W.ShuttleNeverLands(x, y, s, out _) || !W.ShuttlePadFree(x, y, s, out _)) continue;
                    best = dd; r = new[] { x, y };
                }
            return r;
        }

        void HoldFire()
        {
            foreach (var o in W.Objects)
            {
                if (o == null || o.Destroyed || o.Owner == me || o.Owner == Team.Neutral || o.AttackRadius <= 0 || heldFire.ContainsKey(o)) continue;
                heldFire[o] = o.AttackRadius; o.AttackRadius = 0;
            }
        }
        void OnDestroy()
        {
            follow = false; OpenZDamageStates.ProofLite = -1; OpenZDamageStates.ShipCarryOn = true;
            foreach (var kv in heldFire) if (kv.Key != null && kv.Key.AttackRadius == 0) kv.Key.AttackRadius = kv.Value;
            heldFire.Clear();
        }

        /// <summary>Health k with a clean change of the switch: through full health (no puffs for a few frames), the switch, then k.</summary>
        IEnumerator Stage(float k, bool carry)
        {
            Hp(s, 1f); yield return Wait(0.3f);
            OpenZDamageStates.ShipCarryOn = carry;
            yield return null; yield return null;
            Hp(s, k); yield return null;
        }

        /// <summary>The ship at cruise between two landable spots until `sec` s at cruise are measured; the picture `shot` half-way.</summary>
        IEnumerator Cruise(int[] a, int[] b, float sec, float max, string shot)
        {
            bool toB = true; float waited = 0f, lastOrder = 0f; bool shotDone = shot == null;
            Move(b[0], b[1]);
            while (waited < max && cur.airSec < sec)
            {
                yield return null; waited += Time.deltaTime;
                var goal = toB ? b : a;
                float dx = (s.CenterX - goal[0]) / 8f, dy = (s.CenterY - goal[1]) / 8f;
                if (waited - lastOrder >= 1.5f && ((s.ShuttleAirborne && dx * dx + dy * dy < 30f * 30f) || Landed()))
                {
                    toB = !toB; var ng = toB ? b : a; Move(ng[0], ng[1]); lastOrder = waited;
                }
                if (!shotDone && cur.airSec >= sec * 0.5f) { shotDone = true; Shot(shot); }
            }
        }

        IEnumerator All()
        {
            Application.targetFrameRate = 60;
            if (W == null || input == null) { Log("no game (level 16 in Play)"); fails++; Finish(); yield break; }
            if (!ZWorld.ShuttlePlayableDeviation || W.PlayableShuttle == null) { Log("no playable shuttle (level 16, the shuttle rule on)"); fails++; Finish(); yield break; }
            me = g.player; s = W.PlayableShuttle; ship = OzPlayableShuttle.Of(s);
            psSmoke = Channel("leg_smoke"); psSpark = Channel("leg_spark"); psFire = Channel("leg_fire");
            if (!ship || !OpenZDamageStates.I || !psSmoke || !psSpark || !psFire) { Log("the shuttle has no view, or no damage states, or the effect channels are missing"); fails++; Finish(); yield break; }
            int bots = W.Bots.Count; W.Bots.Clear(); HoldFire();
            OpenZDamageStates.ProofLite = 0;
            dn = FindFirstObjectByType<DayNight>(); if (dn) { SetClock(0.10f); dn.running = false; }
            Log($"K432 puffs on the hull proof, {UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}, {Screen.width}x{Screen.height}, preset {OzQuality.Name}; CPU removed {bots}, the other teams hold their fire ({heldFire.Count}), day 0.10 held; shuttle #{s.Id} ({s.MaxHealth} HP); carry on {OpenZDamageStates.ShipCarryOn}, smoke x{OpenZDamageStates.ShipSmokeCarry:F2}, embers x{OpenZDamageStates.ShipEmberCarry:F2}, flame x{OpenZDamageStates.FlameCarry:F2}");
            Check("C0 the fix is on (the ship's puffs leave with the hull's velocity)", OpenZDamageStates.ShipCarryOn && OpenZDamageStates.ShipSmokeCarry > 0.5f);

            Hp(s, 1f);
            yield return WaitFor(() => Landed(), 30f); yield return Wait(0.8f);
            var a = Free(s.CenterX, s.CenterY, 0, 40); var b = Free(s.CenterX, s.CenterY, 560, 900);
            if (a == null) a = new[] { s.CenterX, s.CenterY };
            if (b == null) { Check("a landing spot 70 .. 110 m away for the cruise", false); Finish(); yield break; }
            Log($"cruise between px {a[0]},{a[1]} and {b[0]},{b[1]} ({Mathf.Sqrt((a[0] - b[0]) * (a[0] - b[0]) + (a[1] - b[1]) * (a[1] - b[1])) / 8f:F0} m)");
            follow = true;

            // C1 burning at cruise (the fix)
            OpenZDamageStates.ShipCarryProofReset();
            Begin("C1_burning"); yield return Stage(0.20f, true);
            yield return Cruise(a, b, CruiseSec, 60f, "K432_air_fire_70m");
            var c1 = End(); Log("C1 burning: " + Of(c1) + $"; laid with the hull's velocity: big {OpenZDamageStates.ShipCarriedPuffs}, small {OpenZDamageStates.ShipCarriedSmallPuffs}, embers {OpenZDamageStates.ShipCarriedEmbers}, flame tongues {OpenZDamageStates.ShipHullFlames}; top speed {OpenZDamageStates.ShipCarrySpeedMax:F1} m/s");
            float s1 = Pct(c1.smoke, 0.5f);
            Check($"C1 burning (20 %), {c1.airSec:F1} s at cruise: the smoke appears {s1:F2} m from its damage point (median, wanted <= {SmokeMedianM:F1}; 90 % within {Pct(c1.smoke, 0.9f):F2}, wanted <= {SmokeP90M:F1}), the embers {Pct(c1.ember, 0.5f):F2} m (wanted <= {EmberMedianM:F1}), and the puffs left with the hull's velocity",
                  c1.airSec >= CruiseSec - 0.5f && c1.smoke.Count >= 30 && s1 <= SmokeMedianM && Pct(c1.smoke, 0.9f) <= SmokeP90M && c1.ember.Count >= 5 && Pct(c1.ember, 0.5f) <= EmberMedianM
                  && OpenZDamageStates.ShipCarriedPuffs > 0 && OpenZDamageStates.ShipCarriedEmbers > 0);
            Check($"C4 the flame tongues in flight ride on the hull: {c1.flame.Count} seen, {Pct(c1.flame, 0.5f):F2} m from the fire point (median, wanted <= {FlameMedianM:F1}), the farthest {Max(c1.flame):F2} m (wanted <= {FlameMaxM:F0})",
                  c1.flame.Count >= 5 && Pct(c1.flame, 0.5f) <= FlameMedianM && Max(c1.flame) <= FlameMaxM && OpenZDamageStates.ShipHullFlames > 0);

            // the night look of C1 (for the eye, not counted)
            Begin("C1_night"); SetClock(0.62f); yield return Cruise(a, b, 2f, 20f, "K432_air_fire_night_70m"); End();
            Begin("C1_night_close"); followH = CloseM; yield return Cruise(a, b, 3f, 20f, "K432_air_fire_night_close"); End(); followH = 70f; SetClock(0.10f);   // the item's own view: night, the closest camera

            // C2 battered at cruise (the fix)
            Begin("C2_battered"); yield return Stage(0.45f, true);
            yield return Cruise(a, b, CruiseSec, 60f, "K432_air_smoke_70m");
            var c2 = End(); Log("C2 battered: " + Of(c2));
            float s2 = Pct(c2.smoke, 0.5f);
            Check($"C2 battered (45 %), {c2.airSec:F1} s at cruise: the smoke appears {s2:F2} m from its damage point (median, wanted <= {SmokeMedianM:F1})",
                  c2.airSec >= CruiseSec - 0.5f && c2.smoke.Count >= 30 && s2 <= SmokeMedianM);

            // C3 the old puffs, for the record (the fault must show, so the probe can see it)
            Begin("C3_before"); yield return Stage(0.20f, false);
            yield return Cruise(a, b, BeforeSec, 40f, "K432_air_fire_before_70m");
            var c3 = End(); Log("C3 the old puffs: " + Of(c3));
            Begin("C3_night_close"); SetClock(0.62f); followH = CloseM; yield return Cruise(a, b, 3f, 20f, "K432_air_fire_night_close_before"); End(); followH = 70f; SetClock(0.10f);   // the old puffs in the item's own view
            yield return Stage(1f, true);
            float s3 = Pct(c3.smoke, 0.5f);
            Check($"C3 the old puffs (test switch off): the smoke appears {s3:F2} m from its damage point (median; wanted >= {SmokeMedianM:F1} and >= {BeforeK:F1} x C1's {s1:F2}): the fault seen by the probe",
                  c3.smoke.Count >= 20 && s3 >= SmokeMedianM && s3 >= BeforeK * s1);

            // C5 standing burning: the old calls exactly
            yield return WaitFor(() => Landed(), 40f);
            if (!Landed()) { Move(a[0], a[1]); yield return WaitFor(() => Landed(), 40f); }
            yield return Wait(1.5f);
            follow = false; Cam(ship.transform.position, 70f);
            yield return Stage(0.20f, true);
            yield return Wait(0.5f);
            OpenZDamageStates.ShipCarryProofReset(); int beat0 = OpenZDamageStates.ShipBeatPuffs;
            yield return Wait(2.5f);
            Shot("K432_ground_fire_70m"); yield return null; yield return null;
            int beat = OpenZDamageStates.ShipBeatPuffs - beat0, carried = OpenZDamageStates.ShipCarriedPuffs + OpenZDamageStates.ShipCarriedSmallPuffs + OpenZDamageStates.ShipCarriedEmbers;
            Log($"C5 standing burning: {beat} beat puffs in 2.5 s, carried {carried}, landed {Landed()}, stage {OpenZDamageStates.StageOf(s.Id)}");
            Check($"C5 standing burning: the old calls exactly ({carried} puffs carried, wanted 0; {beat} beat puffs, wanted >= 6)", Landed() && carried == 0 && beat >= 6);
            Hp(s, 1f);
            Finish();
        }

        void Finish()
        {
            follow = false; cur = null; OpenZDamageStates.ProofLite = -1; OpenZDamageStates.ShipCarryOn = true;
            if (s != null && !s.Destroyed) Hp(s, 1f);
            Log($"K432 puffs on the hull proof: {passes} PASS, {fails} FAIL (expected 6 PASS)");
            Report = $"{passes} PASS, {fails} FAIL";
            try
            {
                System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "puffs_on_hull_proof_log.txt"), sb.ToString());
                System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "puffs_appear.csv"), csv.ToString());
            }
            catch (System.Exception e) { Debug.LogWarning("[OzShipPuffCarryProof] log not written: " + e.Message); }
            Destroy(gameObject, 1f);
        }
    }
}
