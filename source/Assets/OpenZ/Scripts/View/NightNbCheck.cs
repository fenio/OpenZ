using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// Play proof for the night-play items (CPU bots removed for the run; player-style commands only).
    ///  L02: NB50 a blue searchlight catches a red jeep in range, keeps it lit while it stays, returns to its sweep when it is
    ///       gone; NB41 minimap enemy dots with / without a radar (frames); NB32 the blue fort gate at night (frame).
    ///  L17: NB25 a selected crane hovers a broken bridge (PlayerInput.HoverAt), gets the CraneRepair order and rebuilds it;
    ///       NB49 a jeep driven into a lamp: the knock distance from the hull and the fall direction against the motion.
    /// </summary>
    [DefaultExecutionOrder(40)]
    public class NightNbCheck : MonoBehaviour
    {
        public static string Report = "idle";
        ZGame g; ZWorld W; readonly StringBuilder sb = new StringBuilder(); int pass, total;
        readonly List<ZObject> keep = new List<ZObject>();
        static string Records => System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "../../../../work/records"));
        static string Frames => Records + "/milestones/night_nb";

        public static void Run() { Report = "running"; new GameObject("NightNbCheck").AddComponent<NightNbCheck>(); }
        void Start() { g = ZGame.I; W = g.World; System.IO.Directory.CreateDirectory(Frames); StartCoroutine(All()); }

        void Update()
        {
            foreach (var o in keep) if (o != null && !o.Destroyed) { o.SetHealth(o.MaxHealth); if (o.Drivers.Count > 0) o.Drivers[0] = 100000; }   // no snipes during a probe
        }

        ZObject Thing(string cat, string type, Team t, int tx, int ty)
        {
            OzCheatSpawner.FreeTile(W, tx, ty, 2, false, out tx, out ty);
            var o = W.CreateObject(cat, type, tx * 16, ty * 16, t); g.SpawnUnitView(o); return o;
        }
        void Kill(ZObject o) { if (o == null || o.Destroyed) return; keep.Remove(o); o.SetHealth(0); W.UpdateObjectHealth(o); }
        void LookAt(Vector3 p, float h) { var cam = FindFirstObjectByType<RtsCamera>(); if (cam) { cam.focus = new Vector3(p.x, 0, p.z); cam.SetHeightImmediate(h); } }
        void Shot(string name) => ScreenCapture.CaptureScreenshot(Frames + "/" + name + ".png");
        void Row(string test, bool ok, string measured) { total++; if (ok) pass++; sb.AppendLine($"| {test} | {(ok ? "PASS" : "FAIL")} | {measured} |"); }
        void Night(float t) { var dn = FindFirstObjectByType<DayNight>(); if (dn) { dn.timeOfDay = t; dn.Apply(t); } }

        IEnumerator All()
        {
            yield return null;
            W.Bots.Clear();
            sb.AppendLine($"# NightNbCheck {g.level}\n");
            sb.AppendLine($"{System.DateTime.Now:yyyy-MM-dd HH:mm}; CPU bots removed for the run\n");
            sb.AppendLine("| test | result | measured |\n|---|---|---|");
            Night(0.62f);
            yield return new WaitForSeconds(2.5f);                                               // lights switch on
            var nl = NightLights.I;
            if (g.level == "L02") { yield return L02(nl); }
            else { yield return L17(nl); }
            sb.AppendLine($"\n**{pass}/{total} PASS**");
            System.IO.File.WriteAllText(Records + $"/night_nb_check_{g.level}.md", sb.ToString());
            Report = $"{pass}/{total}";
            Destroy(gameObject);
        }

        IEnumerator L02(NightLights nl)
        {
            // ---- NB50 searchlight tracking
            NightFixture f = null;
            if (nl) foreach (var x in nl.Fixtures) if (x.Kind == NightKind.Searchlight && x.Rig != null && x.Rig.B != null && x.Rig.B.Owner == Team.Blue && x.Level > 0.5f) { f = x; break; }
            if (f == null) Row("NB50 searchlight tracks an enemy in range", false, "no lit blue searchlight found");
            else
            {
                var ahead = f.Root.position + f.Root.forward * Mathf.Min(14f, f.Range * 0.55f);
                var px = ZGame.WorldToPx(ahead);
                var jeep = Thing("vehicle", "jeep", Team.Red, px.x >> 4, px.y >> 4); keep.Add(jeep); jeep.Damage = 0; jeep.MoveSpeed = 0; jeep.MaxHealth = 10000000; jeep.SetHealth(jeep.MaxHealth);   // parked + unkillable (the fort mortar one-shot it at 6 s)
                LookAt(ahead, 40f);
                float t0 = Time.time, caught = -1f, held = 0f, nextLog = 0f; var trace = new StringBuilder();
                var ins = typeof(NightLights).GetMethod("InSector", BindingFlags.Instance | BindingFlags.NonPublic);
                while (Time.time - t0 < 16f)
                {
                    if (f.Target == jeep) { if (caught < 0f) caught = Time.time - t0; held += Time.deltaTime; }
                    if (Time.time - t0 >= nextLog)
                    {
                        nextLog += 0.5f; var a = new object[] { f, jeep, 1.05f, 0f, 0f }; bool inS = (bool)ins.Invoke(nl, a);
                        trace.AppendLine($"{Time.time - t0:F1} target {(f.Target == null ? "-" : f.Target.Type + "#" + f.Target.Id)} inSector {inS} yaw {(float)a[3]:F0} pitch {(float)a[4]:F0} level {f.Level:F2} jeep {jeep.CenterX},{jeep.CenterY} {jeep.Owner} hp {jeep.Health} destroyed {jeep.Destroyed}");
                    }
                    yield return null;
                }
                System.IO.File.WriteAllText(Records + "/nb50_harness_trace.txt", trace.ToString());
                Shot("NB50_searchlight_on_jeep");
                bool still = f.Target == jeep;
                Kill(jeep);
                float t1 = Time.time, released = -1f, yaw0 = f.YawNow;
                while (Time.time - t1 < 6f) { if (released < 0f && f.Target == null) released = Time.time - t1; yield return null; }
                float swing = Mathf.Abs(Mathf.DeltaAngle(yaw0, f.YawNow));
                Row("NB50 searchlight catches an enemy in range, keeps it lit, returns to the sweep", caught >= 0f && caught < 3f && still && released >= 0f && released < 1.5f,
                    $"{f.Id}: caught after {caught:F1} s, lit {held:F1} of {16f - Mathf.Max(0f, caught):F1} s, still on it at 16 s {still}; released {released:F1} s after the jeep died, turned {swing:F0} deg back into its sweep in 6 s");
            }
            yield return new WaitForSeconds(0.5f);

            // ---- NB41 minimap dots without / with a radar
            var enemy = Thing("vehicle", "light", Team.Blue, 40, 60); keep.Add(enemy); enemy.Damage = 0;
            LookAt(ZGame.PxToWorld(enemy.CenterX, enemy.CenterY), 60f);
            ZObject radar = W.Objects.FirstOrDefault(o => o.Kind == ZKind.Building && o.Type == "radar" && !o.Destroyed);
            var own0 = radar != null ? radar.Owner : Team.Neutral;
            var setOwner = typeof(ZWorld).GetMethod("SetOwnerZod", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (radar != null && radar.Owner == g.player) setOwner?.Invoke(W, new object[] { radar, Team.Neutral, false });
            yield return new WaitForSeconds(0.6f);
            bool noRadar = !OpenZMinimap.HasRadar; Shot("NB41_minimap_no_radar");
            if (radar != null) setOwner?.Invoke(W, new object[] { radar, g.player, false });
            yield return new WaitForSeconds(0.6f);
            bool withRadar = OpenZMinimap.HasRadar; Shot("NB41_minimap_with_radar");
            if (radar != null) setOwner?.Invoke(W, new object[] { radar, own0, false });
            Row("NB41 enemy minimap dots only with a radar of ours", radar != null && noRadar && withRadar,
                $"radar {(radar != null ? radar.Id + " (owner " + own0 + ")" : "none")}: without -> HasRadar {!noRadar}, with -> HasRadar {withRadar}; frames NB41_minimap_*.png (a blue light tank at tile 40,60)");
            Kill(enemy);

            // ---- NB32 blue fort gate at night
            var bf = W.Objects.FirstOrDefault(o => o.IsFort && o.Owner == Team.Blue && o.IsProducer);
            if (bf != null)
            {
                var gate = ZGame.PxToWorld(bf.CenterX, bf.LocY + bf.HeightPix) + new Vector3(0f, 0f, 4f);   // the camera pitch: focus 4 m inside frames the gate
                LookAt(gate, 22f); yield return new WaitForSeconds(1f);
                Shot("NB32_fort_gate_night");
                Row("NB32 fort gate at night (frame for the dots check)", true, "frame NB32_fort_gate_night.png");
            }
        }

        IEnumerator L17(NightLights nl)
        {
            // ---- NB25 crane repairs a broken bridge
            var input = FindFirstObjectByType<PlayerInput>();
            var bridge = W.Objects.FirstOrDefault(o => o.IsBridge && o.Destroyed);
            if (bridge == null)
            {   // the auto-repair may have rebuilt them: break one
                bridge = W.Objects.FirstOrDefault(o => o.IsBridge);
                if (bridge != null) { bridge.SetHealth(0); W.UpdateObjectHealth(bridge); }
                yield return new WaitForSeconds(1f);
            }
            if (bridge == null || input == null) Row("NB25 crane repairs a broken bridge", false, "no bridge / no PlayerInput");
            else
            {
                bridge.GetCraneCenter(out int kx, out int ky);
                var crane = Thing("vehicle", "crane", g.player, (kx >> 4) + 3, (ky >> 4) + 3); keep.Add(crane);
                input.Selection.Clear(); input.Selection.Add(crane);
                var cam = Camera.main; LookAt(ZGame.PxToWorld(bridge.CenterX, bridge.CenterY), 40f);
                yield return null; yield return null;
                var mp = (Vector2)cam.WorldToScreenPoint(g.CenterWorld(bridge));
                var hoverAt = typeof(PlayerInput).GetMethod("HoverAt", BindingFlags.Instance | BindingFlags.NonPublic);
                var h = hoverAt?.Invoke(input, new object[] { mp }) as ZObject;
                var wp = input.OrderFor(crane, h, bridge.CenterX, bridge.CenterY);
                W.SendWaypoints(g.player, crane, new List<ZWaypoint> { wp });
                float t0 = Time.time, rebuilt = -1f;
                while (Time.time - t0 < 60f && rebuilt < 0f) { if (!bridge.Destroyed) rebuilt = Time.time - t0; yield return null; }
                Shot("NB25_crane_bridge");
                Row("NB25 a selected crane targets a broken bridge (repair order) and rebuilds it", h == bridge && wp.mode == WpMode.CraneRepair && rebuilt > 0f,
                    $"hover under the pointer: {(h == null ? "none" : h.Type + "#" + h.Id)} (bridge #{bridge.Id}); order {wp.mode}; rebuilt {(rebuilt > 0f ? $"after {rebuilt:F1} s" : "no (60 s)")}");
                input.Selection.Clear();
            }

            // ---- NB49 a jeep driven into a lamp
            NightFixture lamp = null; float best = float.MaxValue;
            if (nl) foreach (var x in nl.Fixtures)
                {
                    if (!x.IsLamp || x.Damage != 0 || !x.LampGO) continue;
                    var p = ZGame.WorldToPx(x.Foot);
                    var c = W.Map.ClassAt(p.x >> 4, p.y >> 4);
                    if (c != ZClass.Road && c != ZClass.Ground) continue;
                    // open ground 5 tiles west of it for the run-up
                    bool clear = true; for (int k = -6; k <= 6 && clear; k++) if (k != 0 && !W.Path.TilePassable((p.x >> 4) + k, p.y >> 4, false)) clear = false;
                    float d = Mathf.Abs(p.x - W.Map.width * 8) + Mathf.Abs(p.y - W.Map.height * 8);
                    if (clear && d < best) { best = d; lamp = x; }
                }
            if (lamp == null) Row("NB49 a lamp knocked by a jeep falls away from it", false, "no standing lamp with a clear run-up");
            else
            {
                var fp = ZGame.WorldToPx(lamp.Foot);
                var jeep = W.CreateObject("vehicle", "jeep", ((fp.x >> 4) - 6) * 16, fp.y - 16, g.player); g.SpawnUnitView(jeep); keep.Add(jeep);   // centre on the pole's row
                LookAt(lamp.Foot, 26f);
                W.SendWaypoints(g.player, jeep, new List<ZWaypoint> { ZWaypoint.Move(fp.x + 6 * 16, fp.y) });
                // whichever standing lamp the jeep knocks first is measured (its path may clip a neighbour first)
                var standing = nl.Fixtures.Where(x => x.IsLamp && x.Damage < 2 && x.LampGO).ToList();
                float t0 = Time.time, knockDist = -1f; Vector3 vAt = Vector3.zero; NightFixture hitLamp = null;
                while (Time.time - t0 < 12f && hitLamp == null)
                {
                    foreach (var x in standing) if (x.Damage >= 2) { hitLamp = x; break; }
                    if (hitLamp != null)
                    {
                        var jw = ZGame.PxToWorld(jeep.CenterX, jeep.CenterY); knockDist = new Vector2(hitLamp.Foot.x - jw.x, hitLamp.Foot.z - jw.z).magnitude;
                        vAt = new Vector3(jeep.Dx, 0, -jeep.Dy).normalized;
                    }
                    yield return null;
                }
                if (hitLamp != null) lamp = hitLamp;
                yield return new WaitForSeconds(0.35f); Shot("NB49_lamp_falling");
                yield return new WaitForSeconds(1.2f); Shot("NB49_lamp_landed");
                var hd = lamp.Head.position - lamp.Foot; var fall = new Vector3(hd.x, 0f, hd.z).normalized;   // where the head lies = the fall direction
                float along = Vector3.Dot(fall, vAt);
                Row("NB49 a lamp knocked by a jeep: knocked at the hull edge, falls along the motion (away from the jeep)", lamp.Damage >= 2 && knockDist >= 1.2f && along > 0.5f,
                    $"knocked {(lamp.Damage >= 2 ? "yes" : "no")} with the jeep centre {knockDist:F2} m from the pole (hull half length ~1.5 m); fall direction . jeep motion = {along:F2}; frames NB49_lamp_*.png");
            }
        }
    }
}
