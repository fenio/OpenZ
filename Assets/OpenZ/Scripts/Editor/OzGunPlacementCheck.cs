using System.Collections.Generic;
using System.Text;
using OpenZ.Sim;
using UnityEditor;
using UnityEngine;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// Counted checks for the gun placement overlay. Pass = 0 violations.
    /// Counted test (menu, no scene, no Play mode; headless worlds of both maps):
    ///   Part 1, the marked area against the rule. For each map, with the ground rule on and off: one building per zone, the
    ///   picture's data (GunCover) against ZWorld.CannonPlacable on 300 random spots (the zones and two tiles round them) and on
    ///   every spot; with the rule on no marked tile may be water or impassable ground.
    ///   Part 2, the ring against the weapon. For each gun type on open ground of L02: the ring radius (the table value the
    ///   overlay reads) against the gun's own range and against the farthest distance at which the rule lets it engage a target;
    ///   then real shots in ticked worlds: it must fire at a target at that distance and must not fire at one half a tile further.
    /// Overlay audit (menu, Play mode on a map): OzGunZone.Audit and OzGunRange.Audit measure the built meshes against the ground.
    /// The ground rule switch is restored afterwards.
    /// </summary>
    public static class OzGunPlacementCheck
    {
        static readonly string[] Levels = { "L02", "L17" };
        const int RandomSpots = 300, HalfTilePx = 8;
        const double ShotSeconds = 30;

        [MenuItem("OpenZ/Checks/Gun Placement: Counted Test (no Play mode)")]
        static void Run() => Debug.Log(Report(out _));

        [MenuItem("OpenZ/Checks/Gun Placement: Overlay Audit (Play mode)")]
        static void RunAudit()
        {
            if (!Application.isPlaying) { Debug.LogWarning("Gun placement overlay audit: enter Play mode on a map first."); return; }
            string a = OzGunZone.Audit(out int v1), b = OzGunRange.Audit(out int v2);
            Debug.Log(a + b + (v1 + v2 == 0 ? "GUN PLACEMENT OVERLAY AUDIT: PASS (0 violations)" : "GUN PLACEMENT OVERLAY AUDIT: FAIL (" + (v1 + v2) + " violations)"));
        }

        static string SettingsText => Resources.Load<TextAsset>("OpenZ/default_settings").text;
        static string MapText(string lv) => Resources.Load<TextAsset>("OpenZ/Maps/" + lv + "_grid").text;

        static List<ZObject> Hosts(ZWorld w)
        {
            var hosts = new List<ZObject>(); var seen = new HashSet<int>();
            foreach (var o in w.Objects)
                if (o.Kind == ZKind.Building && w.ZoneOf(o) != null && seen.Add(o.ZoneIndex)) hosts.Add(o);
            return hosts;
        }

        /// <summary>Allowed 2x2 spots over all zones of both maps, with the ground rule and without (for the deviations list).</summary>
        public static (int l02On, int l02Off, int l17On, int l17Off) SpotCounts()
        {
            bool keep = ZWorld.GunGroundRule;
            var n = new int[4];
            try
            {
                for (int lv = 0; lv < 2; lv++)
                    for (int pass = 0; pass < 2; pass++)
                    {
                        ZWorld.GunGroundRule = pass == 0;
                        var w = ZWorld.Create(SettingsText, MapText(Levels[lv]), 1);
                        foreach (var h in Hosts(w)) n[lv * 2 + pass] += GunCover.Compute(w, h).validSpots;
                    }
            }
            finally { ZWorld.GunGroundRule = keep; }
            return (n[0], n[1], n[2], n[3]);
        }

        public static string Report(out int violations)
        {
            violations = 0;
            var sb = new StringBuilder("GUN PLACEMENT COUNTED TEST\n");
            bool keep = ZWorld.GunGroundRule;
            try
            {
                // ---------------------------------------------------------------- part 1: the marked area against the rule
                var valid = new int[2, 2]; var badGround = new int[2];
                for (int lv = 0; lv < Levels.Length; lv++)
                    for (int pass = 0; pass < 2; pass++)
                    {
                        ZWorld.GunGroundRule = pass == 0;
                        var w = ZWorld.Create(SettingsText, MapText(Levels[lv]), 1);
                        var hosts = Hosts(w); var covers = new List<GunCover>();
                        foreach (var h in hosts) covers.Add(GunCover.Compute(w, h));
                        int all = 0, allBad = 0, marked = 0, pads = 0, tilesBad = 0;
                        for (int k = 0; k < hosts.Count; k++)
                        {
                            var c = covers[k];
                            for (int ty = c.zy - 2; ty < c.zy + c.zh + 2; ty++)
                                for (int tx = c.zx - 2; tx < c.zx + c.zw + 2; tx++)
                                {
                                    bool m = c.Marked(tx, ty);
                                    all++; if (m) marked++;
                                    if (m != w.CannonPlacable(hosts[k], tx, ty)) allBad++;
                                }
                            for (int ty = c.zy; ty < c.zy + c.zh; ty++)
                                for (int tx = c.zx; tx < c.zx + c.zw; tx++)
                                {
                                    if (!c.Tile(tx, ty)) continue;
                                    var cls = w.Map.ClassAt(tx, ty);
                                    if (cls != ZClass.Ground && cls != ZClass.Road) tilesBad++;
                                }
                            pads += c.pads.Count; valid[lv, pass] += c.validSpots;
                        }
                        var rng = new System.Random(1000 + lv * 10 + pass);
                        int rndBad = 0, rndYes = 0;
                        for (int i = 0; i < RandomSpots && hosts.Count > 0; i++)
                        {
                            int k = rng.Next(hosts.Count); var c = covers[k];
                            int tx = c.zx - 2 + rng.Next(c.zw + 4), ty = c.zy - 2 + rng.Next(c.zh + 4);
                            bool rule = w.CannonPlacable(hosts[k], tx, ty);
                            if (rule) rndYes++;
                            if (rule != c.Marked(tx, ty)) rndBad++;
                        }
                        if (pass == 0) badGround[lv] = tilesBad;
                        int bad = allBad + rndBad + (hosts.Count == 0 ? 1 : 0);
                        violations += bad;
                        sb.Append(bad == 0 ? "OK   " : "FAIL ").Append(Levels[lv]).Append(pass == 0 ? ", ground rule on: " : ", ground rule off: ")
                          .Append(hosts.Count).Append(" zones with a building; random spots: marked equals the rule on ").Append(RandomSpots - rndBad).Append(" of ").Append(RandomSpots)
                          .Append(" (").Append(rndYes).Append(" of them allowed); every spot: ").Append(all - allBad).Append(" of ").Append(all)
                          .Append(" (").Append(marked).Append(" allowed, ").Append(pads).Append(" of them fort tower pads); marked tiles on water or impassable ground: ").Append(tilesBad).Append('\n');
                    }
                bool hooked = valid[0, 0] < valid[0, 1] || valid[1, 0] < valid[1, 1];
                if (hooked)
                {
                    int bad = badGround[0] + badGround[1];
                    violations += bad;
                    sb.Append(bad == 0 ? "OK   " : "FAIL ").Append("ground rule in the placement rule: allowed spots with it / without it: L02 ").Append(valid[0, 0]).Append(" / ").Append(valid[0, 1])
                      .Append(", L17 ").Append(valid[1, 0]).Append(" / ").Append(valid[1, 1]).Append("; marked tiles on water or impassable ground with it: ").Append(bad).Append('\n');
                }
                else sb.Append("NOTE the ground rule is not in ZWorld.CannonPlacable (the kit was applied without it): the area follows the Zod rule, water included\n");

                // ---------------------------------------------------------------- part 2: the ring against the weapon
                ZWorld.GunGroundRule = keep;
                foreach (var type in ZSettings.Cannons)
                {
                    var w = Bare(out int lx, out int ly, type);
                    if (w == null) { violations++; sb.Append("FAIL ").Append(type).Append(": no open lane found on L02\n"); continue; }
                    int ring = OzGunRange.RadiusPx(w, type);
                    var gun = w.CreateObject("cannon", type, lx * 16, ly * 16, Team.Red);
                    var probe = w.CreateObject("vehicle", "crane", gun.LocX + 64, gun.LocY, Team.Blue);
                    int far = -1;
                    for (int dpx = ring + 24; dpx >= 16; dpx--)
                    {
                        probe.SetCords(gun.CenterX + dpx - 16, gun.LocY);
                        if (gun.WithinAttackRadius(probe)) { far = dpx; break; }
                    }
                    string target = "crane";
                    bool at = far > 0 && Shot(type, target, far);
                    if (far > 0 && !at) { target = "light"; at = Shot(type, target, far); }
                    bool beyond = far > 0 && Shot(type, target, far + HalfTilePx);
                    bool ok = far > 0 && gun.AttackRadius == ring && System.Math.Abs(far - ring) <= HalfTilePx && at && !beyond;
                    if (!ok) violations++;
                    sb.Append(ok ? "OK   " : "FAIL ").Append(type).Append(": ring ").Append(ring).Append(" px = ").Append((ring / 8f).ToString("0.0")).Append(" m; the gun's own range ")
                      .Append(gun.AttackRadius).Append(" px; farthest target centre the rule lets it engage ").Append(far).Append(" px (difference ").Append(far - ring)
                      .Append(" px, allowed ").Append(HalfTilePx).Append("); real shot at a ").Append(target).Append(" at ").Append(far).Append(" px within ").Append(ShotSeconds).Append(" s: ")
                      .Append(at ? "yes" : "NO").Append("; real shot at ").Append(far + HalfTilePx).Append(" px: ").Append(beyond ? "YES" : "none").Append('\n');
                }
            }
            finally { ZWorld.GunGroundRule = keep; }
            sb.Append(violations == 0 ? "GUN PLACEMENT COUNTED TEST: PASS (0 violations)" : "GUN PLACEMENT COUNTED TEST: FAIL (" + violations + " violations)");
            return sb.ToString();
        }

        /// <summary>L02 without units, flags and crates, nothing building, no CPU; and an open lane on it: a row two tiles high of
        /// plain ground or road, long enough for the gun, its range and a target, with no object within a tile of it.</summary>
        static ZWorld Bare(out int lx, out int ly, string type)
        {
            lx = ly = -1;
            var w = ZWorld.Create(SettingsText, MapText("L02"), 1);
            w.Bots.Clear();
            w.Objects.RemoveAll(o => o.IsUnit || o.Type == "grenades" || o.Type == "flag");
            w.Flags.Clear();
            foreach (var b in w.Objects) if (b.Kind == ZKind.Building) b.StopBuildingProduction();
            int len = (OzGunRange.RadiusPx(w, type) + 24) / 16 + 6;
            for (int ty = 2; ty < w.Map.height - 4; ty++)
                for (int tx = 2; tx + len < w.Map.width - 2; tx++)
                {
                    bool open = true;
                    for (int j = 0; j < 2 && open; j++)
                        for (int i = 0; i < len && open; i++)
                        {
                            var c = w.Map.ClassAt(tx + i, ty + j);
                            if (c != ZClass.Ground && c != ZClass.Road) open = false;
                        }
                    if (!open) continue;
                    int x0 = tx * 16 - 16, x1 = (tx + len) * 16 + 16, y0 = ty * 16 - 16, y1 = ty * 16 + 48;
                    foreach (var o in w.Objects)
                        if (o.LocX < x1 && o.LocX + o.WidthPix > x0 && o.LocY < y1 && o.LocY + o.HeightPix > y0) { open = false; break; }
                    if (!open) continue;
                    lx = tx; ly = ty;
                    return w;
                }
            return null;
        }

        /// <summary>A fresh bare world: the gun on the lane, one enemy target with its centre distPx from the gun's centre; true when
        /// the gun fires (a hitscan or a missile of its own) within ShotSeconds.</summary>
        static bool Shot(string type, string targetType, int distPx)
        {
            var w = Bare(out int lx, out int ly, type);
            if (w == null) return false;
            var gun = w.CreateObject("cannon", type, lx * 16, ly * 16, Team.Red);
            var target = w.CreateObject("vehicle", targetType, gun.CenterX + distPx - 16, gun.LocY, Team.Blue);
            int ticks = (int)(ShotSeconds / ZConst.TICK);
            // the longest lane lies beside the enemy fort: its defender would step out and be shot at instead of the target, so
            // the fort defender rule is off for these ticks, and only a shot while the gun aims at the target counts
            bool keepDefender = ZWorld.FortDefenderDeviation;
            ZWorld.FortDefenderDeviation = false;
            try
            {
                for (int i = 0; i < ticks; i++)
                {
                    w.Tick(ZConst.TICK);
                    foreach (var e in w.Events)
                        if ((e.type == ZEventType.Hitscan || e.type == ZEventType.MissileFired) && e.objId == gun.Id && gun.AttackObject == target) return true;
                    w.Events.Clear();
                }
            }
            finally { ZWorld.FortDefenderDeviation = keepDefender; }
            return false;
        }
    }
}
