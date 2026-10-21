using System;
using System.Text;

namespace OpenZ
{
    // ============================================================================================================================
    // The main menu's flyby rotation: ONLY the levels on this list play behind the main menu. A level joins it only when its menu
    // flyby is approved for the main menu; a level card's preview in the level select is NOT this list (every card keeps its own
    // preview, its still or its clips). Level 16's picture (the temple in its pool) is the Jungle card's preview and is not in
    // the rotation.
    // Hooks in OpenZMainMenu.cs: NextFlyby and CanAlternate count only listed levels, FirstFlyby falls back to a listed level, and
    // GoMain (back from the level select) leaves a level that is not on the list at once.
    // ============================================================================================================================
    public partial class OpenZMainMenu
    {
        /// <summary>The main menu's flyby rotation, by level id, in menu order.</summary>
        public static readonly string[] MenuFlybyLevels = { "L02", "L17", "L16" };

        /// <summary>Level index i plays in the main menu's flyby rotation.</summary>
        bool InMenuFlybys(int i) => levels != null && i >= 0 && i < levels.Length && Array.IndexOf(MenuFlybyLevels, levels[i].Id) >= 0;

        /// <summary>i itself when it is on the list, else the first listed level that has something to show (i when none has).</summary>
        int MenuFlybyOr(int i)
        {
            if (InMenuFlybys(i)) return i;
            if (levels != null) for (int k = 0; k < levels.Length; k++) if (InMenuFlybys(k) && previewAny(k)) return k;
            return i;
        }

        // ---------------------------------------------------------------- proof hooks (Play only)
        /// <summary>The level ids the main menu would rotate through now, from the first one, " > " between them (NextFlyby's own walk).</summary>
        public string ProofMenuRotation
        {
            get
            {
                if (levels == null || previews == null) return "";
                var sb = new StringBuilder(); int start = FirstFlyby, at = start;
                for (int n = 0; n < levels.Length; n++)
                {
                    if (sb.Length > 0) sb.Append(" > ");
                    sb.Append(levels[at].Id);
                    int next = NextFlyby(at); if (next == start || next == at) break; at = next;
                }
                return sb.ToString();
            }
        }
        /// <summary>The level shown behind the menu now (-1 = none yet) and its id.</summary>
        public string ProofBackgroundLevel => bgLevel >= 0 && levels != null && bgLevel < levels.Length ? levels[bgLevel].Id : "";
        /// <summary>The levels that have a card preview (a still or a clip), " | " between them.</summary>
        public string ProofCardPreviews
        {
            get
            {
                if (levels == null || previews == null) return "";
                var sb = new StringBuilder();
                for (int i = 0; i < levels.Length; i++) if (previewAny(i)) { if (sb.Length > 0) sb.Append(" | "); sb.Append(levels[i].Id); }
                return sb.ToString();
            }
        }
        /// <summary>Back to the main view as the BACK key does (no-op outside the level select).</summary>
        public void ProofGoMain() => GoMain();
        /// <summary>Into the level select with card i focused (its preview behind the cards), as PLAY and a hover do.</summary>
        public void ProofGoSelect(int card) { GoSelect(); FocusCard(card, true, true); }
        /// <summary>The index of a level id in the menu (-1 when it has none).</summary>
        public int ProofLevelIndex(string id) => levels == null ? -1 : Array.FindIndex(levels, l => l.Id == id);
    }
}
