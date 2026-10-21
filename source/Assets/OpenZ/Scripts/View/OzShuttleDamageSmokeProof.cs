using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// K393 Play proof (the shuttle's damage smoke and fire: View/FxV3/OpenZDamageStates.Shuttle.cs), on level 16 in Play (menu
    /// OpenZ/V1.1/Shuttle: K393 Damage Smoke Proof, Editor/OzShuttleDamageSmokeMenu.cs). Counted PASS / FAIL lines and full-screen
    /// pictures from the normal RTS camera at 70 m. Set-up: the computer player's orders removed, the other teams hold their fire, morning
    /// (day clock 0.10, held), 60 fps. Health is set straight in the rules (test only) and given back at the end.
    /// <list type="bullet">
    /// <item>S1 the same thresholds as the other vehicles: the shuttle and a ground vehicle side by side at 100 / 55 / 45 / 30 / 20 / 35
    /// / 45 / 60 % hold the same stage (0 0 1 1 2 2 1 0: smoke at 50 %, fire at 25 %, the fire out only past 40 %).</item>
    /// <item>S2 every emitter point lies over the drawn skin (hull and pods) and is seen from the camera (a ray test on test-only colliders
    /// of their meshes); the others' old spots (4.0 m and 2.4 m over the root) lie under it (why nothing was seen before).</item>
    /// <item>S3 standing, battered (45 %): grey smoke rises from 2 points, the clank is heard. Picture K393_ground_smoke_70m.</item>
    /// <item>S4 standing, burning (20 %): the small fire on its pod, the scaled flame and dark smoke, 3 points, the ignite and the fire
    /// loop are heard. Picture K393_ground_fire_70m.</item>
    /// <item>S5 LOW: one point less in each stage (2 burning, 1 battered).</item>
    /// <item>S6 in flight, battered: the trail (a puff each 0.4 m of each point's way, no gap wider than the spacing). Picture
    /// K393_air_smoke_70m.</item>
    /// <item>S7 in flight, burning: fire ticks and the trail while it flies. Picture K393_air_fire_70m.</item>
    /// <item>S8 R to the repair station: once repaired past 50 % the fire is out (the steam hiss), the stage is 0 and no puff follows.
    /// Picture K393_repaired_70m.</item>
    /// </list>
    /// <see cref="Folder"/>
    /// </summary>
    public sealed class OzShuttleDamageSmokeProof : MonoBehaviour
    {
        public static string Report = "idle";
        public static string Folder()
        {
            var d = new System.IO.DirectoryInfo(Application.dataPath);
            for (int i = 0; i < 8 && d != null; i++, d = d.Parent)
                if (System.IO.Directory.Exists(System.IO.Path.Combine(d.FullName, "work", "records")))
                    return System.IO.Path.Combine(d.FullName, "work", "records", "milestones", "shuttle_damage_smoke_k393");
            return System.IO.Path.Combine(Application.persistentDataPath, "shuttle_damage_smoke_k393");
        }
        public static void Run()
        {
            Report = "running";
#if UNITY_EDITOR
            var gv = System.Type.GetType("UnityEditor.GameView,UnityEditor"); if (gv != null) UnityEditor.EditorWindow.FocusWindowIfItsOpen(gv);
#endif
            new GameObject("OzShuttleDamageSmokeProof").AddComponent<OzShuttleDamageSmokeProof>();
        }

        // level 16, open ground east of the robot factory (the K256g2 / K387 proofs' line Z0)
        static readonly Vector2 P0 = new Vector2(118, -82), A = new Vector2(180, -82);

        ZGame g; ZWorld W; PlayerInput input; RtsCamera rts; DayNight dn; Team me = Team.Red; ZObject s, veh; OzPlayableShuttle ship;
        readonly StringBuilder sb = new StringBuilder(); int fails, passes; string dir; bool follow; int vehHp0 = -1;
        readonly Dictionary<ZObject, int> heldFire = new Dictionary<ZObject, int>();

        void Start()
        {
            g = ZGame.I; W = g ? g.World : null; input = FindFirstObjectByType<PlayerInput>(); rts = FindFirstObjectByType<RtsCamera>();
            dir = Folder(); System.IO.Directory.CreateDirectory(dir);
            StartCoroutine(All());
        }

        void Log(string t) { sb.AppendLine(t); Debug.Log("[OzShuttleDamageSmokeProof] " + t); }
        void Check(string what, bool ok) { if (ok) passes++; else fails++; Log((ok ? "PASS " : "FAIL ") + what); }
        void Shot(string name) { ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, name + ".png")); Log($"frame {name}.png"); }
        IEnumerator WaitFor(System.Func<bool> c, float max) { float t = 0f; while (t < max && !c()) { t += Time.deltaTime; yield return null; } }
        IEnumerator Wait(float s0) { float t = 0f; while (t < s0) { t += Time.deltaTime; yield return null; } }
        void Cam(Vector3 focus, float h) { if (!rts) return; rts.FreeLookOff(); rts.focus = new Vector3(focus.x, 0f, focus.z); rts.SetHeightImmediate(h); }
        void Update() { if (follow && ship) Cam(ship.transform.position, 70f); }
        static int Px(float x) => Mathf.Clamp(Mathf.FloorToInt(x * 8f / 16f), 1, 400) * 16 + 8;
        static ZWaypoint Wp(Vector2 w) => new ZWaypoint { mode = WpMode.Move, x = Px(w.x), y = Px(-w.y), playerGiven = true };
        bool MoveTo(Vector2 w) => W.SendWaypoints(me, s, new[] { Wp(w) });
        void SetClock(float t) { if (dn) { dn.timeOfDay = t; dn.Apply(t); } }
        void Hp(ZObject o, float k) { if (o != null && !o.Destroyed) { o.SetHealth(Mathf.Max(1, Mathf.RoundToInt(o.MaxHealth * k))); W.UpdateObjectHealth(o); } }
        int Heard(float t0, string key) => OzSoundLog.Since(t0).Count(e => e.key == key && e.outcome != null && !e.outcome.StartsWith("DROP"));   // played or looped (not dropped)

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
            follow = false; OpenZDamageStates.ProofLite = -1;
            foreach (var kv in heldFire) if (kv.Key != null && kv.Key.AttackRadius == 0) kv.Key.AttackRadius = kv.Value;
            heldFire.Clear();
        }

        IEnumerator All()
        {
            Application.targetFrameRate = 60;
            if (W == null || input == null) { Log("no game (level 16 in Play)"); fails++; Finish(); yield break; }
            if (!ZWorld.ShuttlePlayableDeviation || W.PlayableShuttle == null) { Log("no playable shuttle (level 16, the shuttle rule on)"); fails++; Finish(); yield break; }
            me = g.player; s = W.PlayableShuttle; ship = OzPlayableShuttle.Of(s);
            if (!ship || !OpenZDamageStates.I) { Log("the shuttle has no view, or no damage states"); fails++; Finish(); yield break; }
            veh = W.Objects.Where(o => o != null && o.Kind == ZKind.Vehicle && !o.IsShuttle && !o.Destroyed && o.MaxHealth > 0 && g.Views.ContainsKey(o.Id))
                           .OrderBy(o => o.Owner == me ? 0 : 1).ThenBy(o => o.Id).FirstOrDefault();
            if (veh != null) vehHp0 = veh.Health;
            int bots = W.Bots.Count; W.Bots.Clear(); HoldFire();
            OzProofHoldFire.Attach(gameObject, W, s, me, false, true, true, Log);                                      // K419b: units built later hold their fire too, the forts' big guns quiet (the stages set the ship's health themselves)
            OpenZDamageStates.ProofLite = 0;                                                              // the counted lines S3, S4, S6, S7 want the full set whatever the player's GRAPHICS QUALITY (S5 tests LOW)
            dn = FindFirstObjectByType<DayNight>(); if (dn) { SetClock(0.10f); dn.running = false; }
            Log($"K393 damage smoke proof, {UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}, {Screen.width}x{Screen.height}, preset {OzQuality.Name}; CPU removed {bots}, the other teams hold their fire ({heldFire.Count}), day 0.10 held; shuttle #{s.Id} ({s.MaxHealth} HP), side by side with {(veh != null ? veh.Type + " #" + veh.Id : "no ground vehicle")}");

            // the run-up: to P0 and down, full health
            Hp(s, 1f); MoveTo(P0);
            yield return WaitFor(() => s.ShuttleAirborne, 10f);
            yield return WaitFor(() => !s.ShuttleAirborne && s.ShuttleState == ZShuttleState.Landed, 60f);
            yield return Wait(1.5f);
            Cam(ship.transform.position, 70f);

            // S1 thresholds side by side
            float[] ks = { 1f, 0.55f, 0.45f, 0.30f, 0.20f, 0.35f, 0.45f, 0.60f }; int[] want = { 0, 0, 1, 1, 2, 2, 1, 0 };
            var row = new StringBuilder(); bool same = veh != null, asWanted = true;
            for (int i = 0; i < ks.Length; i++)
            {
                Hp(s, ks[i]); Hp(veh, ks[i]); yield return Wait(0.35f);
                int a = OpenZDamageStates.StageOf(s.Id), b = veh != null ? OpenZDamageStates.StageOf(veh.Id) : -1;
                row.Append($" {ks[i] * 100f:0}%: shuttle {a} / {(veh != null ? veh.Type : "-")} {b} (wanted {want[i]});");
                same &= a == b; asWanted &= a == want[i];
            }
            Log("thresholds:" + row);
            Check($"S1 the same thresholds as the other vehicles (SmokeAt {OpenZDamageStates.SmokeAt:0.00}, BurnAt {OpenZDamageStates.BurnAt:0.00}, OutAt {OpenZDamageStates.OutAt:0.00}): every step as wanted and as the {(veh != null ? veh.Type : "ground vehicle")}'s", same && asWanted);
            Hp(veh, 1f);

            // S2 the points against the drawn hull's skin
            Hp(s, 0.20f); yield return Wait(0.5f);
            HullTest();

            // S3 standing, battered
            Hp(s, 0.60f); yield return Wait(0.6f);
            float t0 = Time.unscaledTime; OpenZDamageStates.ShipProofReset();
            Hp(s, 0.45f); yield return Wait(2.5f);
            Check($"S3 standing at 45 %: grey smoke from {OpenZDamageStates.ShipPointsUsed} points ({OpenZDamageStates.ShipBeatPuffs} puffs in 2.5 s, wanted 2 points, >= 8 puffs), the clank heard {Heard(t0, "dst_debris_metal")}x",
                  OpenZDamageStates.StageOf(s.Id) == 1 && OpenZDamageStates.ShipPointsUsed == 2 && OpenZDamageStates.ShipBeatPuffs >= 8 && Heard(t0, "dst_debris_metal") >= 1);
            Shot("K393_ground_smoke_70m"); yield return null; yield return null;

            // S4 standing, burning
            t0 = Time.unscaledTime; OpenZDamageStates.ShipProofReset(); int ign0 = OpenZDamageStates.Ignited;
            Hp(s, 0.20f); yield return Wait(3f);
            Check($"S4 standing at 20 %: burning (stage {OpenZDamageStates.StageOf(s.Id)}), the small fire on its pod {(OpenZDamageStates.StickyOf(s.Id) ? "lit" : "NOT lit (the flame of the beat only)")}, {OpenZDamageStates.ShipFireTicks} fire ticks, {OpenZDamageStates.ShipPointsUsed} points; ignite heard {Heard(t0, "dst_fire_ignite")}x, the fire loop {Heard(t0, "dst_fire_loop")}x",
                  OpenZDamageStates.StageOf(s.Id) == 2 && OpenZDamageStates.ShipFireTicks >= 6 && OpenZDamageStates.ShipPointsUsed == 3 && OpenZDamageStates.Ignited > ign0 && Heard(t0, "dst_fire_ignite") + Heard(t0, "dst_fire_loop") >= 1);
            Shot("K393_ground_fire_70m"); yield return null; yield return null;

            // S5 LOW: one point less
            OpenZDamageStates.ProofLite = 1; yield return Wait(0.8f);
            int lowBurn = OpenZDamageStates.ShipPointsUsed;
            Hp(s, 0.45f); yield return Wait(0.8f);
            int lowSmoke = OpenZDamageStates.ShipPointsUsed;
            OpenZDamageStates.ProofLite = 0;
            Check($"S5 LOW: {lowBurn} points burning, {lowSmoke} battered (wanted 2 and 1; the base beat as the others')", lowBurn == 2 && lowSmoke == 1);

            // S6 in flight, battered: the trail
            Hp(s, 0.60f); yield return Wait(0.6f);
            MoveTo(A);
            yield return WaitFor(() => s.ShuttleState == ZShuttleState.Cruise && s.ShuttleSpeedPx > 0.6 * s.RealMoveSpeed, 30f);
            follow = true; OpenZDamageStates.ShipProofReset(); Hp(s, 0.45f);
            int airF = 0; float vMax = 0f;
            for (float t = 0f; t < 2.2f; t += Time.deltaTime) { yield return null; if (s.ShuttleAirborne) { airF++; vMax = Mathf.Max(vMax, ship.Velocity.magnitude); } }
            float sp = OpenZDamageStates.TrailSpacingM;
            Check($"S6 in flight at 45 %: {OpenZDamageStates.ShipTrailPuffs} trail puffs + {OpenZDamageStates.ShipBeatPuffs} on the beat in {airF} frames aloft (up to {vMax:F1} m/s), the widest gap between two puffs of a point {OpenZDamageStates.ShipTrailGapMaxM:F2} m (spacing {sp:F2} m)",
                  airF > 60 && OpenZDamageStates.ShipTrailPuffs >= 20 && OpenZDamageStates.ShipAirPuffs > 0 && OpenZDamageStates.ShipTrailGapMaxM <= sp * 1.05f + 0.01f);
            Shot("K393_air_smoke_70m"); yield return null; yield return null;

            // S7 in flight, burning (on the way back)
            yield return WaitFor(() => !s.ShuttleAirborne && s.ShuttleState == ZShuttleState.Landed, 40f);
            yield return Wait(1f);
            MoveTo(P0);
            yield return WaitFor(() => s.ShuttleState == ZShuttleState.Cruise && s.ShuttleSpeedPx > 0.6 * s.RealMoveSpeed, 30f);
            OpenZDamageStates.ShipProofReset(); Hp(s, 0.20f); airF = 0;
            for (float t = 0f; t < 2.2f; t += Time.deltaTime) { yield return null; if (s.ShuttleAirborne) airF++; }
            Check($"S7 in flight at 20 %: burning (stage {OpenZDamageStates.StageOf(s.Id)}), {OpenZDamageStates.ShipFireTicks} fire ticks ({OpenZDamageStates.ShipFlyingFlames} flame tongues riding on the pod), {OpenZDamageStates.ShipTrailPuffs} trail puffs in {airF} frames aloft, no oil stain in the air ({OpenZDamageStates.ShipStains})",
                  airF > 60 && OpenZDamageStates.StageOf(s.Id) == 2 && OpenZDamageStates.ShipFireTicks >= 4 && OpenZDamageStates.ShipFlyingFlames >= 2 && OpenZDamageStates.ShipTrailPuffs >= 20 && OpenZDamageStates.ShipStains == 0);
            Shot("K393_air_fire_70m"); yield return null; yield return null;
            follow = false;
            yield return WaitFor(() => !s.ShuttleAirborne && s.ShuttleState == ZShuttleState.Landed, 40f);
            yield return Wait(1f);

            // S8 R to the repair station
            ZObject station = null;
            foreach (var f in W.Flags) foreach (var b in f.LinkedObjects) if (b.Type == "repair" && station == null) { station = b; if (b.Owner != me) W.AwardZone(f, me); }
            if (station == null) Check("S8 a repair station on the level", false);
            else
            {
                Hp(s, 0.20f); yield return Wait(0.5f);
                input.Selection.Clear(); input.Selection.Add(s);
                t0 = Time.unscaledTime; int out0 = OpenZDamageStates.Extinguished;
                bool sent = !s.Destroyed && input.SendToRepair(); follow = true;
                Log($"S8 (K419b): the ship {(s.Destroyed ? "LOST before the repair stage (NOT PROVEN)" : "alive")}, {s.Health}/{s.MaxHealth}; repair stations for {me}: {W.Objects.Count(o => o != null && o.Kind == ZKind.Building && !o.Destroyed && o.CanRepairUnit(me))} (the zone's station #{station.Id}, {station.Owner}); the R order {(sent ? "taken" : "NOT taken")}");
                yield return WaitFor(() => s.Health * 2 > s.MaxHealth && OpenZDamageStates.StageOf(s.Id) == 0, 180f);
                follow = false; Cam(ship.transform.position, 70f);
                yield return Wait(0.5f);
                int p0 = OpenZDamageStates.ShipBeatPuffs + OpenZDamageStates.ShipTrailPuffs;
                yield return Wait(1.5f);
                int p1 = OpenZDamageStates.ShipBeatPuffs + OpenZDamageStates.ShipTrailPuffs;
                Check($"S8 R taken ({sent}); repaired to {s.Health}/{s.MaxHealth}: stage {OpenZDamageStates.StageOf(s.Id)}, the fire out ({OpenZDamageStates.Extinguished - out0}x, steam hiss heard {Heard(t0, "dtl_steam_hiss")}x), puffs in the 1.5 s after: {p1 - p0}",
                      sent && OpenZDamageStates.StageOf(s.Id) == 0 && OpenZDamageStates.Extinguished > out0 && p1 == p0);
                Shot("K393_repaired_70m"); yield return null; yield return null;
                input.Selection.Clear();
            }
            Finish();
        }

        /// <summary>S2: test-only colliders of the drawn hull and pods (SH_Hull: the door kit's hull in Play, SH_Engine_L / _R); each emitter point is
        /// over its skin (a ray down from 20 m above meets nothing before the point) and seen from the camera (nothing between); the
        /// others' old spots (root 0 / 4.0 / 0 and 0 / 2.4 / 1.3) lie under the skin.</summary>
        void HullTest()
        {
            var parts = ship.transform.GetComponentsInChildren<MeshFilter>(true).Where(m => m.name == "SH_Hull" || m.name == "SH_Engine_L" || m.name == "SH_Engine_R").ToList();
            var cols = new List<MeshCollider>(); var made = new List<GameObject>();
            foreach (var mf in parts)
            {
                if (!mf.sharedMesh || !mf.sharedMesh.isReadable) continue;
                var go = new GameObject("K393_HullTest_" + mf.name); go.transform.SetParent(mf.transform, false); made.Add(go);
                var mc = go.AddComponent<MeshCollider>(); mc.sharedMesh = mf.sharedMesh; cols.Add(mc);
            }
            if (cols.Count < 3) { foreach (var go in made) Destroy(go); Log($"S2 skipped: {cols.Count} of the 3 body meshes (hull, two pods) are readable (the off-engine test in the kit's out/ covers it)"); return; }
            bool Hit(Ray r, float max) { foreach (var c in cols) if (c.Raycast(r, out _, max)) return true; return false; }
            var cam = Camera.main;
            int over = 0, seen = 0, n = OpenZDamageStates.ShipPointsUsed; var sbp = new StringBuilder();
            for (int i = 0; i < n; i++)
            {
                var p = OpenZDamageStates.ShipLastPoints[i];
                bool under = Hit(new Ray(p + Vector3.up * 20f, Vector3.down), 19.98f);
                bool hid = false;
                if (cam) { var to = cam.transform.position - p; hid = Hit(new Ray(p + to.normalized * 0.02f, to.normalized), to.magnitude); }
                if (!under) over++; if (!hid) seen++;
                sbp.Append($" [{i}] y {p.y - ship.transform.position.y:F2} m over the root: {(under ? "UNDER the skin" : "over the skin")}, {(hid ? "HIDDEN from the camera" : "seen")};");
            }
            int oldUnder = 0;
            foreach (var lp in new[] { new Vector3(0f, 4.0f, 0f), new Vector3(0f, 2.4f, 1.3f) })
                if (Hit(new Ray(ship.transform.TransformPoint(lp) + Vector3.up * 20f, Vector3.down), 19.98f)) oldUnder++;
            foreach (var go in made) Destroy(go);
            Log("emitter points:" + sbp);
            Check($"S2 all {n} emitter points over the drawn skin of the hull and pods ({over}) and seen from the camera ({seen})", n == 3 && over == n && seen == n);
            Log($"S2 note (K420, not counted): the others' old spots (no longer used for the ship) under today's skin: {oldUnder} of 2");
        }

        void Finish()
        {
            follow = false; OpenZDamageStates.ProofLite = -1;
            if (s != null && !s.Destroyed) Hp(s, 1f);
            if (veh != null && !veh.Destroyed && vehHp0 > 0) { veh.SetHealth(vehHp0); W.UpdateObjectHealth(veh); }
            Log($"K393 damage smoke proof: {passes} PASS, {fails} FAIL (expected 8 PASS)");
            Report = $"{passes} PASS, {fails} FAIL";
            try { System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "damage_smoke_proof_log.txt"), sb.ToString()); } catch (System.Exception e) { Debug.LogWarning("[OzShuttleDamageSmokeProof] log not written: " + e.Message); }
            Destroy(gameObject, 1f);
        }
    }
}
