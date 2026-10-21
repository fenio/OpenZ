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
    /// parked vehicles, ZWorld.L17Crews.cs), measured headless in the same sim as the parity rows (L17 with DV32 parked start, no bots,
    /// no orders, 45 s, once with the switch on and once off; the exit lanes rule DV66 on in both, as in every normal game).
    /// Menu OpenZ/Deviations/L17 Start Crews (oz_l17_crews; read by ZGame at
    /// every start: Easy + human vs CPU only, off in 'Zod exact').
    /// </summary>
    public static partial class OpenZParity
    {
        /// <summary>L17, DV32 on, no bots, crews for red: crew robots sent, Enter orders, red-owned vehicles at 45 s, first UnitBuilt time.</summary>
        static (int sentR, int sentB, int orders, int redVeh, int parkedR, double first) L17CrewsProbe(bool on)
        {
            var team = ZWorld.L17CrewsTeam; ZWorld.L17CrewsTeam = Team.Red;
            try
            {
                // The start crews exist only in a normal game, and every normal game keeps the exit lanes clear (DV66). Without that
                // rule the fort's exit point lies under a parked start vehicle and the crews jam there, so the probe turns it on for
                // itself whatever the editor's switches were left at (off after a script reload, on after a Play session).
                return WithSwitch(() => ZWorld.ExitLanesDeviation, v => ZWorld.ExitLanesDeviation = v, true, () =>
                       WithSwitch(() => ZWorld.L17CrewsDeviation, v => ZWorld.L17CrewsDeviation = v, on, () =>
                {
                    var w = ZWorld.Create(SettingsText, MapText("L17"), 1, true);
                    int parkedR = w.Objects.Count(o => o.Kind == ZKind.Vehicle && o.Owner == Team.Neutral && o.Source != null && o.Source.owner == "red");
                    double first = -1;
                    while (w.Time < 45)
                    {
                        w.Tick(ZConst.TICK);
                        foreach (var e in w.Events)
                            if (e.type == ZEventType.UnitBuilt && first < 0) { var b = w.GetAny(e.otherId); if (b != null && b.IsFort) first = w.Time; }
                        w.Events.Clear();
                    }
                    int redVeh = w.Objects.Count(o => o.Kind == ZKind.Vehicle && o.Owner == Team.Red && !o.Destroyed);
                    return (w.L17CrewsSent[1], w.L17CrewsSent[2], w.L17CrewsBoardOrders[1], redVeh, parkedR, first);
                }));
            }
            finally { ZWorld.L17CrewsTeam = team; }
        }

        static Deviation DevL17Crews(Func<string, int, int> getInt)
        {
            var on = L17CrewsProbe(true); var off = L17CrewsProbe(false);
            bool pref = getInt("oz_l17_crews", 1) == 1 && getInt("oz_zod_exact", 0) == 0 && getInt("oz_l17_parked", 1) == 1;
            // a crew goes only to a start vehicle still empty when it leaves the door (red's own robots may have taken one meanwhile)
            bool ok = on.sentR > 0 && on.sentR == on.orders * Math.Max(1, ZL17Balance.CrewSquadSize) && on.orders <= on.parkedR && on.sentB == 0 &&
                      off.sentR == 0 && off.sentB == 0 && on.redVeh >= on.parkedR && on.redVeh > off.redVeh && on.first >= 0;
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            return new Deviation
            {
                id = "DV63", change = "L17 EASY start crews: the player's fort sends a grunt driver into each of its parked start vehicles (DV32 follow-up)",
                zod = "every owned map vehicle starts with one grunt driver inside (zvehicle.cpp:155-168); DV32 parks L17's start vehicles empty, which removes those drivers",
                openz = $"on L17 EASY in human vs CPU games, with DV32 on, from {ZL17Balance.CrewFirstSeconds.ToString("0.0", inv)} s the player's (red) fort sends out one {ZL17Balance.CrewType} per parked start vehicle of its side still empty " +
                        $"(Zod's start drivers), one every {ZL17Balance.CrewGapSeconds.ToString("0.0", inv)} s through its door (BuildingCreateUnit: door, roll-out sound, Rachel 'Robot manufactured', HUD toast; the fort's production timer untouched), " +
                        "each walking straight into its own vehicle (nearest the fort first: boarding, capture sound). Reason: the CPU is slow to take the car park while a human goes straight there, " +
                        "so human vs CPU is the target, not the bot-vs-bot share; Medium, Hard, the CPU side and bot matches keep the live L17 start (no crews)",
                measured = $"L17 headless, no bots, exit lanes kept clear (DV66) as in every normal game, 45 s: on {on.sentR}/{on.sentB} crew robots out of the fort doors (red / blue) for {on.parkedR} red parked vehicles, {on.orders} enter orders, first at {(on.first >= 0 ? on.first.ToString("0.0", inv) + " s" : "never")}, red-owned vehicles {on.redVeh}; off {off.sentR}/{off.sentB} crews, red-owned vehicles {off.redVeh}",
                toggle = $"PlayerPrefs oz_l17_crews = {(getInt("oz_l17_crews", 1) == 1 ? "1 (on, default)" : "0 (off)")}; menu OpenZ/Deviations/L17 Start Crews; Easy + human vs CPU only; needs oz_l17_parked (DV32) and the exit lanes rule (DV66, oz_exit_lanes); off in 'Zod exact' (oz_zod_exact = 1)",
                status = !ok ? DevCheck : pref ? DevActive : DevActive + " (off by pref)",
            };
        }

#if !OPENZ_HARNESS
        const string MenuL17Crews = "OpenZ/Deviations/L17 Start Crews (oz_l17_crews)";
        [MenuItem(MenuL17Crews)] static void TL17Crews() => NbToggle("oz_l17_crews", 1, "L17 Easy start crews (DV63)");
        [MenuItem(MenuL17Crews, true)] static bool TL17CrewsC() { Menu.SetChecked(MenuL17Crews, PlayerPrefs.GetInt("oz_l17_crews", 1) == 1); return true; }

        [MenuItem("OpenZ/Deviations/Probe DV63 (L17 start crews)")]
        static void ProbeL17CrewsMenu()
        {
            var d = DevL17Crews((k, def) => PlayerPrefs.GetInt(k, def));
            Debug.Log($"[{d.id}] {d.status}: {d.measured}");
        }
#endif
    }
}
