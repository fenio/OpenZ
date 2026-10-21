using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// The OPTIONS row SHUTTLE ORDERS (rule DV118, K256g; ADVANCED tab, right under SHUTTLE, in the main menu and in the pause menu), a cycle
    /// row saved as oz_shuttle_orders, read at once when it changes (no restart):
    ///  LANDABLE SPOTS ONLY (default)  a place where the shuttle can never land (water, rocks, buildings, trees, the map's edge: the landing
    ///                                 spot rule with the units left out, Sim/ZWorld.ShuttleOrders.cs) shows the NO cursor while only the
    ///                                 shuttle (or only shuttles) would take the move, and a click there gives the shuttle no order (the
    ///                                 refusal blip when no selected unit took it; in a mixed group the others go, the shuttle stays and its
    ///                                 mark turns red for a moment: View/OzShuttleOrderAim.cs).
    ///  ANYWHERE                       the orders as they were: it flies to any place and hovers where it cannot land, until it is moved.
    /// View and input side only: the rules never read it.
    /// </summary>
    public static class OzShuttleOrdersOption
    {
        public const string Pref = "oz_shuttle_orders";
        public const string Label = "SHUTTLE ORDERS";
        public const string Tip = "SHUTTLE ORDERS|Landable spots only (default): over water, rocks, buildings, trees or the edge of the map, where the shuttle can never land, the cursor shows NO and a click gives the shuttle no order; other selected units still go. Anywhere: the shuttle flies to any place and hovers where it cannot land, until you move it. Changes at once.|";
        public static readonly string[] Names = { "LANDABLE SPOTS ONLY", "ANYWHERE" };
        public const int LandableSpotsOnly = 0, Anywhere = 1;
        static int setting = -1;

        public static int Setting
        {
            get { if (setting < 0) setting = PlayerPrefs.GetInt(Pref, LandableSpotsOnly) == Anywhere ? Anywhere : LandableSpotsOnly; return setting; }
            set
            {
                setting = value == Anywhere ? Anywhere : LandableSpotsOnly;
                PlayerPrefs.SetInt(Pref, setting); PlayerPrefs.Save();
                Debug.Log($"[OpenZ Options] shuttle orders: {Names[setting]} ({Pref} {setting}, saved, live)");
            }
        }
        /// <summary>LANDABLE SPOTS ONLY: no order to a place where the shuttle can never land.</summary>
        public static bool LandableOnly => Setting == LandableSpotsOnly;
        /// <summary>The cycle row's value.</summary>
        public static string Name => Names[Setting];
        /// <summary>The cycle row's step: two values, either arrow flips it.</summary>
        public static void Step(int dir) => Setting = Setting == Anywhere ? LandableSpotsOnly : Anywhere;
    }
}
