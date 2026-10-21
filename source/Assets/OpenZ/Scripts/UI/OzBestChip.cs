using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// Level card (battlefield screen): the best result on that level at the difficulty chosen right now, as a chip in the
    /// lower-left corner of the card's picture: "BEST · MEDIUM · 19 901 · MAJOR GENERAL", or "NO RECORD YET · MEDIUM". On a card too
    /// narrow for that line the difficulty is left out ("BEST · 19 901 · MAJOR GENERAL"): the buttons under the cards show it.
    ///
    /// The chip is a label only. The level's whole table (every entry with rank, time, units killed and lost) is in the
    /// HIGH SCORES popup of the same screen (OpenZMainMenu.HighScores.cs), so the chip carries no table of its own and takes no
    /// pointer: a click on it is a click on its card.
    ///
    /// It follows the difficulty buttons and a table change by itself (looked up four times a second), so the menu code only
    /// adds it: OzBestChip.Add(card, level id, picture height).
    /// </summary>
    public sealed class OzBestChip : MonoBehaviour
    {
        public const float X = 24f, FromPictureBottom = 38f, H = 22f, TextPad = 18f;
        string level; OzText text; RectTransform rt;
        int shownVersion = -1; int shownDiff = -1; float next;

        /// <summary>Checks: what the chip says.</summary>
        public string Shown => text != null ? text.text : "";

        public static OzBestChip Add(RectTransform card, string levelId, float pictureHeight)
        {
            var c = Hud.Chip(card, "BestChip", new OzRect(X, pictureHeight - FromPictureBottom, 200f, H), "", OzUi.GM950, OzUi.Amber, 10, 0.16f, OzUi.A(OzUi.Amber, 0.6f));
            var root = c.bg.transform.parent.GetComponent<RectTransform>();
            var chip = root.gameObject.AddComponent<OzBestChip>();
            chip.level = levelId; chip.text = c.text; chip.rt = root;
            chip.Refresh();
            return chip;
        }

        void Update()
        {
            float now = Time.unscaledTime;
            if (now >= next) { next = now + 0.25f; Refresh(); }
        }

        void Refresh()
        {
            if (text == null || !rt) return;
            var d = OpenZDifficulty.Level;
            if ((int)d == shownDiff && OzHighScores.Version == shownVersion) return;
            shownDiff = (int)d; shownVersion = OzHighScores.Version;
            var e = OzHighScores.Best(level, d);
            string diff = OpenZDifficulty.Label(d);
            if (e != null)
            {
                string result = OzHighScores.Group(e.score) + " · " + OzHighScores.Rank(e.score);
                text.Set(OzText.Tab("BEST · " + diff + " · " + result));
                var card = rt.parent as RectTransform;
                float room = card ? card.rect.width - 2f * X : 0f;                                           // the chip stays on its card
                if (room > 0f && text.preferredWidth + TextPad > room) text.Set(OzText.Tab("BEST · " + result));
            }
            else text.Set("NO RECORD YET · " + diff);
            text.color = e != null ? (Color)OzUi.Amber : (Color)OzUi.Ink2;
            rt.sizeDelta = new Vector2(text.preferredWidth + TextPad, H);
        }
    }
}
