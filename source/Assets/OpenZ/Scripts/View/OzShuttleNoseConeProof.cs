#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.Text;
using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// K256d2, the Play proof of the nose searchlight's clear cone (editor only; menu OpenZ/V1.1/Shuttle: K256d2 Nose Light Clear Cone
    /// Proof (Level 16, Play)). Level 16 at night (the clock held at 0.62), the CPU's orders off, the shuttle hovering over ground it cannot
    /// land on, the camera at the standard 70 m:
    /// <list type="bullet">
    /// <item>P1 an enemy robot beside the ship, past the clear cone's edge but inside the old 110 deg lock zone (95 deg from the ship's
    /// heading: about 100 to 108 deg from the nose light, at the distance where it is about 20 deg under it; the old rules took it): not
    /// taken, the light sweeps on ahead. Picture 01.</item>
    /// <item>P2 an enemy inside the cone (75 deg): taken with the beep, alarming, the beam on it. Picture 02.</item>
    /// <item>P3 it walks round to 125 deg behind the side: the beam eases into the edge and holds there, still alarming, then lets go and
    /// goes back to the calm white sweep. Picture 03 (THE picture: the enemy behind the side, the beam held at the edge, nothing through
    /// the pod or the leg), its close twin at 30 m, picture 04 after the let-go.</item>
    /// <item>Every frame: the drawn beam (the drum's aim in the ship's root space, the cone as wide as it is drawn) against the ship's own
    /// model files (OzNoseConeShipModel): frames through the ship must be 0.</item>
    /// </list>
    /// The clock and CALM MODE go back when it ends or is stopped (the CPU's orders stay off for the rest of that Play session, as in the
    /// K256d proof). Its enemies never fire and are removed.
    /// </summary>
    public sealed partial class OzShuttleNoseConeProof : MonoBehaviour                            // + OzShuttleNoseConeProof.P1.cs (P1 staged past the edge)
    {
        public static string Report = "idle";
        /// <summary>The output folder: the work tree's records folder found upward from the project (milestones/shuttle_nose_light_cone_proof),
        /// else the player's own data folder.</summary>
        public static string Folder()
        {
            const string sub = "shuttle_nose_light_cone_proof";
            var d = new System.IO.DirectoryInfo(Application.dataPath);
            for (int i = 0; i < 8 && d != null; i++, d = d.Parent)
                if (System.IO.Directory.Exists(System.IO.Path.Combine(d.FullName, "work", "records")))
                    return System.IO.Path.Combine(d.FullName, "work", "records", "milestones", sub);
            return System.IO.Path.Combine(Application.persistentDataPath, sub);
        }
        public static void Run()
        {
            Report = "running";
            var gv = System.Type.GetType("UnityEditor.GameView,UnityEditor"); if (gv != null) UnityEditor.EditorWindow.FocusWindowIfItsOpen(gv);
            new GameObject("OzShuttleNoseConeProof").AddComponent<OzShuttleNoseConeProof>();
        }

        ZGame g; ZWorld W; RtsCamera rts; DayNight dn; ZObject s; OzPlayableShuttle ship; OzShuttleNightLights night; Team me = Team.Red, them = Team.Blue;
        readonly StringBuilder sb = new StringBuilder(); int fails, passes; string dir; float clock0 = 0.10f; bool running0, calm0;
        readonly List<ZObject> enemies = new List<ZObject>(); Vector3? look;
        OzNoseConeShipModel model; OzNoseConeShipModel.Probe probe;
        int beamFrames, throughFrames; float worstPast = -999f; string firstThrough = "";

        void Start()
        {
            g = ZGame.I; W = g ? g.World : null; rts = FindFirstObjectByType<RtsCamera>(); dn = FindFirstObjectByType<DayNight>();
            dir = Folder(); System.IO.Directory.CreateDirectory(dir);
            calm0 = OzMotion.Reduced;
            if (dn) { clock0 = dn.timeOfDay; running0 = dn.running; }
            StartCoroutine(All());
        }

        void OnDestroy()
        {   // everything the proof changed goes back, even when it is stopped half way
            if (OzMotion.Reduced != calm0) OzMotion.Reduced = calm0;
            if (dn) { dn.timeOfDay = clock0; dn.Apply(clock0); dn.running = running0; }
            foreach (var e in enemies) Vanish(e);
        }

        void Log(string t) { sb.AppendLine(t); Debug.Log("[OzShuttleNoseConeProof] " + t); }
        void Check(string what, bool ok) { if (ok) passes++; else fails++; Log((ok ? "PASS " : "FAIL ") + what); }
        void Shot(string name)
        {
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, name + ".png"));
            Log($"frame {name}.png (camera height {(rts ? rts.height : -1f):F1} m)");
        }
        IEnumerator ShotAt(string name, Vector3 at, float height)
        {
            look = at; if (rts) rts.SetHeightImmediate(height); yield return null; yield return null; yield return null;
            Shot(name); yield return OzProofPictures.AfterGrab();
            if (rts) rts.SetHeightImmediate(70f); look = null; yield return null;
        }
        IEnumerator WaitFor(System.Func<bool> c, float max) { float t = 0f; while (t < max && !c()) { t += Time.deltaTime; yield return null; } }
        IEnumerator Wait(float s0) { float t = 0f; while (t < s0) { t += Time.deltaTime; yield return null; } }
        void SetClock(float t) { if (dn) { dn.timeOfDay = t; dn.Apply(t); } }
        Vector3 PosOf(ZObject o) { var v = o != null && g.Views.TryGetValue(o.Id, out var uv) ? uv : null; return v ? v.transform.position : (o != null ? ZGame.PxToWorld(o.CenterX, o.CenterY) : Vector3.zero); }
        void Vanish(ZObject o)
        {
            if (o == null || g == null) return;
            var all = new List<ZObject> { o }; all.AddRange(o.Minions);
            foreach (var x in all) { x.KillAt = 0; if (g.Views.TryGetValue(x.Id, out var v)) { g.Views.Remove(x.Id); if (v) Destroy(v.gameObject); } }
        }

        /// <summary>A map point (px) this far (m) from the ship's ground point at this angle from its heading (as the K256d proof's enemies).</summary>
        Vector2Int At(float distM, float offDeg)
        {
            double a = (s.ShuttleHeadingDeg + offDeg) * System.Math.PI / 180.0;
            return new Vector2Int((int)(s.CenterX + System.Math.Cos(a) * distM * 8.0), (int)(s.CenterY + System.Math.Sin(a) * distM * 8.0));
        }
        /// <summary>A blue robot there: it never fires and is never shot dead (the proof removes it itself).</summary>
        ZObject Enemy(float distM, float offDeg)
        {
            var q = At(distM, offDeg); int tx = q.x >> 4, ty = q.y >> 4;
            OzCheatSpawner.FreeTile(W, tx, ty, 1, true, out tx, out ty);
            var o = W.CreateRobotGroup("grunt", tx * 16, ty * 16, them, 1); if (o == null) return null;
            g.SpawnUnitView(o); foreach (var m in o.Minions) g.SpawnUnitView(m);
            o.Damage = 0; o.AttackRadius = 0; o.MaxHealth = o.Health = 1000000000;
            enemies.Add(o);
            return o;
        }
        /// <summary>The enemy as the nose searchlight sees it: its turn from the nose and drop (deg).</summary>
        bool SeenAt(ZObject o, out float yaw, out float drop)
        {
            yaw = drop = 0f; var h = night != null ? night.NoseHead : null;
            if (o == null || !h || !g.Views.TryGetValue(o.Id, out var v) || !v) return false;
            var q = v.transform.position; q.y = g.GroundY(q.x, q.z) + OzShuttleNightLights.AimRobotM;
            var d = ship.transform.InverseTransformDirection(q - h.parent.position);
            OzNoseConeBeam.Aim(d.x, d.y, d.z, out double yy, out double pp); yaw = (float)yy; drop = (float)pp;
            return true;
        }
        int[] Spot(System.Func<string, bool> want, int near)
        {
            long best = long.MaxValue; int[] r = null;
            for (int ty = 4; ty < W.Map.height - 4; ty++)
                for (int tx = 4; tx < W.Map.width - 4; tx++)
                {
                    int x = tx * 16 + 8, y = ty * 16 + 8; long d = (long)(x - s.CenterX) * (x - s.CenterX) + (long)(y - s.CenterY) * (y - s.CenterY);
                    if (d >= best || d < (long)near * near) continue;
                    W.ShuttlePadFree(x, y, s, out string why);
                    if (want(why)) { best = d; r = new[] { x, y }; }
                }
            return r;
        }

        // ------------------------------------------------------------------ per frame: the camera, the drawn beam against the model
        void LateUpdate()
        {
            if (s == null || !ship || night == null || model == null) return;
            if (look.HasValue && rts) rts.focus = new Vector3(look.Value.x, 0f, look.Value.z);
            else if (rts) { var p = ship.transform.position; rts.focus = new Vector3(p.x, 0f, p.z); }
            var P = night.Plan; var h = night.NoseHead;
            if (!h || !night.NoseBeamShown || P.NoseStrength <= 0.001f) return;
            var d = ship.transform.InverseTransformDirection(h.forward);
            OzNoseConeBeam.Aim(d.x, d.y, d.z, out double yaw, out double drop);
            beamFrames++;
            worstPast = Mathf.Max(worstPast, Mathf.Abs((float)yaw) - OzShuttleNightPlan.ConeEdgeDeg((float)yaw, (float)drop));
            if (OzNoseConeBeam.Through(model, probe, yaw, drop, P.AngleDeg)) { throughFrames++; if (firstThrough == "") firstThrough = $" (first at {yaw:F1} / {drop:F1})"; }
        }

        // ------------------------------------------------------------------ the stages
        IEnumerator All()
        {
            Application.targetFrameRate = 60;
            if (W == null || W.PlayableShuttle == null || !OzPlayableShuttle.I) { Log("no playable shuttle (level 16 in Play, the option SHUTTLE on)"); fails++; Finish(); yield break; }
            s = W.PlayableShuttle; ship = OzPlayableShuttle.I; me = s.Owner; them = me == Team.Red ? Team.Blue : Team.Red;
            night = ship.Searchlights != null ? ship.Searchlights.Night : null;
            model = OzNoseConeShipModel.Load(System.IO.Path.GetDirectoryName(Application.dataPath));
            Check($"the ship's night lights are built and its model read ({model.Tris} triangles{(model.Error != null ? ", " + model.Error : "")})", night != null && !night.Dead && night.NoseHead && model.Error == null && model.Tris > 10000);
            if (night == null || model.Error != null) { model = null; Finish(); yield break; }
            probe = OzNoseConeBeam.ProbeOf(model);
            W.Bots.Clear();
            if (OzMotion.Reduced) OzMotion.Reduced = false;
            SetClock(0.62f); if (dn) dn.running = false;
            if (rts) { rts.FreeLookOff(); rts.SetHeightImmediate(70f); }
            Log($"K256d2 nose light clear cone proof, {UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}, {Screen.width}x{Screen.height}; set-up: CPU orders off, night (clock 0.62) held, camera 70 m, CALM MODE off");
            if (s.ShuttleState != ZShuttleState.Landed && s.ShuttleState != ZShuttleState.Hover) yield return WaitFor(() => s.ShuttleState == ZShuttleState.Landed || s.ShuttleState == ZShuttleState.Hover, 40f);
            if (s.ShuttleState != ZShuttleState.Hover)
            {   // a place it cannot land on: it flies there and hovers
                var at = Spot(why => why == "rocks" || why == "building" || why == "water", 160) ?? Spot(why => why != "", 160);
                if (at == null) { Check("a place to hover over (rocks, a building, water)", false); Finish(); yield break; }
                bool sent = W.SendWaypoints(me, s, new[] { new ZWaypoint { mode = WpMode.Move, x = at[0], y = at[1], playerGiven = true } });
                Check("the move order to a place it cannot land on is taken", sent);
                yield return WaitFor(() => s.ShuttleState == ZShuttleState.Hover, 45f);
            }
            yield return Wait(2.5f);
            Check($"it hovers at night, the nose searchlight on ({night.Plan.Nose:F2}; feet {ship.FeetHeight:F1} m)", s.ShuttleState == ZShuttleState.Hover && night.Plan.Nose > 0.9f);
            // the distance at which an enemy is about 20 deg under the nose light
            var hp = night.NoseHead.parent.position; float drop0 = hp.y - (g.GroundY(hp.x, hp.z) + OzShuttleNightLights.AimRobotM);
            float dist = Mathf.Clamp(drop0 / Mathf.Tan(20f * Mathf.Deg2Rad), 10f, 26f);
            Log($"the nose light {drop0:F1} m over a robot's middle: enemies at {dist:F1} m ({Mathf.Atan2(drop0, dist) * Mathf.Rad2Deg:F1} deg under it)");
            // the old rules never took either): not taken, the sweep goes on
            int acq0 = night.Plan.Acquires, beeps0 = night.Plan.Beeps; float t = 0f;                        // acq0, beeps0 and t: used again by P2 and P3
            ZObject e1 = null; yield return P1PastEdge(o => e1 = o);                                          // P1 staged past the edge at the real hover height, standing still, judged every frame (OzShuttleNoseConeProof.P1.cs)
            yield return ShotAt("k256d2_01_enemy_behind_side_light_sweeps_ahead_h70", Vector3.Lerp(ship.transform.position, PosOf(e1), 0.5f), 70f);
            Vanish(e1); enemies.Remove(e1);
            // P2 inside: taken, alarming, the beam on it
            var e2 = Enemy(dist, 75f); acq0 = night.Plan.Acquires; beeps0 = night.Plan.Beeps;
            yield return WaitFor(() => night.Plan.Locked && night.Plan.LockId == (e2 != null ? e2.Id : -9), 4f); yield return Wait(1.5f);
            SeenAt(e2, out float y2, out float d2);
            float off2 = night.NoseHead ? Vector3.Angle(night.NoseHead.forward, PosOf(e2) + Vector3.up * OzShuttleNightLights.AimRobotM - night.NoseHead.position) : 180f;
            Log($"P2 the enemy #{(e2 != null ? e2.Id : -1)} at {y2:F1} deg, {d2:F1} down: taken {night.Plan.Acquires - acq0}, beeps {night.Plan.Beeps - beeps0}, alarm {night.Plan.Alarm:F2}, the beam {off2:F1} deg off it");
            Check("P2 an enemy inside the cone is taken as before: the beep, alarming, the beam on it (<= 6 deg)", e2 != null && night.Plan.LockId == e2.Id && night.Plan.Beeps - beeps0 == 1 && night.Plan.Alarm >= 0.95f && off2 <= 6f);
            yield return ShotAt("k256d2_02_enemy_inside_taken_alarm_h70", Vector3.Lerp(ship.transform.position, PosOf(e2), 0.5f), 70f);
            // P3 it walks round behind the side: held at the edge, then let go
            var to = At(dist, 125f); int rel0 = night.Plan.Releases;
            bool walk = e2 != null && W.SendWaypoints(them, e2, new[] { new ZWaypoint { mode = WpMode.Move, x = to.x, y = to.y, playerGiven = true } });
            Check("P3 the enemy is sent round behind the side (a move order)", walk);
            bool held = false, shot3 = false; float heldOff = 0f, past = 0f; t = 0f;
            while (t < 20f && night.Plan.Releases == rel0)
            {
                if (SeenAt(e2, out float y3, out float d3))
                {
                    float edge = OzShuttleNightPlan.ConeEdgeDeg(y3, d3); past = Mathf.Abs(y3) - edge;
                    if (past >= 3f && night.Plan.LockId == e2.Id)
                    {
                        held = true; heldOff = Mathf.Max(heldOff, edge - Mathf.Abs(night.Plan.YawNow));
                        if (!shot3 && past >= 6f)
                        {
                            shot3 = true;
                            Log($"P3 held: the enemy at {y3:F1} deg, {d3:F1} down, {past:F1} deg past the edge ({edge:F1}); the beam at {night.Plan.YawNow:F1} / {night.Plan.PitchNow:F1}, alarm {night.Plan.Alarm:F2}");
                            yield return ShotAt("k256d2_03_enemy_behind_side_beam_held_at_edge_h70", Vector3.Lerp(ship.transform.position, PosOf(e2), 0.5f), 70f);
                            yield return ShotAt("k256d2_03_enemy_behind_side_beam_held_at_edge_extra_close", Vector3.Lerp(ship.transform.position, PosOf(e2), 0.5f), 30f);
                        }
                    }
                }
                t += Time.deltaTime; yield return null;
            }
            Log($"P3 let go {night.Plan.Releases - rel0} (the enemy {past:F1} deg past the edge then), the beam held within {heldOff:F2} deg of the edge while it was past it");
            Check("P3 a followed enemy walking behind the side: the beam holds at the edge (within 3 deg of it, never past it), then lets go once", held && shot3 && heldOff <= 3f && night.Plan.Releases - rel0 == 1);
            yield return Wait(2.5f);
            Check("P3 back to the calm white sweep after the let-go", night.Plan.LockId < 0 && night.Plan.Alarm <= 0.05f);
            yield return ShotAt("k256d2_04_after_let_go_white_sweep_h70", Vector3.Lerp(ship.transform.position, PosOf(e2), 0.5f), 70f);
            Finish();
        }

        void Finish()
        {
            Check($"every frame of the take: the drawn nose beam through the ship 0 of {beamFrames} frames{firstThrough}; never past the clear cone's edge (at most {worstPast:F2} deg past it)", beamFrames > 0 && throughFrames == 0 && worstPast <= 0.01f);
            foreach (var e in enemies) Vanish(e);
            enemies.Clear();
            Report = (fails == 0 ? "PASS" : "FAIL " + fails) + " (" + passes + " PASS)";
            Log("RESULT " + Report);
            try { System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "proof.txt"), sb.ToString()); } catch (System.Exception ex) { Debug.LogWarning("[OzShuttleNoseConeProof] " + ex.Message); }
            Destroy(gameObject);
        }
    }
}
#endif
