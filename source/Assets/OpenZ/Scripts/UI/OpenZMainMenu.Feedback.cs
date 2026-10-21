using System.Collections.Generic;
using UnityEngine;

namespace OpenZ
{
    // ============================================================================================================================
    // The line "Share your ideas, report bugs,
    // request features" + a BUG REPORTS & SUGGESTIONS button at the end of the ROADMAP and CHANGES & UPDATES scroll views (the
    // same minimal big-type style), click = ui_click + OzFeedback.Open (Application.OpenURL, the system browser). A button's hot
    // area only lives while the whole button is inside its scroll view (like the credits links), so a button scrolled under the
    // header or the footer never takes a click.
    // ============================================================================================================================
    public partial class OpenZMainMenu
    {
        sealed class FeedbackHot { public RectTransform hot, view; public HudButton btn; public string from; }
        readonly List<FeedbackHot> feedbackHots = new List<FeedbackHot>();
        readonly List<FeedbackHot> discordHots = new List<FeedbackHot>();                                  // the OPENZ DISCORD button under each feedback button
        static readonly Vector3[] fbA = new Vector3[4], fbB = new Vector3[4];

        const string FeedbackLineRich = "<align=center><size=20><color=#F4F2EA>" + OzFeedback.Line + "</color></size></align>";

        /// <summary>The line + button at y in a scroll content (cw wide, view = its masked scroll view); returns the height used.</summary>
        float FeedbackBlock(RectTransform content, RectTransform view, float y, float cw, Side s, string from)
        {
            float y0 = y;
            var t = Hud.Label(content, "FeedbackLine", FeedbackLineRich, OzMenuLayout.CreditsBody, OzUi.Ink2, 0.01f, OzStyle.Plain).Body(40f, y, cw - 80f);
            float lh = Mathf.Max(10f, t.preferredHeight + 4f);
            t.rectTransform.sizeDelta = new Vector2(cw - 80f, lh);
            y += lh + 12f;
            const float bw = 380f, bh = 42f;
            var b = HudButton.Make(content, "FeedbackBtn", OzFeedback.Button + "  ↗", new Vector2(bw, bh), HudButtonKind.Small, 12, 0.14f);
            b.Rt.Place(new OzRect((cw - bw) * 0.5f, y, bw, bh));
            var hot = Hud.Node("FeedbackHot", content).Place(new OzRect((cw - bw) * 0.5f, y, bw, bh));
            string f = from;
            AddHot(hot, View.Main, () => OzFeedback.Open(f), null, b, "ui_click", s);
            feedbackHots.Add(new FeedbackHot { hot = hot, view = view, btn = b, from = from });
            y += bh + 12f;
            // the OpenZ Discord: the same button, the address from the one links file, opened outside the game
            var b2 = HudButton.Make(content, "DiscordBtn", OzLinks.DiscordButton + "  ↗", new Vector2(bw, bh), HudButtonKind.Small, 12, 0.14f);
            b2.Rt.Place(new OzRect((cw - bw) * 0.5f, y, bw, bh));
            var hot2 = Hud.Node("DiscordHot", content).Place(new OzRect((cw - bw) * 0.5f, y, bw, bh));
            AddHot(hot2, View.Main, () => OzLinks.Open(OzLinks.Discord, f), null, b2, "ui_click", s);
            discordHots.Add(new FeedbackHot { hot = hot2, view = view, btn = b2, from = from });
            y += bh + 8f;
            var t2 = Hud.Label(content, "DiscordLine", "<align=center><size=15><color=#8D9286>" + OzLinks.DiscordLine + "</color></size></align>", OzMenuLayout.CreditsBody, OzUi.Ink2, 0.01f, OzStyle.Plain).Body(40f, y, cw - 80f);
            float lh2 = Mathf.Max(10f, t2.preferredHeight + 4f);
            t2.rectTransform.sizeDelta = new Vector2(cw - 80f, lh2);
            y += lh2;
            return y - y0;
        }

        /// <summary>Every frame on the main view: a feedback button takes the mouse only while it is wholly inside its scroll view.</summary>
        void TickFeedback()
        {
            for (int i = 0; i < feedbackHots.Count + discordHots.Count; i++)
            {
                var fh = i < feedbackHots.Count ? feedbackHots[i] : discordHots[i - feedbackHots.Count];
                if (!fh.hot || !fh.view) continue;
                bool shown = fh.view.gameObject.activeInHierarchy;
                if (shown)
                {
                    fh.btn.Rt.GetWorldCorners(fbA); fh.view.GetWorldCorners(fbB);
                    shown = fbA[0].y >= fbB[0].y - 0.5f && fbA[1].y <= fbB[1].y + 0.5f;
                }
                fh.hot.gameObject.SetActiveFast(shown);
            }
        }

        /// <summary></summary>
        public int ProofFeedbackCount => feedbackHots.Count;
        public int ProofDiscordCount => discordHots.Count;
        public Vector2? ProofDiscordPoint(int n)
        {
            if (n < 0 || n >= discordHots.Count || !discordHots[n].hot || !discordHots[n].hot.gameObject.activeInHierarchy) return null;
            var c = new Vector3[4]; discordHots[n].hot.GetWorldCorners(c); return (Vector2)((c[0] + c[2]) * 0.5f);
        }
        public string ProofFeedbackFrom(int n) => n >= 0 && n < feedbackHots.Count ? feedbackHots[n].from : null;
        public Vector2? ProofFeedbackPoint(int n)
        {
            if (n < 0 || n >= feedbackHots.Count || !feedbackHots[n].hot || !feedbackHots[n].hot.gameObject.activeInHierarchy) return null;
            var c = new Vector3[4]; feedbackHots[n].hot.GetWorldCorners(c); return (Vector2)((c[0] + c[2]) * 0.5f);
        }
        public void ProofScrollRoadmapEnd() { if (rmContent && rmView) rmTarget = Mathf.Max(0f, rmContent.sizeDelta.y - rmView.rect.height); }
        public void ProofOpenChanges() { if (side != Side.Changes) OpenSide(Side.Changes); }
    }
}
