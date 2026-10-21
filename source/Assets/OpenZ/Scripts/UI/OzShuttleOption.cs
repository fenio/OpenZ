using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// K347: no longer an OPTIONS row. The SHUTTLE switch is the battlefield screen's box now (UI/OzShuttleStart.cs, drawn by
    /// UI/OpenZMainMenu.ShuttleStart.cs; level 16 on EASY / MEDIUM, ON by default), and the level reads that box when it starts; both
    /// menus skip this row (OzShuttleStart.InOptions). What stays here: the old row's label and words, and its key oz_shuttle, which
    /// nothing reads any more (a switch turned off here before never turns the new box off).
    /// Before: the OPTIONS row SHUTTLE (rule DV118, Sim/ZWorld.Shuttle.cs) on the ADVANCED tab of both menus, ON by default, saved in
    /// oz_shuttle, read when a level started; OFF: no shuttle on level 16 at all (not the playable one, not the parked ship).
    /// </summary>
    public static class OzShuttleOption
    {
        public const string Pref = "oz_shuttle";
        public const string Label = "SHUTTLE";
        public const string Tip = "SHUTTLE|On (default): on the jungle level the shuttle beside your fort is yours. Order one squad into it, then right-click a place: it takes off, flies there and lands; EXIT lets the squad out at its back. It cannot land on water, rocks, buildings or units (SHUTTLE ORDERS: only to places where it can land, or anywhere: then it hovers there until you move it), it never takes a flag and it has no gun. Destroyed, in the air or on the ground, the squad inside is lost. Off: no shuttle on that level. Read when a level starts.|";

        public static bool On
        {
            get => PlayerPrefs.GetInt(Pref, 1) == 1;
            set
            {
                PlayerPrefs.SetInt(Pref, value ? 1 : 0); PlayerPrefs.Save();
                bool live = ZGame.I != null && ZGame.I.World != null;
                Debug.Log($"[OpenZ Options] shuttle: {(value ? "ON" : "OFF")} ({Pref} {(value ? 1 : 0)}, saved, read at the next level start{(live ? "; this match keeps " + (ZGame.I.World.PlayableShuttle != null ? "its shuttle" : "no shuttle") : "")})");
            }
        }
    }
}
