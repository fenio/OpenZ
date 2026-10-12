using System.Collections;
using System.Collections.Generic;
using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// Play-mode proof: each cannon type (gun, gatling, howitzer, missile cannon) is placed crewed on open
    /// ground; a blue dummy at 8 bearings in turn makes the turret swing there (the view aims at the sim target); after the swing a
    /// fixed-direction close camera renders the gun + gunner (after BoardingView / UnitAim / WeaponHold, execution order 1003) and the
    /// gunner's hips / feet are measured against the gun (distance of the hips from the turret pivot, height above the base top, and
    /// how far that moved during the last 0.5 s while the turret stood: a floating or lagging gunner shows up as drift). One sheet per
    /// </summary>
    [DefaultExecutionOrder(1003)]
    public class CrewSeatCheck : MonoBehaviour
    {
        public static string Report = "idle";
        public static string[] Types = { "gun", "gatling", "howitzer", "missile_cannon" };
        /// <summary>
        /// vehicle types (jeep, light, medium, heavy) are placed as vehicles with the same crew.</summary>
        public static string CrewType = "grunt";
        /// <summary>
        /// in one run (null = CrewType), Bearings 1 = one engaged bearing (135 deg), IdleShot = a frame of the idle crew before the
        /// target appears, SubDir / ReportName = where the frames and the table go; the CT1 table adds the commander shots and the
        /// sound keys played per combo.</summary>
        public static string[] Crews;
        public static int Bearings = 8;
        public static bool IdleShot;
        public static string SubDir = "crew_seats", ReportName = "crew_seat_check.md";
        public static float EngageWaitVehicle = 1.6f;
        /// <summary>
        /// not dead; view hidden), so the next combo's frames are clean.</summary>
        public static bool NoWreck;
        ZGame g; ZWorld W; readonly System.Text.StringBuilder sb = new System.Text.StringBuilder();
        Camera cam; RenderTexture rt; Texture2D tex; string dir;
        UnitView shotView; string shotPath; bool shotPending; float shotYaw;

        public static void Run() { Report = "running"; new GameObject("CrewSeatCheck").AddComponent<CrewSeatCheck>(); }

        void Start()
        {
            g = ZGame.I; W = g.World;
            dir = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "../../../../work/records/milestones/" + SubDir));
            System.IO.Directory.CreateDirectory(dir);
            cam = gameObject.AddComponent<Camera>(); cam.enabled = false; cam.fieldOfView = 30f; cam.nearClipPlane = 0.1f;
            var main = Camera.main; if (main) { cam.clearFlags = main.clearFlags; cam.backgroundColor = main.backgroundColor; cam.cullingMask = main.cullingMask; }
            rt = new RenderTexture(480, 360, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 }; tex = new Texture2D(480, 360, TextureFormat.RGB24, false);
            StartCoroutine(All());
        }

        /// <summary>A quiet test field: the passable tile with no object within 12 tiles, nearest the map centre; the RTS camera flies
        /// there so nothing is culled.</summary>
        Vector2Int Focus()
        {
            int wt = W.Path.WidthPix >> 4, ht = W.Path.HeightPix >> 4; Vector2Int best = new Vector2Int(wt / 2, ht / 2); float bd = float.MaxValue;
            for (int y = 8; y < ht - 8; y += 2)
                for (int x = 8; x < wt - 8; x += 2)
                {
                    bool ok = true;
                    for (int j = -3; j <= 3 && ok; j++) for (int i = -3; i <= 3 && ok; i++) ok = W.Path.TilePassable(x + i, y + j, false);
                    if (!ok) continue;
                    foreach (var o in W.Objects) if (!o.Destroyed && Mathf.Abs((o.CenterX >> 4) - x) < 12 && Mathf.Abs((o.CenterY >> 4) - y) < 12) { ok = false; break; }
                    if (!ok) continue;
                    float d = (x - wt / 2) * (x - wt / 2) + (y - ht / 2) * (y - ht / 2);
                    if (d < bd) { bd = d; best = new Vector2Int(x, y); }
                }
            var rc = FindFirstObjectByType<RtsCamera>(); if (rc) rc.FlyTo(ZGame.PxToWorld(best.x * 16 + 16, best.y * 16 + 16));
            sb.AppendLine($"test field: tile {best.x},{best.y}\n");
            return best;
        }

        static Transform Find(Transform root, string suffix) { foreach (var t in root.GetComponentsInChildren<Transform>(true)) if (t.name.EndsWith(suffix)) return t; return null; }

        IEnumerator All()
        {
            var f = Focus();
            bool ct1 = Crews != null;
            sb.AppendLine("| type | bearing | crew found | hips-pivot m | hips above gun root m | drift in turret frame, 30 frames, cm | turret yaw |" + (ct1 ? " commander shots | sounds played |" : ""));
            sb.AppendLine("|---|---|---|---|---|---|---|" + (ct1 ? "---|---|" : ""));
            int bad = 0;
            foreach (var crewType in Crews ?? new[] { CrewType })
            foreach (var type in Types)
            {
                OzCheatSpawner.FreeTile(W, f.x, f.y, 2, false, out int tx, out int ty);
                var gun = W.CreateObject(ZBuildLists.IsCannon(type) ? "cannon" : "vehicle", type, tx * 16, ty * 16, Team.Red); gun.DriverType = crewType; g.SpawnUnitView(gun);
                yield return new WaitForSeconds(ct1 ? 0.9f : 1.2f);
                if (IdleShot) { g.Views.TryGetValue(gun.Id, out var iv); shotView = iv; shotPath = System.IO.Path.Combine(dir, $"{crewType}_{type}_idle.png"); shotPending = true; shotYaw = 35f; yield return null; yield return null; }
                for (int k = 0; k < Bearings; k++)
                {
                    float deg = Bearings == 1 ? 135f : k * 45f;
                    float a = deg * Mathf.Deg2Rad;
                    float s0 = Time.unscaledTime; int c0 = BoardingView.CommanderShots;
                    int dx = Mathf.RoundToInt(Mathf.Cos(a) * 5), dy = Mathf.RoundToInt(Mathf.Sin(a) * 5);
                    var dummy = W.CreateRobotGroup("grunt", (tx + 1 + dx) * 16, (ty + 1 + dy) * 16, Team.Blue, 1);
                    dummy.MaxHealth *= 50; dummy.SetHealth(dummy.MaxHealth); dummy.MoveSpeed = 0; dummy.Damage = 0; g.SpawnUnitView(dummy);   // harmless: no return fire, no driver hits / snipes
                    gun.AttackObject = dummy;                                                          // test only: aim the gun there
                    yield return new WaitForSeconds(ct1 && !ZBuildLists.IsCannon(type) ? EngageWaitVehicle : 1.6f);
                    g.Views.TryGetValue(gun.Id, out var v);
                    var crew = FindCrew(v);
                    Transform hips = crew ? Find(crew.transform, "Hips") : null;
                    var pivot = v ? (Find(v.transform, "TurretYaw") ?? Find(v.transform, "Turret") ?? v.transform) : null;
                    // drift in the TURRET's frame: a seated / standing gunner rides with the turret, so his hips must stay put
                    // relative to the turret's yaw node whatever the turret does (idle scan, tracking, recoil)
                    Vector3 h0 = hips && pivot ? pivot.InverseTransformPoint(hips.position) : Vector3.zero;
                    float worst = 0f;
                    for (int fr = 0; fr < 30; fr++)
                    {
                        yield return null;
                        if (hips && pivot) worst = Mathf.Max(worst, (Vector3.Scale(pivot.InverseTransformPoint(hips.position) - h0, pivot.lossyScale)).magnitude);
                    }
                    Vector3 h1 = hips ? hips.position : Vector3.zero;
                    float drift = hips ? worst * 100f : -1f;
                    float d = hips && pivot ? Vector3.Distance(Flat(h1), Flat(pivot.position)) : -1f;
                    float up = hips && v ? h1.y - v.transform.position.y : -1f;
                    float yaw = pivot ? pivot.eulerAngles.y : 0f;
                    if (!crew || drift > 3f) bad++;
                    string extra = "";
                    if (ct1)
                    {
                        var keys = new SortedDictionary<string, int>();
                        foreach (var en in OzSoundLog.Since(s0)) if (en.outcome != null && en.outcome.StartsWith("PLAY")) { keys.TryGetValue(en.key, out int n); keys[en.key] = n + 1; }
                        var ks = new System.Text.StringBuilder(); foreach (var kv in keys) ks.Append(kv.Key).Append(" x").Append(kv.Value).Append(' ');
                        extra = $" {BoardingView.CommanderShots - c0} | {ks.ToString().Trim()} |";
                    }
                    sb.AppendLine($"| {(ct1 ? crewType + " / " : "")}{type} | {deg:F0} | {(crew ? crew.name : "NONE")} | {d:F2} | {up:F2} | {drift:F1} | {yaw:F0} |{extra}");
                    shotView = v; shotPath = System.IO.Path.Combine(dir, $"{crewType}_{type}_{deg:000}.png"); shotPending = true; shotYaw = 35f;
                    yield return null; yield return null;
                    gun.AttackObject = null;
                    if (NoWreck) Remove(dummy); else { dummy.SetHealth(0); W.UpdateObjectHealth(dummy); }
                }
                if (NoWreck) Remove(gun); else { gun.SetHealth(0); W.UpdateObjectHealth(gun); }
                yield return new WaitForSeconds(NoWreck ? 0.3f : ct1 ? 0.8f : 1.5f);
            }
            Report = (bad == 0 ? "PASS" : $"FAIL ({bad} frames with no crew or drift > 3 cm)") + "\n" + sb;
            System.IO.File.WriteAllText(System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "../../../../work/records/" + ReportName)), "# Crew seat check\n\n" + Report);
            Debug.Log("[CrewSeatCheck] " + Report);
            if (rt) rt.Release();
            Destroy(gameObject);
        }

        static Vector3 Flat(Vector3 p) { p.y = 0; return p; }

        /// <summary>Gone without a wreck (DoKillMe(0) like LegGateTypes.ClearStage): the sim deletes it next tick, the view hides now.</summary>
        void Remove(ZObject o)
        {
            if (o == null || o.Destroyed || W.Get(o.Id) == null) return;
            o.AttackObject = null; o.KillAt = W.Time;
            foreach (var m in o.Minions) if (m != null && !m.Destroyed) { m.KillAt = W.Time; if (g.Views.TryGetValue(m.Id, out var mv) && mv) mv.Hide(); }
            if (g.Views.TryGetValue(o.Id, out var v) && v) v.Hide();
        }

        /// <summary>The seated crew object BoardingView keeps for this gun (a child under the gun's yaw node, or near it).</summary>
        static GameObject FindCrew(UnitView v)
        {
            if (!v) return null;
            foreach (var an in v.GetComponentsInChildren<Animator>(true)) if (an.isHuman && an.gameObject.activeInHierarchy) return an.gameObject;
            var root = GameObject.Find("OZ_BoardingCrew");
            if (root) { GameObject best = null; float bd = 4f; foreach (Transform c in root.transform) { if (!c.gameObject.activeInHierarchy) continue; float d = Vector3.Distance(c.position, v.transform.position); if (d < bd) { bd = d; best = c.gameObject; } } return best; }
            return null;
        }

        void LateUpdate()
        {
            if (!shotPending || !shotView) return;
            shotPending = false;
            var t = shotView.transform; var focus = t.position + Vector3.up * 1.6f;
            var dirv = Quaternion.Euler(0, shotYaw, 0) * new Vector3(0, 0.55f, -1f).normalized;
            cam.transform.position = focus + dirv * 13f; cam.transform.LookAt(focus);
            cam.targetTexture = rt; cam.Render(); cam.targetTexture = null;
            var prev = RenderTexture.active; RenderTexture.active = rt; tex.ReadPixels(new Rect(0, 0, 480, 360), 0, 0); tex.Apply(); RenderTexture.active = prev;
            System.IO.File.WriteAllBytes(shotPath, tex.EncodeToPNG());
        }
    }
}
