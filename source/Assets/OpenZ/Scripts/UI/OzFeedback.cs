using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// The one link: main menu entry
    /// BUG REPORTS &amp; SUGGESTIONS, the line + button at the end of ROADMAP and of CHANGES &amp; UPDATES, and the pause menu's
    /// REPORT A BUG. Application.OpenURL = the player's default system browser, never an in-game view.
    /// </summary>
    public static class OzFeedback
    {
        public static string Url => OzLinks.Get(OzLinks.FeedbackForm);                                    // the one links file holds the address
        public const string Line = "Share your ideas, report bugs, request features";
        public const string Button = "BUG REPORTS & SUGGESTIONS";

        /// <summary>Proof switch (session only): log the exact URL instead of opening the browser.</summary>
        public static bool ProofNoOpen;
        public static string LastOpened, LastFrom;
        public static int Opened;

        public static void Open(string from)
        {
            LastOpened = Url; LastFrom = from; Opened++;
            if (ProofNoOpen) { Debug.Log($"[OpenZ Feedback] OpenURL (proof: browser not opened) from {from}: {Url}"); return; }
            Debug.Log($"[OpenZ Feedback] OpenURL from {from}: {Url}");
            Application.OpenURL(Url);
        }
    }
}
