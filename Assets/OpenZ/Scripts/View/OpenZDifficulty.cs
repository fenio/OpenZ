using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// 1 = Easy; 2 = Medium is new.</summary>
    public enum GameDifficulty { Hard = 0, Easy = 1, Medium = 2 }

    /// <summary>
    /// Difficulty choice shared by the menus: PlayerPrefs "oz_difficulty". ZGame reads it when a level starts; setting it during
    /// a game also re-tunes the running CPU (human vs CPU only; bot vs bot stays Zod).
    ///   EASY   = the Easy CPU handicap (ZBotDifficulty.Easy) + every unit unlocked in the player's factories and fort
    ///            (ZWorld.TechBonus +5, L02 too) + the L02 head start vehicles, always.
    ///   MEDIUM = the Zod CPU + the player's factories two tech levels up + the L02 head start (menu toggle).
    ///   HARD   = the Zod CPU, Zod build lists, map start units + one missile cannon on each fort's door tower (ZWorld.FortDoorGunDeviation);
    ///            the deliberate OpenZ rule changes (DV rows) apply on every difficulty.
    /// All three are OpenZ deviations (parity report, Deviations: DV4; Hard also DV73).
    /// </summary>
    public static class OpenZDifficulty
    {
        public const string Key = "oz_difficulty";

        public static GameDifficulty Level
        {
            get { int v = PlayerPrefs.GetInt(Key, (int)GameDifficulty.Medium); /* menu_exe_v1: MEDIUM is the default */ return v == 1 ? GameDifficulty.Easy : v == 2 ? GameDifficulty.Medium : GameDifficulty.Hard; }
            set { PlayerPrefs.SetInt(Key, (int)value); PlayerPrefs.Save(); ApplyLive(value); }
        }

        /// <summary>The CPU handicap of a difficulty: only Easy softens the bot.</summary>
        public static ZBotDifficulty Bot(GameDifficulty d) => d == GameDifficulty.Easy ? ZBotDifficulty.Easy : ZBotDifficulty.Normal;
        /// <summary>Kept for older callers: the CPU handicap of the stored difficulty.</summary>
        public static ZBotDifficulty Current => Bot(Level);

        /// <summary>Extra build-list levels for the human player (ZWorld.TechBonus).</summary>
        public static int TechBonus(GameDifficulty d) => d == GameDifficulty.Easy ? 5 : d == GameDifficulty.Medium ? 2 : 0;

        /// <summary>Does the human get the L02 head start? Easy always, Medium when the menu toggle is on, Hard never.</summary>
        /// <summary>
        /// default OFF) on every difficulty and both maps: L02 2 light tanks, 2 jeeps, 1 mech; L17 1 MLRS, 1 heavy tank.</summary>
        public const string HeadStartPref = "oz_headstart";
        public static bool HeadStartToggle { get => PlayerPrefs.GetInt(HeadStartPref, 0) == 1; set { PlayerPrefs.SetInt(HeadStartPref, value ? 1 : 0); PlayerPrefs.Save(); } }
        public static bool HeadStart(GameDifficulty d, bool toggle) => toggle;
        public static int HeadStartCount(string level) => level == "L02" ? ZGame.HeadStartTypes.Length : level == "L17" ? ZGame.EasyL17Types.Length : 0;
        public static string HeadStartHint(string level) => level == "L02" ? "+2 LIGHT TANKS · 2 JEEPS · 1 MECH, CREWED, AT YOUR FORT" : level == "L17" ? (ZGame.ParkedStartOn("L17") ? "+1 MLRS · 1 HEAVY TANK PARKED EMPTY AT YOUR FORT" : "+1 MLRS · 1 HEAVY TANK, CREWED, AT YOUR FORT") : "";

        public static string Label(GameDifficulty d) => d == GameDifficulty.Easy ? "EASY" : d == GameDifficulty.Medium ? "MEDIUM" : "HARD";

        /// <summary>One-line explanation for the level select / options.</summary>
        public static string Hint(GameDifficulty d) => d == GameDifficulty.Easy
            ? "EVERY UNIT UNLOCKED · GENTLE CPU"
            : d == GameDifficulty.Medium ? "FACTORIES 2 TECH LEVELS UP · ZOD CPU" : "Z'S BUILD LISTS · FORT MISSILE CANNONS";

        public static GameDifficulty Next(GameDifficulty d) => d == GameDifficulty.Easy ? GameDifficulty.Medium : d == GameDifficulty.Medium ? GameDifficulty.Hard : GameDifficulty.Easy;

        static void ApplyLive(GameDifficulty d)
        {
            var g = ZGame.I;
            if (g == null || g.World == null) return;
            g.gameDifficulty = d; g.difficulty = Bot(d);
            foreach (var b in g.World.Bots) b.Difficulty = g.BotDifficulty(b.Team);
            g.ApplyTechBonus();
        }
    }
}
