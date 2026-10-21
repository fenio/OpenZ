using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// The SHUTTLE box of the battlefield screen (rule DV118; K347). It took the place of the OPTIONS row SHUTTLE: the level's own
    /// parked shuttle is an extra chosen when a level starts, like the extra vehicles of HEAD START.
    ///  * Offered for levels 16 and 17 (K444), on EASY and MEDIUM (and only while the shuttle rule is on, not in 'Zod exact'). Both have the
    ///    SHUTTLE slot in the deploy bar's row (<see cref="HasSlot"/>); on HARD the slot stays, locked (<see cref="LockedRow"/>), and
    ///    the level starts without the box's shuttle; level 2 has no slot and no shuttle of its own, as before.
    ///  * ON by default, saved at once in <see cref="Pref"/> and remembered like HEAD START (oz_headstart).
    ///  * Read when a level starts (ZGame -> ZWorld.ShuttleOn = <see cref="RuleAtStart"/>):
    ///      level 16, EASY / MEDIUM, the box ON   Alan and Brad's shuttle stands by the player's fort (the playable shuttle);
    ///      level 16, EASY / MEDIUM, the box OFF  no shuttle on the level (not the playable one, not the parked decoration);
    ///      level 16, HARD                        no shuttle on the level;
    ///      every other level                     the rule switch stays on, as before (nothing of the box is read there).
    ///  * K444 (rule DV134): level 17 "Car Park" reads the same box (the same default and saved setting): on EASY / MEDIUM with the box ON
    ///    the player's shuttle parks by the red fort (ZWorld.L17StartShuttle = <see cref="L17StartAtStart"/>); the rule switch above stays on
    ///    there on every difficulty, and level 17's EMPTY car-park shuttle stands there whatever the box says (Sim/ZWorld.L17Shuttles.cs).
    ///  * The testing panel (P) drops shuttles (crewed and empty) on every level and every difficulty, the box on or off: it never
    ///    reads the box (ZWorld.SpawnShuttle only asks for the shuttle rule).
    ///  * The old OPTIONS key oz_shuttle is not read any more, so a switch turned off there before never turns this box off.
    /// The battlefield screen draws the box as the third block of its deploy bar (UI/OpenZMainMenu.ShuttleStart.cs, the bar:
    /// UI/OpenZMainMenu.DeployBar.cs); this class holds its state, its words and the rule.
    /// </summary>
    public static class OzShuttleStart
    {
        public const string Pref = "oz_start_shuttle";
        /// <summary>The one level with a shuttle of its own.</summary>
        public const string Level = "L16";
        /// <summary>K444: the second level with the box (its shuttle parks by the red fort; Sim/ZWorld.L17Shuttles.cs).</summary>
        public const string Level17 = "L17";
        /// <summary>The levels with the box: 16 and 17.</summary>
        public static bool BoxLevel(string level) => level == Level || level == Level17;
        public const string Label = "SHUTTLE";
        /// <summary>The switch row's words, the hint under it (on / off; the switch itself says ON / OFF), the locked slot on HARD, the tooltip.</summary>
        public const string RowText = "+1 SHUTTLE", HintOn = "PARKED BY YOUR FORT", HintOff = "NO SHUTTLE";
        /// <summary>K444: the hint with the box off on level 17, whose EMPTY car-park shuttle stands there whatever the box says.</summary>
        public const string HintOffL17 = "NONE BY YOUR FORT";
        public static string HintOffFor(string level) => level == Level17 ? HintOffL17 : HintOff;
        public const string LockedRow = "NOT ON HARD", LockedHint = "EASY · MEDIUM ONLY";
        public const string TipTitle = "SHUTTLE · KEY ";
        public const string TipBody = "Alan and Brad's shuttle waits by your fort.\nOrder one squad into it, then right-click a place:\nit flies there and lands.\nL16 JUNGLE and L17 CAR PARK on EASY and MEDIUM.\nHARD or OFF: none by your fort.";

        /// <summary>The box (saved at once; ON when nothing is saved yet).</summary>
        public static bool On
        {
            get => PlayerPrefs.GetInt(Pref, 1) == 1;
            set { PlayerPrefs.SetInt(Pref, value ? 1 : 0); PlayerPrefs.Save(); }
        }

        /// <summary>The box is shown for this level and difficulty (zodExact: the 'Zod exact' rules, no playable shuttle).</summary>
        public static bool Offered(string level, GameDifficulty d, bool zodExact) => BoxLevel(level) && d != GameDifficulty.Hard && !zodExact;   // K444: levels 16 and 17
        public static bool Offered(string level, GameDifficulty d) => Offered(level, d, PlayerPrefs.GetInt("oz_zod_exact", 0) == 1);

        /// <summary>The deploy bar has the SHUTTLE slot for this level (levels 16 and 17, not 'Zod exact'): offered on EASY / MEDIUM, locked
        /// on HARD. Without it (level 2) the bar shows DIFFICULTY and HEAD START only.</summary>
        public static bool HasSlot(string level, bool zodExact) => BoxLevel(level) && !zodExact;
        public static bool HasSlot(string level) => HasSlot(level, PlayerPrefs.GetInt("oz_zod_exact", 0) == 1);

        /// <summary>ZWorld.ShuttleOn for a level start, the box given: level 16 = the box on EASY / MEDIUM, off on HARD; any other level: on.</summary>
        public static bool Rule(string level, GameDifficulty d, bool box) => level != Level || (d != GameDifficulty.Hard && box);

        /// <summary>ZGame at the level start: <see cref="Rule"/> with the saved box, and one log line on level 16.</summary>
        public static bool RuleAtStart(string level, GameDifficulty d)
        {
            bool box = On, on = Rule(level, d, box);
            if (level == Level)
                Debug.Log($"[OpenZ] shuttle at the start of {level} ({Name(d)}): " + (d == GameDifficulty.Hard ? "HARD, no shuttle (the box is not offered)" : box ? "the box is ON: the shuttle stands by your fort" : "the box is OFF: no shuttle")
                          + $" ({Pref} {(box ? 1 : 0)}; the testing panel drops shuttles as before)");
            return on;
        }

        /// <summary>K444 (DV134): ZWorld.L17StartShuttle for a level start, the box given: level 17 on EASY / MEDIUM with the box on (its
        /// shuttle parks by the red fort); else false. Level 17's EMPTY car-park shuttle is not the box's: it stands there on every difficulty.</summary>
        public static bool L17Start(string level, GameDifficulty d, bool box) => level == Level17 && d != GameDifficulty.Hard && box;

        /// <summary>ZGame at the level start: <see cref="L17Start"/> with the saved box, and one log line on level 17.</summary>
        public static bool L17StartAtStart(string level, GameDifficulty d)
        {
            bool box = On, on = L17Start(level, d, box);
            if (level == Level17)
                Debug.Log($"[OpenZ] shuttle at the start of {level} ({Name(d)}): " + (d == GameDifficulty.Hard ? "HARD, none by the fort (the box is not offered)" : box ? "the box is ON: the shuttle stands by your fort" : "the box is OFF: none by your fort")
                          + $"; the empty shuttle waits in the car park on every difficulty ({Pref} {(box ? 1 : 0)})");
            return on;
        }

        /// <summary>K347: the OPTIONS row SHUTTLE is not built any more (main menu and pause menu); the switch is this box.</summary>
        public static bool InOptions => false;

        static string Name(GameDifficulty d) => d == GameDifficulty.Easy ? "EASY" : d == GameDifficulty.Medium ? "MEDIUM" : "HARD";
    }
}
