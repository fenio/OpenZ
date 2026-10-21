using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// The OPTIONS row ENEMY STRIKE GROUPS (ADVANCED tab, first row, in the main menu and in the pause menu: the GAME tab has no room left): the saved setting of the
    /// rule DV107 (Sim/ZBot.StrikeGroup.cs). ON by default, saved in oz_strike_groups at once. ZGame reads it at every match start
    /// (never on in the engine-exact mode). Changed during a match it also takes effect at once: turned off, a group that is forming
    /// or on its way breaks up into single units in the same moment (every unit keeps the order it has); turned on, the enemy may
    /// start gathering again as the rule's own clocks allow.
    /// </summary>
    public static class OzStrikeGroups
    {
        public const string Pref = "oz_strike_groups";
        public const string Label = "ENEMY STRIKE GROUPS";
        public const string Tip = "ENEMY STRIKE GROUPS|On (default): after 8 minutes of play the enemy now and then gathers a group in front of its fort and sends it together at your biggest army. Off: the enemy sends single units only.|";

        public static bool On
        {
            get => PlayerPrefs.GetInt(Pref, 1) == 1;
            set
            {
                PlayerPrefs.SetInt(Pref, value ? 1 : 0); PlayerPrefs.Save();
                bool live = ZGame.I != null && ZGame.I.World != null;
                if (live) ZWorld.StrikeGroupDeviation = value && PlayerPrefs.GetInt("oz_zod_exact", 0) == 0;
                Debug.Log($"[OpenZ Options] enemy strike groups: {(value ? "ON" : "OFF")} ({Pref} {(value ? 1 : 0)}, saved{(live ? ", live: the rule is " + (ZWorld.StrikeGroupDeviation ? "on" : "off") + " in this match" : ", read at the next match start")})");
            }
        }
    }
}
