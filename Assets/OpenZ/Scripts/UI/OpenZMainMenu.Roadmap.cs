using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace OpenZ
{
    // ============================================================================================================================
    // The main menu's ROADMAP screen, the item directly above CREDITS: the same panel as CREDITS (same place, plate, title row,
    // scroll view, big centred type, amber eyebrows / bullets / rules) with the 7 roadmap items as a bullet list. Wheel / arrows / PgUp PgDn scroll,
    // ESC closes (Keys() in OpenZMainMenu.cs). Hooks in OpenZMainMenu.cs: Side.Roadmap, AddItem, BuildMain -> BuildRoadmap,
    // OpenSide reset, SideRt / SideCg, SidePanels, wheel + Keys scroll, Update -> TickRoadmap.
    // ============================================================================================================================
    public partial class OpenZMainMenu
    {
        RectTransform roadmapRt, rmContent, rmView, rmTrack; CanvasGroup roadmapCg; Image rmThumb; float rmScroll, rmTarget;

        const string RoadmapHead =
            "<align=center><size=60><b><cspace=0.06em><color=#F4F2EA>ROADMAP</color></cspace></b></size>\n" +
            "<size=4>\n</size><size=13><cspace=0.24em><color=#F2B84B>30TH ANNIVERSARY  ·  1996 – 2026</color></cspace></size></align>";
        const string RoadmapIntro =
            "<align=center><size=20><color=#F4F2EA>OpenZ is a non-commercial, faithful fan remake celebrating the 30th anniversary of Z.</color></size>\n" +
            "<size=10>\n</size><size=18><color=#C8CABF>It is free: no sales, no donations. The code is GPL-3.0 and all other assets are CC BY-NC-SA.</color></size>\n" +
            "<size=10>\n</size><size=18><color=#C8CABF>It is a self-funded solo fan project made for the love of the original, so progress is slower than we would like.</color></size></align>";
        const string RoadmapListHead = "<align=center><size=13><cspace=0.24em><color=#F2B84B>LONG-TERM ROADMAP (2-3 YEARS)</color></cspace></size></align>";
        static readonly string[] RoadmapItems =
        {
            "At least one map in each of the 5 biomes",
            "1 vs 1 online multiplayer",
            "More restored classic maps from every biome",
            "Original new maps",
            "New original vehicles and robots",
            "More cut scenes",
            "The remaining classic maps restored",
        };
        const string RoadmapFoot =
            "<align=center><size=20><color=#F4F2EA>Your support, feedback and bug reports help the community remake grow - thank you!</color></size></align>";

        static string RoadmapBullets()
        {
            var sb = new StringBuilder();
            foreach (var it in RoadmapItems)
                sb.Append("<size=19><color=#F2B84B>•</color><indent=1.1em><color=#F4F2EA>").Append(it).Append("</color></indent></size>\n<size=7>\n</size>");
            return sb.ToString().TrimEnd('\n');
        }

        float RoadmapBlock(string rich, float y, float x, float w, string name)
        {
            var t = Hud.Label(rmContent, name, rich, OzMenuLayout.CreditsBody, OzUi.Ink2, 0.01f, OzStyle.Plain).Body(x, y, w);
            float h = Mathf.Max(10f, t.preferredHeight + 4f);
            t.rectTransform.sizeDelta = new Vector2(w, h);
            return h;
        }

        void RoadmapRule(float y, float cw)
        {
            var r = Hud.Img(rmContent, "Rule", OzUi.A(OzUi.Amber, 0.55f)).rectTransform;
            r.anchorMin = r.anchorMax = new Vector2(0, 1); r.pivot = new Vector2(0.5f, 1);
            r.anchoredPosition = new Vector2(cw * 0.5f, -y); r.sizeDelta = new Vector2(96f, 2f);
        }

        void BuildRoadmap()
        {
            float W = Mathf.Min(OzMenuLayout.CreditsW, CW - OzMenuLayout.CreditsX - 60f), H = OzMenuLayout.CreditsH, pad = OzMenuLayout.CreditsPad, head = OzMenuLayout.CreditsHeader;
            roadmapRt = Hud.Node("Roadmap", mainRoot).Place(new OzRect(OzMenuLayout.CreditsX, OzMenuLayout.CreditsY, W, H));
            roadmapRt.gameObject.SetActive(true);                                                            // built while active: TMP measures wrapped text right
            Hud.Plate(roadmapRt, "lg", head, OzUi.Amber, null, true);
            Hud.Label(roadmapRt, "Title", "ROADMAP", 16, OzUi.Ink, 0.16f).At(pad, 15);
            Hud.Label(roadmapRt, "Sub", "FREE · NON-COMMERCIAL · FAN REMAKE", 9, OzUi.Muted, 0.2f, OzStyle.Plain).At(W - pad, 18, OzH.R, false, 420);
            rmView = Hud.Node("View", roadmapRt).Place(new OzRect(pad, head + 16, W - 2 * pad - 14, H - head - 16 - 40));
            rmView.gameObject.AddComponent<RectMask2D>();
            rmContent = Hud.Node("Content", rmView);
            rmContent.anchorMin = new Vector2(0, 1); rmContent.anchorMax = new Vector2(1, 1); rmContent.pivot = new Vector2(0.5f, 1);
            rmContent.offsetMin = new Vector2(0, -10); rmContent.offsetMax = Vector2.zero;
            float cw = W - 2 * pad - 14, y = 10f;                                                         // tightened: the whole text fits the 680 u view (was 727 u, scrolled)
            y += RoadmapBlock(RoadmapHead, y, 0, cw, "Head") + 12f;
            RoadmapRule(y, cw); y += 2f + 16f;
            y += RoadmapBlock(RoadmapIntro, y, 50f, cw - 100f, "Intro") + 18f;
            y += RoadmapBlock(RoadmapListHead, y, 0, cw, "ListHead") + 10f;
            float lw = Mathf.Min(560f, cw - 120f);
            y += RoadmapBlock(RoadmapBullets(), y, (cw - lw) * 0.5f, lw, "Items") + 14f;
            RoadmapRule(y, cw); y += 2f + 14f;
            y += RoadmapBlock(RoadmapFoot, y, 40f, cw - 80f, "Foot") + 18f;
            y += FeedbackBlock(rmContent, rmView, y, cw, Side.Roadmap, "roadmap") + 14f;
            rmContent.sizeDelta = new Vector2(0, y);
            rmTrack = Hud.Node("Track", roadmapRt).Place(new OzRect(W - pad + 6, head + 16, 4, H - head - 16 - 40));
            Hud.Img(rmTrack, "Bg", OzUi.A(OzUi.GM950, 0.9f)).rectTransform.Fill();
            rmThumb = Hud.Img(rmTrack, "Thumb", OzUi.Amber);
            rmThumb.rectTransform.anchorMin = new Vector2(0, 1); rmThumb.rectTransform.anchorMax = new Vector2(1, 1); rmThumb.rectTransform.pivot = new Vector2(0.5f, 1);
            Hud.Label(roadmapRt, "Keys", "WHEEL / ↑ ↓ SCROLL · ESC CLOSE", 9, OzUi.Dim, 0.2f, OzStyle.Plain).At(pad, H - 24);
            roadmapRt.pivot = new Vector2(0f, 0.5f); roadmapRt.anchoredPosition += new Vector2(0, -H * 0.5f);
            roadmapCg = roadmapRt.gameObject.AddComponent<CanvasGroup>();
            roadmapRt.gameObject.SetActive(false);
            rmScroll = rmTarget = 0f;
            float view = H - head - 16 - 40;
            Debug.Log($"[OpenZ Menu] ROADMAP built: {RoadmapItems.Length} items, content {y:0} u, view {view:0} u ({(y <= view ? "fits, no scroll" : "scrolls")}), panel {W:0} x {H:0} u on the {CW:0} x 1080 u canvas ({Screen.width}x{Screen.height})");
        }

        void ScrollRoadmap(float dy)
        {
            float max = Mathf.Max(0f, rmContent.sizeDelta.y - rmView.rect.height);
            rmTarget = Mathf.Clamp(rmTarget + dy, 0f, max);
        }

        void TickRoadmap(float dt)
        {
            if (!roadmapRt || !roadmapRt.gameObject.activeSelf) return;
            float max = Mathf.Max(0f, rmContent.sizeDelta.y - rmView.rect.height);
            rmTarget = Mathf.Clamp(rmTarget, 0f, max);
            rmScroll = OzMotion.Reduced ? rmTarget : Mathf.Lerp(rmScroll, rmTarget, 1f - Mathf.Exp(-dt * 14f));
            rmContent.anchoredPosition = new Vector2(0, rmScroll);
            float th = rmTrack.rect.height, vis = max <= 0 ? 1f : rmView.rect.height / rmContent.sizeDelta.y;
            rmThumb.rectTransform.sizeDelta = new Vector2(0, Mathf.Max(24f, th * vis));
            rmThumb.rectTransform.anchoredPosition = new Vector2(0, max <= 0 ? 0 : -(th - rmThumb.rectTransform.sizeDelta.y) * rmScroll / max);
            rmTrack.gameObject.SetActiveFast(max > 0);
        }

        /// <summary></summary>
        public void ProofOpenRoadmap() { if (side != Side.Roadmap) OpenSide(Side.Roadmap); }
        public string ProofRoadmap => rmContent && rmView ? $"content {rmContent.sizeDelta.y:0} u, view {rmView.rect.height:0} u, panel {(roadmapRt ? roadmapRt.rect.width : 0):0} u wide" : "not built";
    }
}
