using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using OpenZ.Sim;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace OpenZ
{
    /// <summary>
    /// the open level it reports the offset from the intended socket, sinking / floating against the surface under it (ray
    /// (edit mode: the scene as built) or socket_audit_&lt;level&gt;_play.md (Play: the live views, after UnitView / TowerMounts).
    ///   fort tower pads   4 per fort (Z_fort pad data in TowerMounts): measured pad top + flatness, occupant
    ///   tower cannons     cannons on Zod's fort turret spots (bfort.cpp:534-548): pad centre + mount height, exact (5 cm),
    ///                     rest heading = the tower's outward diagonal (1 deg) (howitzer_towers_v1)
    ///   ground cannons    Zod tile centre (2x2 tiles), base on the lowest ground under the footprint
    ///   flags             map flags on their tile centre; fort flags (Play: OZ_Flags/FortFlag_*) on the fort roof
    ///   lamps             Environment DEC_*lamp* props (+ the NightLights posts in Play): pole base on the ground
    ///   sandbags          Environment/Sandbags walls: base on the ground, no overlap with gameplay objects
    /// Read only: probe colliders live in a preview scene, nothing in the level is changed or dirtied.
    /// </summary>
    public static class OpenZSocketAudit
    {
        static readonly string[] CannonTypes = { "gatling", "gun", "howitzer", "missile_cannon" };
        static readonly string[] MovingTypes = { "grunt", "psycho", "sniper", "tough", "pyro", "laser", "jeep", "light", "medium", "heavy", "apc", "missile_launcher", "crane" };
        static readonly Regex Named = new Regex(@"^(?<t>[a-z_]+?)_(?<x>\d+)_(?<y>\d+)(?:_\d+)?$");

        class Obj { public string name, type, cat; public Transform t; public int x = -1, y = -1; public Bounds b; public bool hasB; }
        class Row { public string cat, name, socket, dxz = "-", dy = "-", surf = "-", status = "PASS", note = ""; public List<string> overlaps = new List<string>(); }

        [MenuItem("OpenZ/Audit/Sockets (open level)")]
        public static void AuditOpen()
        {
            Audit(SceneManager.GetActiveScene());
        }

        [MenuItem("OpenZ/Audit/Sockets (both levels)")]
        public static void AuditBoth()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogError("[OpenZ] Socket audit (both levels): stop Play first, or use 'Sockets (open level)' in Play"); return; }
            EditorSceneManager.SaveOpenScenes(); AssetDatabase.SaveAssets();                                    // the project rules: save first
            string back = SceneManager.GetActiveScene().path;
            foreach (var p in new[] { "Assets/OpenZ/Scenes/L02_Desert.unity", "Assets/OpenZ/Scenes/L17_CarPark.unity" })
            {
                if (!File.Exists(p)) { Debug.LogWarning("[OpenZ] Socket audit: missing " + p); continue; }
                var s = EditorSceneManager.OpenScene(p, OpenSceneMode.Single);
                Audit(s);
            }
            if (!string.IsNullOrEmpty(back) && File.Exists(back)) EditorSceneManager.OpenScene(back, OpenSceneMode.Single);
        }

        /// <summary>Audits one loaded level scene; returns the report path (null when the scene is not a level).</summary>
        public static string Audit(Scene scene)
        {
            string level = scene.name.Contains("L02") ? "L02" : scene.name.Contains("L17") ? "L17" : null;
            if (level == null) { Debug.LogError("[OpenZ] Socket audit: open L02_Desert or L17_CarPark"); return null; }
            bool play = Application.isPlaying && ZGame.I && ZGame.I.World != null;
            var game = play ? ZGame.I : null;
            var zroot = GameObject.Find("ZodObjects"); var env = GameObject.Find("Environment");
            if (!zroot) { Debug.LogError("[OpenZ] Socket audit: no ZodObjects in " + scene.name); return null; }

            // ---- probe physics (preview scene: fort meshes + terrain), never the level's own colliders
            var probe = EditorSceneManager.NewPreviewScene();
            var fortCols = new Dictionary<Transform, List<Collider>>();
            Collider terrainCol = null;
            try
            {
                // ---- gather objects
                var objs = new List<Obj>();
                foreach (Transform t in zroot.transform)
                {
                    if (!t.gameObject.activeInHierarchy) continue;
                    if (play && t.GetComponent<UnitView>()) continue;                                        // Play: units come from the live views
                    var m = Named.Match(t.name);
                    var o = new Obj { name = t.name, t = t };
                    if (m.Success) { o.type = m.Groups["t"].Value; o.x = int.Parse(m.Groups["x"].Value); o.y = int.Parse(m.Groups["y"].Value); }
                    else o.type = t.name;
                    o.cat = Cat(o.type);
                    if (play && (o.cat == "cannon" || o.cat == "moving")) continue;                          // Play: the live views below
                    objs.Add(o);
                }
                if (play)
                    foreach (var kv in game.Views)
                    {
                        var v = kv.Value; if (!v || v.Obj == null || !v.gameObject.activeInHierarchy || v.Dying) continue;
                        if (v.Obj.Kind != ZKind.Cannon) continue;
                        objs.Add(new Obj { name = v.name, t = v.transform, type = v.Obj.Type, cat = "cannon", x = v.Obj.LocX / 16, y = v.Obj.LocY / 16 });
                    }
                var lamps = new List<Obj>(); var bags = new List<Obj>(); var mounts = new List<Obj>(); var fortFlags = new List<Obj>(); var flagViews = new List<Obj>();
                if (env)
                    foreach (var t in env.GetComponentsInChildren<Transform>(false))
                    {
                        string n = t.name, nl = n.ToLowerInvariant();
                        if (nl.StartsWith("dec_") && nl.Contains("lamp") && !HasLampParent(t, env.transform)) lamps.Add(new Obj { name = n, t = t, cat = "lamp", type = "lamp" });
                        else if (n.StartsWith("DEC_Sandbag") && t.parent && t.parent.name == "Sandbags") bags.Add(new Obj { name = n, t = t, cat = "sandbag", type = "sandbag" });
                    }
                if (play)
                {
                    foreach (var go in scene.GetRootGameObjects())
                    {
                        if (go.name == "OZ_TowerMounts") foreach (Transform t in go.transform) mounts.Add(new Obj { name = t.name, t = t, cat = "mount", type = "mount" });
                        if (go.name == "OZ_Flags")
                            foreach (Transform t in go.transform)
                            {
                                if (t.name.StartsWith("FortFlag_")) fortFlags.Add(new Obj { name = t.name, t = t, cat = "fortflag", type = "fortflag" });
                                else if (t.name.StartsWith("Flag_") && int.TryParse(t.name.Substring(5), out int id))
                                {
                                    var fo = game.World.Get(id);
                                    if (fo != null) flagViews.Add(new Obj { name = t.name, t = t, cat = "flag", type = "flag", x = fo.LocX / 16, y = fo.LocY / 16 });
                                }
                            }
                        foreach (var t in go.GetComponentsInChildren<Transform>(false))
                            if (t.name.StartsWith("DEC_night_lamp_proc_")) lamps.Add(new Obj { name = t.name, t = t, cat = "lamp", type = "lamp" });
                    }
                }
                foreach (var o in objs.Concat(lamps).Concat(bags).Concat(mounts).Concat(fortFlags).Concat(flagViews)) o.hasB = WorldBounds(o.t, out o.b);

                // ---- probe colliders: every fort's meshes, the terrain
                var forts = objs.Where(o => o.type == "fort_front" || o.type == "fort_back").ToList();
                foreach (var f in forts) fortCols[f.t] = CloneColliders(f.t, probe);
                var col = GameObject.Find("COL_Terrain");
                terrainCol = col ? col.GetComponent<Collider>() : null;                                        // read only: Collider.Raycast
                Physics.SyncTransforms();

                float Ground(float x, float z)
                {
                    if (play) return game.GroundY(x, z);
                    return terrainCol && terrainCol.Raycast(new Ray(new Vector3(x, 300f, z), Vector3.down), out var h, 600f) ? h.point.y : 0f;
                }
                float? FortTop(Transform fort, Vector3 at, float from)
                {
                    float best = float.NegativeInfinity;
                    var ray = new Ray(new Vector3(at.x, from, at.z), Vector3.down);
                    foreach (var c in fortCols[fort]) if (c && c.Raycast(ray, out var h, 80f)) best = Mathf.Max(best, h.point.y);   // per collider: the first hit from above = its top
                    return float.IsNegativeInfinity(best) ? (float?)null : best;
                }

                var rows = new List<Row>();
                // ---- fort tower pads + tower cannons
                var cannons = objs.Where(o => o.cat == "cannon").ToList();
                var onTower = new HashSet<Obj>();
                foreach (var f in forts)
                {
                    for (int i = 0; i < 4; i++)
                    {
                        var pad = TowerMounts.PadWorld(f.t, i);
                        var row = new Row { cat = "tower pad", name = $"{f.name} pad {i}", socket = V(pad) };
                        float? top = FortTop(f.t, pad, pad.y + 30f);
                        if (top.HasValue)
                        {
                            float lo = top.Value, hi = top.Value; int miss = 0;
                            for (int k = 0; k < 8; k++)
                            {
                                float a = k * Mathf.PI / 4f; var q = pad + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * (TowerMounts.PadFlatRadius * 0.8f);   // crews_v2: the flat top is 1.06 m in radius
                                var h = FortTop(f.t, q, pad.y + 30f); if (h.HasValue) { lo = Mathf.Min(lo, h.Value); hi = Mathf.Max(hi, h.Value); } else miss++;
                            }
                            row.dy = F(top.Value - pad.y); row.surf = $"top {top.Value:F2}, ring r{TowerMounts.PadFlatRadius * 0.8f:F2} {lo:F2}..{hi:F2}" + (miss > 0 ? $", {miss}/8 off the mesh" : "");
                            if (Mathf.Abs(top.Value - pad.y) > 0.05f) Flag(row, "WARN", "measured pad top differs from TowerMounts.PadTopY");
                            if (hi - lo > 0.08f || miss > 0) Flag(row, "WARN", "pad not flat within 0.8 x its radius");
                        }
                        else Flag(row, "FAIL", "no fort mesh under the pad centre");
                        var occ = cannons.FirstOrDefault(c => c.x >= 0 && TowerMounts.IsTurretTile(Tile(f).x, Tile(f).y, c.x, c.y) && TowerMounts.PadForSpot(f.t, (c.x - Tile(f).x) * 16, (c.y - Tile(f).y) * 16) == i);   // crews_v2: the Zod spot decides (as TowerMounts.Resolve)
                        row.note = (occ != null ? "occupied by " + occ.name : "free") + (row.note.Length > 0 ? "; " + row.note : "");
                        rows.Add(row);
                        if (occ == null) continue;
                        onTower.Add(occ);
                        var seat = TowerMounts.SeatWorld(f.t, i);
                        var cr = new Row { cat = "tower cannon", name = occ.name, socket = V(seat) + $" ({f.name} pad {i})" };
                        var p = occ.t.position; var ax = occ.t.TransformPoint(TowerMounts.FootprintCentreLocal(occ.t)); var d = new Vector3(ax.x - seat.x, p.y - seat.y, ax.z - seat.z);   // crews_v2: base plate centre on the pad centre, base on the mount
                        float dxz = new Vector2(d.x, d.z).magnitude;
                        cr.dxz = F(dxz); cr.dy = F(d.y);
                        float padTop = top ?? pad.y;
                        cr.surf = $"pivot {p.y:F2}, pad top {padTop:F2}, mount top {padTop + TowerMounts.MountHeight:F2}";
                        if (play)
                        {
                            if (dxz > 0.05f) Flag(cr, "FAIL", "off the pad centre (> 5 cm)");
                            float hdgErr = Mathf.Abs(Mathf.DeltaAngle(occ.t.eulerAngles.y, TowerMounts.OutwardHeading(f.t, i) + TowerMounts.RestYawOffset(occ.type)));   // howitzer_towers_v1
                            if (hdgErr > 1f) Flag(cr, "FAIL", $"rest heading {hdgErr:F1} deg off the tower diagonal");
                            if (Mathf.Abs(d.y) > 0.05f) Flag(cr, "FAIL", d.y < 0 ? "sunk into the mount" : "floating over the mount");
                            if (!mounts.Any(m => (new Vector2(m.t.position.x - pad.x, m.t.position.z - pad.z)).magnitude < 0.05f && m.t.gameObject.activeInHierarchy)) Flag(cr, "WARN", "no ring mount on this pad");
                        }
                        else
                        {
                            // tower_gun_fix: the saved scene is what edit mode, edit-mode captures and the first frames show: same rule as Play
                            // (was INFO "UnitView reseats it at run time": 23-86 cm off, 35 cm sunk, facing the fort, and nobody saw a FAIL)
                            cr.note = "edit-time placement (TowerGunSeat: OpenZ > Tower Guns > Seat Scene Tower Guns)";
                            if (dxz > 0.05f) Flag(cr, "FAIL", $"scene placement {dxz:F2} m off the pad centre (> 5 cm)");
                            if (Mathf.Abs(d.y) > 0.05f) Flag(cr, "FAIL", $"scene placement {d.y:+0.00;-0.00} m off the plinth top");
                            float hdgE = Mathf.Abs(Mathf.DeltaAngle(occ.t.eulerAngles.y, TowerMounts.SeatYaw(f.t, i, occ.type)));
                            if (hdgE > 1f) Flag(cr, "FAIL", $"scene rest heading {hdgE:F1} deg off the tower diagonal");
                        }
                        rows.Add(cr);
                    }
                }
                // ---- ground cannons
                foreach (var c in cannons.Where(c => !onTower.Contains(c)))
                {
                    var r = new Row { cat = "ground cannon", name = c.name };
                    var want = SimCentre(c); r.socket = V(want);
                    var p = c.t.position; float dxz = new Vector2(p.x - want.x, p.z - want.z).magnitude;
                    var (lo, hi) = Footprint(c, Ground);
                    r.dxz = F(dxz); r.dy = F(p.y - lo); r.surf = $"ground {lo:F2}..{hi:F2}";
                    if (dxz > (play ? 0.35f : 0.1f)) Flag(r, "WARN", play ? "view offset from the sim centre (spacing push?)" : "off the tile centre");
                    if (p.y - lo > 0.05f) Flag(r, "FAIL", "floating over the lowest ground under its footprint");
                    if (p.y - lo < -0.15f) Flag(r, "WARN", "sunk below the lowest ground");
                    if (hi - lo > 0.6f) Flag(r, "WARN", "footprint on a slope > 0.6 m");
                    rows.Add(r);
                }
                // ---- flags
                foreach (var fl in (play ? flagViews : objs.Where(o => o.type == "flag").ToList()))
                {
                    var r = new Row { cat = "flag", name = fl.name };
                    var want = new Vector3(fl.x * 2 + 1, 0, -(fl.y * 2 + 1)); r.socket = V(want);
                    var p = fl.t.position; float g = Ground(p.x, p.z); float dxz = new Vector2(p.x - want.x, p.z - want.z).magnitude;
                    float baseY = fl.hasB ? fl.b.min.y : p.y;
                    r.dxz = F(dxz); r.dy = F(baseY - g); r.surf = $"ground {g:F2}, pole base {baseY:F2}";
                    if (dxz > 1.0f) Flag(r, "WARN", "outside its Zod tile");
                    if (baseY - g > 0.05f) Flag(r, "FAIL", "pole floating");
                    if (baseY - g < -0.4f) Flag(r, "WARN", "pole sunk > 0.4 m");
                    rows.Add(r);
                }
                foreach (var ff in fortFlags)
                {
                    var r = new Row { cat = "fort flag", name = ff.name, socket = "fort roof socket" };
                    var fort = forts.OrderBy(f => (f.t.position - ff.t.position).sqrMagnitude).FirstOrDefault();
                    var p = ff.t.position; float baseY = ff.hasB ? ff.b.min.y : p.y;
                    float? top = fort != null ? FortTop(fort.t, p, p.y + 30f) : null;
                    if (top.HasValue) { r.dy = F(baseY - top.Value); r.surf = $"roof {top.Value:F2}, pole base {baseY:F2}"; if (baseY - top.Value > 0.05f) Flag(r, "FAIL", "pole floating over the roof"); if (baseY - top.Value < -0.5f) Flag(r, "WARN", "pole sunk > 0.5 m"); }
                    else Flag(r, "WARN", "no fort mesh under the pole");
                    rows.Add(r);
                }
                // ---- lamps and sandbags
                foreach (var l in lamps)
                {
                    var r = new Row { cat = "lamp", name = l.name, socket = "ground under the pole" };
                    var p = l.t.position; float g = Ground(p.x, p.z); float baseY = l.hasB ? l.b.min.y : p.y;
                    r.dy = F(baseY - g); r.surf = $"ground {g:F2}, base {baseY:F2}";
                    if (baseY - g > 0.05f) Flag(r, "FAIL", "floating");
                    if (baseY - g < -0.35f) Flag(r, "WARN", "sunk > 0.35 m");
                    rows.Add(r);
                }
                foreach (var s in bags)
                {
                    var r = new Row { cat = "sandbags", name = s.name, socket = "ground under the wall" };
                    if (!s.hasB) { Flag(r, "WARN", "no renderer"); rows.Add(r); continue; }
                    var (lo, hi) = FootprintB(s.b, Ground);
                    r.dy = F(s.b.min.y - lo); r.surf = $"ground {lo:F2}..{hi:F2}, base {s.b.min.y:F2}";
                    if (s.b.min.y - lo > 0.05f) Flag(r, "FAIL", "floating over the lowest ground");
                    if (s.b.min.y - hi < -0.45f) Flag(r, "WARN", "sunk > 0.45 m under the highest ground");
                    rows.Add(r);
                }

                // ---- overlaps (world AABBs shrunk 15 %, placed objects only: robots and vehicles move)
                var solid = objs.Where(o => o.cat != "moving" && o.hasB).Concat(lamps.Where(o => o.hasB)).Concat(bags.Where(o => o.hasB)).ToList();
                var byName = new Dictionary<string, Obj>();
                foreach (var o in objs.Concat(lamps).Concat(bags).Concat(fortFlags).Concat(flagViews)) byName[o.name] = o;
                foreach (var r in rows)
                {
                    if (!byName.TryGetValue(r.name, out var me) || !me.hasB) continue;
                    var mb = Shrink(me.b, 0.85f);
                    foreach (var o in solid)
                    {
                        if (o == me || !mb.Intersects(Shrink(o.b, 0.85f))) continue;
                        if ((r.cat == "tower cannon" || r.cat == "fort flag") && (o.type == "fort_front" || o.type == "fort_back")) continue;   // they stand on it
                        if (r.cat == "sandbags" && o.cat == "sandbag") continue;                                                           // walls join
                        if (o.t.IsChildOf(me.t) || me.t.IsChildOf(o.t)) continue;
                        r.overlaps.Add(o.name);
                    }
                    if (r.overlaps.Count > 0) Flag(r, "WARN", "AABB overlap");
                }

                // ---- report
                var sb = new StringBuilder();
                sb.AppendLine($"# Socket audit {level} ({(play ? "Play: live views" : "edit mode: scene as built")})");
                sb.AppendLine();
                sb.AppendLine($"Written {System.DateTime.Now:yyyy-MM-dd HH:mm} by OpenZ/Audit/Sockets (Editor/OpenZSocketAudit.cs, crews_v1). Scene `{scene.name}`.");
                sb.AppendLine("Pads (howitzer_towers_v1): TowerMounts.PadLocal (fort-local, plate top y " + TowerMounts.PadTopY + ", measured centres (+-6.985, -6.563) / (+-6.985, +5.32)); plinth drum r " + TowerMounts.MountRadius + " m on the ledge " + TowerMounts.LedgeDrop + " m below; tower cannon seat = pad + plinth " + TowerMounts.MountHeight + " m - " + TowerMounts.SeatSink + " m, base plate centre on the pad centre, rest heading = the tower diagonal.");
                sb.AppendLine("dxz = horizontal offset from the socket (m; tower cannons: the base plate centre (TowerMounts.FootprintCentreLocal)); dy = vertical (+ floating / - sunk, m); overlaps = world AABBs shrunk 15 % (coarse: a WARN to look at, not proof).");
                sb.AppendLine();
                var cats = rows.GroupBy(r => r.cat).ToList();
                sb.AppendLine("| category | total | PASS | INFO | WARN | FAIL |"); sb.AppendLine("|---|---|---|---|---|---|");
                foreach (var g in cats) sb.AppendLine($"| {g.Key} | {g.Count()} | {g.Count(r => r.status == "PASS")} | {g.Count(r => r.status == "INFO")} | {g.Count(r => r.status == "WARN")} | {g.Count(r => r.status == "FAIL")} |");
                foreach (var g in cats)
                {
                    sb.AppendLine(); sb.AppendLine($"## {g.Key}"); sb.AppendLine();
                    sb.AppendLine("| status | object | socket | dxz | dy | surface | overlaps | note |"); sb.AppendLine("|---|---|---|---|---|---|---|---|");
                    foreach (var r in g.OrderByDescending(r => Rank(r.status)).ThenBy(r => r.name))
                        sb.AppendLine($"| {r.status} | {r.name} | {r.socket} | {r.dxz} | {r.dy} | {r.surf} | {string.Join(", ", r.overlaps.Take(4))}{(r.overlaps.Count > 4 ? $" +{r.overlaps.Count - 4}" : "")} | {r.note} |");
                }
                string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "..", "..", "work", "records", "qc"));
                Directory.CreateDirectory(dir);
                string path = Path.Combine(dir, $"socket_audit_{level}{(play ? "_play" : "")}.md");
                File.WriteAllText(path, sb.ToString());
                int fail = rows.Count(r => r.status == "FAIL"), warn = rows.Count(r => r.status == "WARN");
                Debug.Log($"[OpenZ] Socket audit {level} ({(play ? "play" : "edit")}): {rows.Count} sockets, {fail} FAIL, {warn} WARN -> work/records/qc/{Path.GetFileName(path)}");
                return path;
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(probe);
            }
        }

        // ------------------------------------------------------------------ helpers
        static string Cat(string type)
        {
            if (CannonTypes.Contains(type)) return "cannon";
            if (MovingTypes.Contains(type)) return "moving";
            if (type == "fort_front" || type == "fort_back") return "fort";
            if (type == "flag") return "flag";
            return "item";
        }

        static bool HasLampParent(Transform t, Transform stop)
        {
            for (var p = t.parent; p && p != stop; p = p.parent) if (p.name.ToLowerInvariant().Contains("lamp") && p.name.StartsWith("DEC_", System.StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        static Vector2Int Tile(Obj o) => new Vector2Int(o.x, o.y);
        /// <summary>The gun's turn axis (TurretYaw / CannonTurretYaw), else its pivot.</summary>
        static Vector3 Axis(Transform t)
        {
            foreach (var c in t.GetComponentsInChildren<Transform>(true)) if (c.name.EndsWith("TurretYaw")) return c.position;
            return t.position;
        }
        /// <summary>Zod sim centre of a 2x2 cannon (world).</summary>
        static Vector3 SimCentre(Obj c) => new Vector3(c.x * 2 + 2, 0, -(c.y * 2 + 2));

        static (float lo, float hi) Footprint(Obj o, System.Func<float, float, float> ground)
        {
            if (!o.hasB) { float g = ground(o.t.position.x, o.t.position.z); return (g, g); }
            return FootprintB(o.b, ground);
        }
        static (float lo, float hi) FootprintB(Bounds b, System.Func<float, float, float> ground)
        {
            float lo = float.MaxValue, hi = float.MinValue;
            for (int ix = -1; ix <= 1; ix++)
                for (int iz = -1; iz <= 1; iz++)
                {
                    float g = ground(b.center.x + ix * b.extents.x * 0.85f, b.center.z + iz * b.extents.z * 0.85f);
                    lo = Mathf.Min(lo, g); hi = Mathf.Max(hi, g);
                }
            return (lo, hi);
        }

        static bool WorldBounds(Transform t, out Bounds b)
        {
            b = default; bool any = false;
            foreach (var r in t.GetComponentsInChildren<Renderer>(false))
            {
                if (!r.enabled || r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer) continue;
                if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
            }
            return any;
        }
        static Bounds Shrink(Bounds b, float k) => new Bounds(b.center, b.size * k);

        /// <summary>Copies every mesh under `t` as a MeshCollider (same world pose) into the probe scene.</summary>
        static List<Collider> CloneColliders(Transform t, Scene probe)
        {
            var list = new List<Collider>();
            foreach (var mf in t.GetComponentsInChildren<MeshFilter>(false))
            {
                if (!mf.sharedMesh) continue;
                var go = new GameObject("probe_" + mf.name) { hideFlags = HideFlags.HideAndDontSave };
                SceneManager.MoveGameObjectToScene(go, probe);
                go.transform.SetPositionAndRotation(mf.transform.position, mf.transform.rotation);
                go.transform.localScale = mf.transform.lossyScale;
                var mc = go.AddComponent<MeshCollider>(); mc.sharedMesh = mf.sharedMesh;
                list.Add(mc);
            }
            foreach (var src in t.GetComponentsInChildren<MeshCollider>(false))
            {
                if (!src.sharedMesh || src.GetComponent<MeshFilter>()) continue;
                var go = new GameObject("probe_col_" + src.name) { hideFlags = HideFlags.HideAndDontSave };
                SceneManager.MoveGameObjectToScene(go, probe);
                go.transform.SetPositionAndRotation(src.transform.position, src.transform.rotation);
                go.transform.localScale = src.transform.lossyScale;
                var mc = go.AddComponent<MeshCollider>(); mc.sharedMesh = src.sharedMesh;
                list.Insert(0, mc);
            }
            return list;
        }

        static void Flag(Row r, string status, string note)
        {
            if (Rank(status) > Rank(r.status)) r.status = status;
            r.note = r.note.Length > 0 ? r.note + "; " + note : note;
        }
        static int Rank(string s) => s == "FAIL" ? 3 : s == "WARN" ? 2 : s == "INFO" ? 1 : 0;
        static string F(float v) => v.ToString("+0.00;-0.00;0.00");
        static string V(Vector3 v) => $"({v.x:F2}, {v.y:F2}, {v.z:F2})";
    }
}
