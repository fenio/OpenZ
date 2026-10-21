using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// Options, GAME tab: FRIENDLY UNITS OVERLAP, a cycle row NO / YES (saved as oz_overlap, default NO). YES = the player's and the computer's moving units pass
    /// through units of their own team, as in the original game (fewer detours in a crowd); they still go round buildings, rocks, trees,
    /// water and every obstacle, and keep apart from enemies. NO = they keep apart (the game as before). Applied at once and at every
    /// match start (View/ZGame.cs); never in 'Zod exact'. The rule: Sim/ZPathing.Overlap.cs.
    /// </summary>
    public static class OzOverlapOption
    {
        public const string Label = "FRIENDLY UNITS OVERLAP", OverlapPref = "oz_overlap";
        public const string Tip = "FRIENDLY UNITS OVERLAP|YES: your moving units pass through each other, as in the original game. Fewer detours in a crowd; buildings, rocks and enemies still block them. NO (default): they keep apart.|";
        /// <summary>The row's value: NO (default) or YES (a cycle row, like RESOLUTION).</summary>
        public static string Name => On ? "YES" : "NO";
        /// <summary>The cycle row's step: two values, either arrow flips it.</summary>
        public static void Step(int dir) => On = !On;

        public static bool On
        {
            get => PlayerPrefs.GetInt(OverlapPref, 0) == 1;
            set
            {
                PlayerPrefs.SetInt(OverlapPref, value ? 1 : 0); PlayerPrefs.Save(); Apply();
                Debug.Log($"[OpenZ Options] friendly units overlap: {(value ? "YES" : "NO")} ({OverlapPref} {(value ? 1 : 0)}, saved, live)");
            }
        }

        /// <summary>The rule's switch from the saved choice (never in 'Zod exact').</summary>
        public static void Apply() => OpenZ.Sim.ZWorld.FriendsOverlap = PlayerPrefs.GetInt("oz_zod_exact", 0) == 0 && On;
    }
}
