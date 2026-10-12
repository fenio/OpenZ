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
            y += bh;
            return y - y0;
        }

        /// <summary>Every frame on the main view: a feedback button takes the mouse only while it is wholly inside its scroll view.</summary>
        void TickFeedback()
        {
            for (int i = 0; i < feedbackHots.Count; i++)
            {
                var fh = feedbackHots[i];
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
