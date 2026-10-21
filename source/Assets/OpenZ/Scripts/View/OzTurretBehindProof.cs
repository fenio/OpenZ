using System.Collections;
using System.Collections.Generic;
using System.Text;
using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// K433 Play proof (item 432, View/UnitAim.BehindFix.cs): a missile cannon whose target stands behind it turns round and fires from
    /// the front. Any level in Play (menu OpenZ/V1.1/Combat: K433 Turret Behind Proof, Editor/OzTurretBehindMenu.cs). Set-up on a free
    /// open spot away from every other unit: a red missile cannon and, 6 tiles east of it, a blue laser squad that cannot be hurt and does
    /// no harm (the cannon's punching bags, as in CombatViewCheck). Trials: the moment the cannon has its laser target, its turret is put
    /// at rest with the barrels pointing (180 + eps) deg away from it, then 3.5 s are watched:
    ///  eps 0, +1, -1, +2.5, -2.5, +4, -4 (straight behind, inside the old lock band) and 90, -135 (ordinary turns, unchanged):
    ///   on the laser within 2.0 s; every rocket leaves from the front (the gun's own tube error at launch, UnitAim's counts, at most
    ///   20 deg; the old fault gave about 175) and none leaves tubes that point away; still on it after 2 s (within 25 deg);
    ///  the split case really happened (UnitAim.BehindFixed rose in the straight-behind trial);
    ///  comparison (INFO, not counted): eps 0 with the fix off for that one trial (then on again): today's lock, if it shows.
    /// Pictures from the normal RTS camera: the fix on and the fix off, 1.5 s into the straight-behind trial.
    /// Output: <see cref="Folder"/> (a folder of the work tree's records, found upward at run time). Test only.
    /// </summary>
    public sealed class OzTurretBehindProof : MonoBehaviour
    {
        public static string Report = "idle";
        public const float Watch = 3.5f, TurnBy = 2.0f, StayDeg = 25f, FrontDeg = 20f;   // the old fault: about 175 deg
        static readonly float[] Eps = { 0f, 1f, -1f, 2.5f, -2.5f, 4f, -4f, 90f, -135f };

        public static string Folder()
        {
            var d = new System.IO.DirectoryInfo(Application.dataPath);
            for (int i = 0; i < 8 && d != null; i++, d = d.Parent)
                if (System.IO.Directory.Exists(System.IO.Path.Combine(d.FullName, "work", "records")))
                    return System.IO.Path.Combine(d.FullName, "work", "records", "milestones", "turret_behind_k433");
            return System.IO.Path.Combine(Application.persistentDataPath, "turret_behind_k433");
        }

        public static void Run()
        {
            Report = "running";
            new GameObject("OzTurretBehindProof").AddComponent<OzTurretBehindProof>();
        }

        ZGame g; ZWorld W; RtsCamera rts; string dir;
        readonly StringBuilder sb = new StringBuilder(), table = new StringBuilder(); int passes, fails;
        ZObject cannon, squad; UnitAim aim;

        void Start()
        {
            g = ZGame.I; W = g ? g.World : null; rts = FindFirstObjectByType<RtsCamera>();
            dir = Folder(); System.IO.Directory.CreateDirectory(dir);
            StartCoroutine(All());
        }

        void Log(string t) { sb.AppendLine(t); Debug.Log("[OzTurretBehindProof] " + t); }
        void Check(string what, bool ok) { if (ok) passes++; else fails++; Log((ok ? "PASS " : "FAIL ") + what); }
        static string F(float v) => v.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
        static float Bearing(Vector3 from, Vector3 to) { var d = to - from; return Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg; }
        void Shot(string name) { ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, name + ".png")); Log("   picture " + name + ".png"); }

        /// <summary>A free open spot: the cannon's 2x2 tiles and the squad's tile 6 east passable, no other unit within 26 tiles.</summary>
        bool FindSpot(out int cx, out int cy)
        {
            int w = W.Map.width, h = W.Map.height, mx = w / 2, my = h / 2;
            for (int r = 0; r < Mathf.Max(w, h); r += 2)
                for (int dx = -r; dx <= r; dx += 2)
                    for (int dy = -r; dy <= r; dy += 2)
                    {
                        if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy)) != r) continue;
                        int x = mx + dx, y = my + dy;
                        if (x < 4 || y < 4 || x + 12 >= w || y + 4 >= h) continue;
                        bool ok = W.Path.TilePassable(x, y, false) && W.Path.TilePassable(x + 1, y, false) && W.Path.TilePassable(x, y + 1, false) && W.Path.TilePassable(x + 1, y + 1, false);
                        for (int k = 5; ok && k <= 8; k++) ok = W.Path.TilePassable(x + k, y, true) && W.Path.TilePassable(x + k, y + 1, true);
                        if (!ok) continue;
                        bool alone = true;
                        foreach (var o in W.Objects)
                        {
                            if (!o.IsUnit || o.Destroyed || o.Owner == Team.Neutral) continue;
                            long ddx = o.CenterX - (x * 16 + 16), ddy = o.CenterY - (y * 16 + 16);
                            if (ddx * ddx + ddy * ddy < 416L * 416L) { alone = false; break; }
                        }
                        if (alone) { cx = x; cy = y; return true; }
                    }
            cx = cy = 0; return false;
        }

        IEnumerator WaitFor(System.Func<bool> c, float max) { float t = 0f; while (t < max && !c()) { t += Time.deltaTime; yield return null; } }

        IEnumerator All()
        {
            bool fix0 = UnitAim.BehindFixOn;
            try
            {
                if (W == null) { Check("a game is running in Play", false); yield break; }
                int bots = W.Bots.Count; W.Bots.Clear();                                              // as the K408 proof: no CPU orders the test squad away
                Log($"set-up: CPU removed {bots} (for the rest of this Play session)");
                if (!FindSpot(out int cx, out int cy)) { Check("a free open spot on this level", false); yield break; }
                cannon = W.CreateObject("cannon", "missile_cannon", cx * 16, cy * 16, Team.Red); g.SpawnUnitView(cannon);
                yield return WaitFor(() => g.Views.TryGetValue(cannon.Id, out var v) && v && v.GetComponent<UnitAim>() && v.GetComponent<UnitAim>().HasRig, 3f);
                aim = g.Views.TryGetValue(cannon.Id, out var cv) && cv ? cv.GetComponent<UnitAim>() : null;
                Check($"the missile cannon #{cannon.Id} at tile {cx},{cy} has its gun rig (yaw joint and tubes)", aim != null && aim.HasRig);
                if (aim == null || !aim.HasRig) yield break;
                yield return new WaitForSeconds(1f);
                squad = W.CreateRobotGroup("laser", (cx + 6) * 16, cy * 16, Team.Blue);
                var lasers = new List<ZObject> { squad }; lasers.AddRange(squad.Minions);
                foreach (var o in lasers) { o.MaxHealth = o.Health = 1000000000; o.Damage = 0; g.SpawnUnitView(o); }
                Log($"set-up: level {g.level}; red missile cannon #{cannon.Id} at tile {cx},{cy}; blue laser squad #{squad.Id} ({lasers.Count} robots, cannot be hurt, harmless) 6 tiles east; tolerance {F(aim.ProofTolerance)} deg");
                if (rts) { rts.FreeLookOff(); var c0 = g.CenterWorld(cannon); rts.focus = new Vector3(c0.x + 5f, 0f, c0.z); rts.SetHeightImmediate(45f); }
                table.AppendLine("| trial | fix | on the laser at | worst after 2 s | rockets | worst error at launch | released off target / snapped | split barrels fixed |");
                table.AppendLine("|---|---|---|---|---|---|---|---|");
                foreach (var e in Eps) yield return Trial(e, true);
                yield return Trial(0f, false);
            }
            finally
            {
                UnitAim.BehindFixOn = fix0;
                Finish();
            }
        }

        IEnumerator Trial(float eps, bool fixOn)
        {
            string tag = (fixOn ? "T" : "OFF") + (eps >= 0 ? "+" : "") + F(eps);
            UnitAim.BehindFixOn = true;
            yield return new WaitForSeconds(1.5f);                                                    // settle on the target with the fix on
            UnitAim.BehindFixOn = fixOn;
            Vector3 p = default;
            yield return WaitFor(() => cannon.AttackObject != null && aim && aim.ProofTarget(out p), 6f);
            if (!aim || cannon.AttackObject == null || !aim.ProofTarget(out p)) { Check(tag + " the cannon has its laser target", false); yield break; }
            float hb = Bearing(aim.ProofPivot, p);
            aim.ProofSetHeading(hb + 180f + eps);
            UnitAim.StatById.Remove(cannon.Id);
            int fixed0 = UnitAim.BehindFixed;
            float t = 0f, alignedAt = -1f, worstLate = 0f, tol = aim.ProofTolerance; bool pictured = false;
            while (t < Watch)
            {
                yield return null; t += Time.deltaTime;
                if (!aim) break;
                if (aim.ProofTarget(out var q) && aim.ProofBarrelHeading(out float h))
                {
                    float err = Mathf.Abs(Mathf.DeltaAngle(Bearing(aim.ProofPivot, q), h));
                    if (alignedAt < 0f && err <= tol) alignedAt = t;
                    if (t > TurnBy) worstLate = Mathf.Max(worstLate, err);
                }
                if (!pictured && t >= 1.5f && eps == 0f) { pictured = true; Shot("k433_" + (fixOn ? "fix_on" : "fix_off") + "_behind_1p5s_h45"); }
            }
            UnitAim.ShotStat st; UnitAim.StatById.TryGetValue(cannon.Id, out st);
            int shots = st != null ? st.Shots : 0, timeouts = st != null ? st.Timeouts : 0, snaps = st != null ? st.Snaps : 0; float maxErr = st != null ? st.MaxErr : 0f;
            int fixedN = UnitAim.BehindFixed - fixed0;
            table.AppendLine($"| eps {F(eps)} | {(fixOn ? "on" : "OFF")} | {(alignedAt >= 0f ? F(alignedAt) + " s" : "never")} | {F(worstLate)} deg | {shots} | {F(maxErr)} deg | {timeouts} / {snaps} | {fixedN} |");
            if (!fixOn)
            {
                bool showed = alignedAt < 0f || alignedAt > TurnBy || maxErr > 90f;
                Log($"INFO {tag} the fix off for this one trial: {(showed ? "today's fault showed" : "the fault did not show this time (a scattered rocket can break the lock)")}: on the laser {(alignedAt >= 0f ? "at " + F(alignedAt) + " s" : "never")}, {shots} rockets, worst error at launch {F(maxErr)} deg");
                yield break;
            }
            Check($"{tag} on the laser within {F(TurnBy)} s (at {(alignedAt >= 0f ? F(alignedAt) + " s" : "never")})", alignedAt >= 0f && alignedAt <= TurnBy);
            Check($"{tag} every rocket left from the front, none from its back: {shots} rockets, worst tube error at launch {F(maxErr)} deg (at most {F(FrontDeg)}; aim tolerance {F(tol)}, outer tubes add their side offset)", shots >= 1 && maxErr <= FrontDeg);
            Check($"{tag} stays on the laser after {F(TurnBy)} s (worst {F(worstLate)} deg, at most {F(StayDeg)} with the tube kick and the swing back from a scattered rocket)", worstLate <= StayDeg);
            if (eps == 0f) Check($"{tag} the barrels' split errors happened and were fixed ({fixedN} moved)", fixedN > 0);
        }

        void OnDestroy() { UnitAim.BehindFixOn = true; }                                             // stopped mid-trial (a level change): the fix never stays off

        void Finish()
        {
            Log("");
            Log(table.ToString());
            Log($"RESULT {passes} PASS, {fails} FAIL");
            Report = $"{passes} PASS, {fails} FAIL";
            try { System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "turret_behind_proof_log.txt"), sb.ToString()); } catch (System.Exception ex) { Debug.LogWarning("[OzTurretBehindProof] log not written: " + ex.Message); }
            Destroy(gameObject, 1f);
        }
    }
}
