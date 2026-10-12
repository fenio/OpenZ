using System.Collections.Generic;
using UnityEngine;

namespace OpenZ
{
    // ============================================================================================================================
    // "Z COMMUNITY" section with clickable links: fan site + Discord, the ORIGINAL Z on Steam / GOG (not Z: Steel Soldiers), the
    // left out). Each link is a short underlined amber label with its site in muted grey; hover = ink + underline + ui_hover, click =
    // ui_click + Application.OpenURL (the default system browser, never an in-game view; the player keeps running: runInBackground
    // is on). A link's hot area only lives while the whole row is inside the scroll view, so a row scrolled under the header or the
    // footer never takes a click.
    // ============================================================================================================================
    public partial class OpenZMainMenu
    {
        static readonly (string group, string label, string site, string url)[] CommunityLinks =
        {
            ("FAN COMMUNITY", "ZZone · the Z fan site", "zzone.lewe.com", "https://zzone.lewe.com/"),
            ("FAN COMMUNITY", "ZZone Discord", "discord.gg", "https://discord.gg/WjFqg35PsE"),
            ("THE ORIGINAL Z", "Z on Steam", "store.steampowered.com", "https://store.steampowered.com/app/275530/Z/"),
            ("THE ORIGINAL Z", "Z: The Game on GOG", "gog.com", "https://www.gog.com/en/game/z"),
            ("OPEN-SOURCE REMAKES", "The Zod Engine", "zod.sourceforge.net", "https://zod.sourceforge.net/"),
            ("OPEN-SOURCE REMAKES", "Zod Engine downloads + source", "sourceforge.net", "https://sourceforge.net/projects/zod/"),
            ("OPEN-SOURCE REMAKES", "ZED Online", "sourceforge.net", "https://sourceforge.net/projects/zedonline/"),
            ("OPEN-SOURCE REMAKES", "Zod Engine, modern SDL3 build", "github.com", "https://github.com/fenio/zod"),
            ("WIKIPEDIA", "Z (video game)", "wikipedia.org", "https://en.wikipedia.org/wiki/Z_(video_game)"),
            ("WIKIPEDIA", "The Bitmap Brothers", "wikipedia.org", "https://en.wikipedia.org/wiki/The_Bitmap_Brothers"),
            ("WIKIPEDIA", "Rebellion", "wikipedia.org", "https://en.wikipedia.org/wiki/Rebellion_Developments"),
        };

        const string CommunityIntro =
            "<align=center><size=13><cspace=0.24em><color=#F2B84B>Z COMMUNITY</color></cspace></size>\n<size=8>\n</size>" +
            "<size=16><color=#C8CABF>The fans, the original game and the other open-source remakes. Links open in your web browser.</color></size></align>";

        sealed class CreditLink { public OzText text; public RectTransform hot; public float y, h; public string url, label, normal, over; }
        readonly List<CreditLink> creditLinks = new List<CreditLink>();
        /// <summary>Proof switch (session only): log the click instead of opening the browser.</summary>
        public static bool ProofNoOpen;
        public static string LastLinkOpened;
        public int ProofLinkCount => creditLinks.Count;

        const float LinkSize = 16f, LinkRowH = 30f, LinkGroupGap = 14f;

        /// <summary>The Z COMMUNITY block at y in the credits content (cw wide); returns its height.</summary>
        float CreditsCommunity(float y, float cw)
        {
            creditLinks.Clear();
            float y0 = y;
            y += CreditsBlock(CommunityIntro, y, 60f, cw - 120f, "CommIntro") + 18f;
            float colW = Mathf.Min(420f, (cw - 60f) * 0.5f), gap = 40f, x0 = (cw - (2f * colW + gap)) * 0.5f;
            float yl = LinkColumn(new[] { "FAN COMMUNITY", "THE ORIGINAL Z" }, x0, y, colW);
            float yr = LinkColumn(new[] { "OPEN-SOURCE REMAKES" }, x0 + colW + gap, y, colW);
            y = Mathf.Max(yl, yr) + 6f;
            // Wikipedia: one centred row of three short links
            Hud.Label(creditsContent, "CommWiki", "WIKIPEDIA", 10, OzUi.Muted, 0.24f, OzStyle.Plain).At(cw * 0.5f, y, OzH.C, false, 300);
            y += 22f;
            var wiki = new List<(string label, string site, string url)>();
            foreach (var l in CommunityLinks) if (l.group == "WIKIPEDIA") wiki.Add((l.label, l.site, l.url));
            float sep = 36f, total = -sep; var widths = new List<float>();
            foreach (var w in wiki)
            {
                var probe = Hud.Label(creditsContent, "Probe", LinkRich(w.label, null, false), LinkSize, OzUi.Ink, 0.01f, OzStyle.Plain);
                float pw = probe.preferredWidth; widths.Add(pw); total += pw + sep; Destroy(probe.gameObject);
            }
            float x = (cw - total) * 0.5f;
            for (int i = 0; i < wiki.Count; i++)
            {
                AddLink(wiki[i].label, null, wiki[i].url, x, y, widths[i] + 8f);
                x += widths[i] + sep;
            }
            y += LinkRowH;
            return y - y0;
        }

        float LinkColumn(string[] groups, float x, float y, float w)
        {
            for (int g = 0; g < groups.Length; g++)
            {
                if (g > 0) y += LinkGroupGap;
                Hud.Label(creditsContent, "CommGroup" + groups[g], groups[g], 10, OzUi.Muted, 0.24f, OzStyle.Plain).At(x, y, OzH.L, false, w);
                y += 22f;
                foreach (var l in CommunityLinks) if (l.group == groups[g]) { AddLink(l.label, l.site, l.url, x, y, w); y += LinkRowH; }
            }
            return y;
        }

        static string LinkRich(string label, string site, bool hover) =>
            (hover ? "<color=#F4F2EA><u>" : "<color=#F2B84B><u>") + label + "</u></color>" + (hover ? "<color=#F2B84B>  ↗</color>" : "<color=#F2B84B00>  ↗</color>")
            + (string.IsNullOrEmpty(site) ? "" : "<size=13><color=#8D9286>   " + site + "</color></size>");

        void AddLink(string label, string site, string url, float x, float y, float w)
        {
            var link = new CreditLink { url = url, label = label, y = y - 6f, h = LinkSize + 12f, normal = LinkRich(label, site, false), over = LinkRich(label, site, true) };
            link.text = Hud.Label(creditsContent, "Link_" + label, link.normal, LinkSize, OzUi.Ink, 0.01f, OzStyle.Plain).At(x, y, OzH.L, false, w);
            var probe = Hud.Label(creditsContent, "Probe", LinkRich(label, null, false), LinkSize, OzUi.Ink, 0.01f, OzStyle.Plain);
            float hw = Mathf.Min(w, probe.preferredWidth + 10f); Destroy(probe.gameObject);
            link.hot = Hud.Node("LinkHot_" + label, creditsContent).Place(new OzRect(x - 6f, link.y, hw + 6f, link.h));
            var l = link;
            AddHot(link.hot, View.Main, () => OpenLink(l), h => l.text.Set(h ? l.over : l.normal), null, "ui_click", Side.Credits);
            creditLinks.Add(link);
        }

        void OpenLink(CreditLink l)
        {
            LastLinkOpened = l.url;
            if (ProofNoOpen) { Debug.Log($"[OpenZ Menu] credits link (proof, not opened): {l.label} -> {l.url}"); return; }
            Debug.Log($"[OpenZ Menu] credits link: {l.label} -> {l.url} (system browser)");
            Application.OpenURL(l.url);
        }

        /// <summary>Called from TickCredits: a link takes the mouse only while its whole row is inside the scroll view.</summary>
        void TickCreditLinks()
        {
            if (creditLinks.Count == 0 || !creditsView) return;
            float vh = creditsView.rect.height;
            foreach (var l in creditLinks)
            {
                if (!l.hot) continue;
                float top = l.y - creditsScroll;
                l.hot.gameObject.SetActiveFast(top >= -2f && top + l.h <= vh + 2f);
            }
        }

        /// <summary>Proof: screen centre of the n-th link's hot area (null when it is out of view).</summary>
        public Vector2? ProofLinkPoint(int n)
        {
            if (n < 0 || n >= creditLinks.Count || !creditLinks[n].hot || !creditLinks[n].hot.gameObject.activeInHierarchy) return null;
            var c = new Vector3[4]; creditLinks[n].hot.GetWorldCorners(c); return (Vector2)((c[0] + c[2]) * 0.5f);
        }
        public string ProofLinkUrl(int n) => n >= 0 && n < creditLinks.Count ? creditLinks[n].url : null;
        public void ProofScrollCredits(float to) { creditsScrollTarget = to; }
        public void ProofOpenCredits() { if (side != Side.Credits) OpenSide(Side.Credits); }
        public bool ProofLinkHovered(int n) => n >= 0 && n < creditLinks.Count && creditLinks[n].text.text == creditLinks[n].over;
        public string ProofCreditsFoot { get { var t = creditsContent ? creditsContent.Find("FootBlock") : null; var tmp = t ? t.GetComponent<TMPro.TMP_Text>() : null; return tmp ? tmp.text : null; } }
        public float ProofCreditsLinkY(int n) => n >= 0 && n < creditLinks.Count ? creditLinks[n].y : -1f;
    }
}
