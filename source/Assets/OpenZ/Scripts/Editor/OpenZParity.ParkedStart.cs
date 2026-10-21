using System;
using System.Linq;
using OpenZ.Sim;
#if !OPENZ_HARNESS
using UnityEditor;
using UnityEngine;
#endif

namespace OpenZ.EditorTools
{
    /// <summary>
    /// Cause: Zod's idle
    /// auto-grab (zobject.cpp, auto_grab_flag_distance 220 px) sends the crewed start vehicles to the neutral flags by the fort at
    /// 0 s; the west red flag (72,41) stands against a rock wall, the converging vehicles get blocked and blast the rocks
    /// (DoAttackImpassableAtCoords). With DV32 the L17 map's start vehicles of both sides begin parked, empty and neutral.
    /// Measured headless on L17, no bots, no orders, once on and once Zod: owned vehicles at 0 s, vehicles that moved and rocks
    /// destroyed in the first 20 s, and vehicles crewed by robots' own idle auto-grab by 120 s.
    /// </summary>
    public static partial class OpenZParity
    {
        static (int owned, int neutral, int moved, int rocks, int crewedLater, int total) ParkedProbe(bool on)
        {
            var w = ZWorld.Create(SettingsText, MapText("L17"), 1, on);
            var start = w.Objects.Where(o => o.Kind == ZKind.Vehicle).ToDictionary(o => o.Id, o => (o.LocX, o.LocY));
            int owned = w.Objects.Count(o => o.Kind == ZKind.Vehicle && o.Owner != Team.Neutral);
            int neutral = w.Objects.Count(o => o.Kind == ZKind.Vehicle && o.Owner == Team.Neutral);
            int rocks0 = w.Objects.Count(o => o.Type == "rock" && !o.Destroyed);
            int moved = 0, rocks = 0;
            while (w.Time < 120)
            {
                w.Tick(ZConst.TICK); w.Events.Clear();
                if (moved == 0 && w.Time >= 20)
                {
                    moved = w.Objects.Count(o => o.Kind == ZKind.Vehicle && (!on || o.Owner == Team.Neutral) && start.TryGetValue(o.Id, out var p) && (Math.Abs(o.LocX - p.LocX) > 8 || Math.Abs(o.LocY - p.LocY) > 8));
                    rocks = rocks0 - w.Objects.Count(o => o.Type == "rock" && !o.Destroyed);
                    if (moved == 0) moved = -1;
                }
            }
            int crewedLater = w.Objects.Count(o => o.Kind == ZKind.Vehicle && start.ContainsKey(o.Id) && o.Owner != Team.Neutral);
            return (owned, neutral, Math.Max(moved, 0), rocks, crewedLater, start.Count);
        }

        static Deviation DevParkedStart(Func<string, int, int> getInt)
        {
            var on = ParkedProbe(true); var off = ParkedProbe(false);
            bool pref = getInt("oz_l17_parked", 1) == 1 && getInt("oz_zod_exact", 0) == 0;
            bool ok = on.owned == 0 && on.moved == 0 && on.rocks == 0 && off.owned > 0;
            return new Deviation
            {
                id = "DV32", change = "L17 start vehicles begin parked and empty (both sides); robots collect them",
                zod = "an owned map vehicle starts with one grunt driver (zvehicle.cpp:155-168); idle, it auto-grabs flags within 220 px, so at 0 s the red vehicles race to the two fort flags and blast the rock wall by the west one",
                openz = "on L17 every map-placed vehicle starts neutral with no driver where the map parks it (cannons keep their crews); the L17 Easy extras (MLRS, heavy) are parked empty too; any robot boards them (Zod's Enter rule, idle auto-grab 220 px, the CPU's own vehicle targets)",
                measured = $"L17 headless, no bots, no orders: DV32 {on.owned} owned / {on.neutral} empty vehicles at 0 s, {on.moved} empty one(s) moved (drift) and {on.rocks} rock(s) destroyed by 20 s, {on.crewedLater} boarded by idle robots by 120 s; " +
                           $"Zod {off.owned} owned at 0 s, {off.moved} moved and {off.rocks} rock(s) destroyed by 20 s",
                toggle = $"PlayerPrefs oz_l17_parked = {(getInt("oz_l17_parked", 1) == 1 ? "1 (on, default)" : "0 (off)")}; menu OpenZ/Deviations/L17 Start Vehicles Parked Empty; off in 'Zod exact' (oz_zod_exact = 1); bot matches follow the same switch",
                status = !ok ? DevCheck : pref ? DevActive : DevActive + " (off by pref)",
            };
        }

        /// <summary></summary>
        static Deviation DevRadarDots(Func<string, int, int> getInt) => new Deviation
        {
            id = "DV33", change = "Minimap shows enemy units only while the player owns a radar (view only)",
            zod = "the radar has no gameplay effect in Zod (bradar.cpp; zplayer.cpp:1146-1188 only plays its sound); the minimap shows every unit",
            openz = "OpenZMinimap: enemy robots / vehicles / cannons are drawn only while an intact radar of the player's stands; own and neutral units, buildings, flags and zones always; the sim and the CPU are unchanged",
            measured = "view rule (no sim state); Play proof in MASTER_TODO NB41",
            toggle = $"PlayerPrefs oz_radar_dots = {(getInt("oz_radar_dots", 1) == 1 ? "1 (on, default)" : "0 (off: every unit shown)")}",
            status = getInt("oz_radar_dots", 1) == 1 ? DevActive : DevActive + " (off by pref)",
        };

        /// <summary></summary>
        static Deviation DevGrabPolicy(Func<string, int, int> getInt) => new Deviation
        {
            id = "DV38", change = "Auto-capture: one unit per flag / vehicle / crate; units the player ordered hold (human teams)",
            zod = "every idle leader auto-grabs the nearest flag / vehicle / crate within 220 px (zobject.cpp:1603-1853), several units may run for the same one, and a unit that finished a player move grabs too",
            openz = "on a human team a target another leader already walks to is skipped, and a unit whose last order came from the player never auto-grabs (it still engages enemies); new units from factories auto-grab as in Zod; every team only auto-grabs a flag / vehicle / cannon / crate it can see; the CPU's own orders are unchanged" +
                    $" (with DV84 on: a held unit takes an empty vehicle or gun within Zod's own 220 px, a try that fails or stalls is given up for {ZWorld.GrabFailMemory:0} s so that the next closest unit goes, and a unit on a plain move takes a flag or an empty vehicle within {ZWorld.CloseGrabPx} px of its way; OpenZParity.V11GrabCertain.cs)",
            measured = "Play proof in MASTER_TODO (auto-grab row); the rules rows run with it off",
            toggle = $"PlayerPrefs oz_grab_policy = {(getInt("oz_grab_policy", 1) == 1 ? "1 (on, default)" : "0 (off)")}; off in 'Zod exact'",
            status = getInt("oz_grab_policy", 1) == 1 ? DevActive : DevActive + " (off by pref)",
        };

#if !OPENZ_HARNESS
        const string ParkedMenu = "OpenZ/Deviations/L17 Start Vehicles Parked Empty (oz_l17_parked)";

        [MenuItem(ParkedMenu)]
        static void ToggleParked()
        {
            bool on = PlayerPrefs.GetInt("oz_l17_parked", 1) != 1;
            PlayerPrefs.SetInt("oz_l17_parked", on ? 1 : 0); PlayerPrefs.Save();
            Debug.Log($"OpenZ L17 parked start vehicles (deviation DV32) from the next Play: {(on ? "ON: empty and neutral, robots collect them" : "OFF: Zod's crewed start vehicles")}");
        }
        [MenuItem(ParkedMenu, true)]
        static bool ToggleParkedCheck() { Menu.SetChecked(ParkedMenu, PlayerPrefs.GetInt("oz_l17_parked", 1) == 1); return true; }

        [MenuItem("OpenZ/Deviations/Probe DV32 Parked Start")]
        static void ProbeParkedMenu() { var d = DevParkedStart((k, def) => PlayerPrefs.GetInt(k, def)); Debug.Log($"[DV32] {d.status}: {d.measured}"); }
#endif
    }
}
