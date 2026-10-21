using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// Version 1.1, the Options row FAST UNITS PASS SLOW ONES (ADVANCED tab, under SMOOTHER PATHING, both Options screens). OFF by
    /// default (it is a test: the player can try it); saved in oz_overtake; read by View/ZGame.cs at every match start
    /// (ZWorld.OvertakeDeviation; always off in 'Zod exact'). The rule: Sim/ZOvertake.cs.
    /// Off = the rules of version 1.0 exactly: a unit queues behind any unit going its way.
    /// </summary>
    public static class OzOvertake
    {
        public const string Label = "FAST UNITS PASS SLOW ONES", OvertakePref = "oz_overtake";   // one key constant per name (the defaults check reads the names)
        public const string Tip = "FAST UNITS PASS SLOW ONES|Off (default): every unit waits behind the slower one in front, as before. On: a faster unit drives round a slower one that goes the same way when there is room beside it; a group sent at the enemy stays together. Applies from the next match.|";
        public static bool On
        {
            get => PlayerPrefs.GetInt(OvertakePref, 0) == 1;
            set { PlayerPrefs.SetInt(OvertakePref, value ? 1 : 0); PlayerPrefs.Save(); Debug.Log($"[OpenZ Options] fast units pass slow ones: {(value ? "ON" : "OFF (units queue as before)")} ({OvertakePref} {(value ? 1 : 0)}, saved, from the next match)"); }
        }
    }
}
