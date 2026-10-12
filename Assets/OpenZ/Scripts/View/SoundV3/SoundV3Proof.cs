using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    ///   SoundV3Proof.Begin()               marks the start of a run (OzSoundLog + AudioDirector counters reset)
    ///   SoundV3Proof.Report(path)          writes, for every sound_v3 event id and CombatAudio key: plays, drops (by reason), the loudest
    ///                                      and median volume; the sound_v3 ids that never played (FAIL unless listed as "not in this run");
    ///                                      and duplicates (the same key + clip twice within 30 ms at the same spot = one action heard twice).
    /// The events themselves are also in AudioDirector.CoverageReport / FireEveryEvent (the table's coverage rows).
    /// </summary>
    public static class SoundV3Proof
    {
        /// <summary>The new table events and CombatAudio keys of sound_v3 (the Play proof expects each one to play at least once
        /// in the scripted fight + UI walk, except the staged details_v1 props).</summary>
        public static readonly string[] NewEvents =
        {
            "veh_fort_traverse_loop", "veh_fort_traverse_start", "veh_fort_traverse_stop", "veh_fort_elevate", "wpn_fort_ready", "wpn_target_lock",
            "veh_turret_slew_loop", "veh_turret_slew_start", "veh_turret_on_target", "veh_tracks_heavy_loop", "wpn_jeep_spinup", "wpn_jeep_spindown",
            "wpn_mech_arm_spinup", "wpn_mech_arm_spindown", "wpn_grenade_fuse", "wpn_tough_reload", "wpn_sniper_bolt", "imp_shrapnel_tink",
            "rob_knockdown_land", "rob_getup", "dst_fire_ignite", "bld_factory_exhaust", "bld_factory_vent", "bld_bridge_rise", "bld_bridge_drip",
            "bld_bridge_countdown", "bld_fort_lift", "bld_radar_loop_enemy", "ui_victory_hit", "ui_defeat_hit", "ui_target_lock", "ui_target_nono",
            "ui_order_capture", "ui_order_enter", "ui_order_pickup", "ui_order_repair", "ui_order_exit", "ui_unit_ready", "ui_fort_alarm",
            "ui_disabled", "ui_select_empty", "ui_select_all", "ui_drag_pick", "ui_drag_cancel", "ui_world_hover", "ui_rally_arm", "ui_rally_point",
            "ui_rally_clear", "ui_band_whoosh", "ui_plate_land", "ui_chart_draw", "ui_preview_sfx", "ui_preview_voice",
            "ann_gun_captured", "ann_building_destroyed", "vo_grenade_warning",
            // existing events that play for the first time (were silent / never triggered)
            "wpn_breech_clank", "veh_tracks_squeal", "veh_suspension", "veh_engine_shutdown", "veh_brake", "imp_bullet_sand", "imp_bullet_stone",
            "exp_debris_rain", "dst_debris_concrete", "bld_factory_accent",
        };
        public static readonly string[] NewKeys = { "mech_rocket", "mc_rocket", "mech_flight", "shell_flight", "grenade", "grenade_land", "debris", "explosion", "turret", "pickup", "built", "driverhit", "sniped" };

        static float t0;
        public static void Begin()
        {
            t0 = Time.unscaledTime;
            if (AudioDirector.I) AudioDirector.I.ResetCounters();
            Debug.Log("[SoundV3Proof] begin at " + t0.ToString("0.0"));
        }

        public static string Report(string path = null)
        {
            var es = OzSoundLog.Since(t0);
            var sb = new StringBuilder();
            sb.AppendLine($"# sound_v3 Play proof ({(ZGame.I ? ZGame.I.level : "menu")}, {es.Count} log entries since {t0:0.0} s)\n");
            sb.AppendLine("| id / key | plays | drops (reason) | vol max | vol median |");
            sb.AppendLine("|---|---|---|---|---|");
            var silent = new List<string>();
            foreach (var id in NewEvents.Concat(NewKeys))
            {
                var mine = es.Where(e => e.key == id).ToList();
                var plays = mine.Where(e => e.outcome.StartsWith("PLAY")).ToList();
                var drops = mine.Where(e => e.outcome.StartsWith("DROP")).GroupBy(e => e.outcome).Select(g => $"{g.Key.Substring(5)} x{g.Count()}");
                float max = plays.Count > 0 ? plays.Max(e => e.vol) : 0f;
                float med = plays.Count > 0 ? plays.Select(e => e.vol).OrderBy(v => v).ElementAt(plays.Count / 2) : 0f;
                sb.AppendLine($"| {id} | {plays.Count} | {string.Join(", ", drops)} | {max:0.000} | {med:0.000} |");
                if (plays.Count == 0) silent.Add(id);
            }
            // duplicates: one action heard twice (same key + clip within 30 ms, within 0.5 m of listener distance)
            var dup = new List<string>();
            var ps = es.Where(e => e.outcome.StartsWith("PLAY")).ToList();
            for (int i = 1; i < ps.Count; i++)
                for (int j = i - 1; j >= 0 && ps[i].t - ps[j].t < 0.03f; j--)
                    if (ps[i].key == ps[j].key && ps[i].clip == ps[j].clip && Mathf.Abs(ps[i].dist - ps[j].dist) < 0.5f)
                        dup.Add($"{ps[i].t:0.00} {ps[i].key} {ps[i].clip}");
            sb.AppendLine($"\nnever played in this run ({silent.Count}): {string.Join(", ", silent)}");
            sb.AppendLine($"duplicates ({dup.Count}): {string.Join("; ", dup.Take(40))}");
            if (AudioDirector.I) sb.AppendLine("\n## AudioDirector coverage (this run)\n\n" + AudioDirector.I.CoverageReport());
            string s = sb.ToString();
            if (!string.IsNullOrEmpty(path)) { try { System.IO.File.WriteAllText(path, s); } catch (System.Exception ex) { Debug.LogWarning("[SoundV3Proof] " + ex.Message); } }
            Debug.Log($"[SoundV3Proof] {silent.Count} never played, {dup.Count} duplicates -> {path}");
            return s;
        }
    }
}
