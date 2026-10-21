#if !OPENZ_HARNESS
using System;
using System.Collections.Generic;
using OpenZ.Sim;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// on top of DV4's Easy computer). Measured headless: the counted check of Editor/OzEasySlowCount.cs on level 2 (one seed, 15 min, the switch on
    /// and off; MEDIUM / HARD / a computer match untouched; the whole check on three levels: menu OpenZ/V1.1/CPU: K434 Easy Slower Decisions Check).</summary>
    public static partial class OpenZParity
    {
        static void AddEasySlowDeviation(List<Deviation> list)
        {
            string measured; bool pass;
            try
            {
                string check = OzEasySlowCount.Check(SettingsText, MapText, 1, 15, null, new[] { "L02" });
                measured = OzV11Kit.VerdictCell(check, out pass) + " (the whole report: menu OpenZ/V1.1/CPU: K434 Easy Slower Decisions Check)";
            }
            catch (Exception ex) { measured = "the probe threw " + ex.GetType().Name + ": " + ex.Message; pass = false; }
            double k = ZWorld.EasyDecisionSlow, tick = ZBot.AI_INTERVAL * ZBot.EASY_AI_INTERVAL_SCALE;
            list.Add(new Deviation
            {
                id = "DV127", change = "The EASY computer takes its big decisions 30 % slower: its order rounds, strike groups, fort finish and idle sweep wait 1.3 x longer; production, combat and MEDIUM / HARD unchanged",
                zod = "one computer, the Zod bot (zbot.cpp: a look every 1 s, rounds 4 / 5 / 8 / 12 s apart); DV4's Easy computer already looked every 2 s, rounds x 2.5, first order after 45 s",
                openz = $"EASY only (ZBotDifficulty.Easy; MEDIUM / HARD play the Zod computer, never reached): the order rounds on their own {tick * k:0.0} s clock, round delays x {ZBot.EASY_ORDER_DELAY_SCALE * k:0.00}, the first round from {ZBot.EASY_START_DELAY * k:0.0} s " +
                        $"(every gap exactly x {k:0.0}: 13 / 18.2 / 26 / 39 s, was 10 / 14 / 20 / 30 s); the fort finish (DV60) on the same clock; strike groups (DV107) from {ZWorld.StrikeStartSec * k / 60:0.0} min, {ZWorld.StrikeGapSec * k / 60:0.0} min apart, " +
                        $"{ZWorld.StrikeRetrySec * k:0} s after a given-up window, a ready group stands at its fort until its opening + {k:0.0} x its time to get ready (nobody joins meanwhile), re-target every {ZWorld.StrikeRetargetSec * k:0.0} s; " +
                        $"the idle sweep (DV70) every {ZWorld.IdleSweepEvery * k / 60:0.0} min, {ZWorld.IdleSweepStillSec * k:0} s standing. Production (the {tick:0} s look from {ZBot.EASY_START_DELAY:0} s), build times, unit stats, unit-level combat and the Easy limits unchanged. Deterministic: game time",
                measured = measured,
                toggle = "ZWorld.EasyCpuSlowerOn (code switch, on; off = DV4's Easy computer as it was); matters only on EASY (PlayerPrefs oz_difficulty 1), human vs computer",
                status = !pass ? DevCheck : ZWorld.EasyCpuSlowerOn ? DevActive : DevActive + " (off)",   // ACTIVE only while the rule is on
            });
        }
    }
}
#endif
