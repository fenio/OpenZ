using System.Collections.Generic;
using OpenZ.Sim;
using UnityEngine;
using UnityEngine.UI;

namespace OpenZ
{
    /// <summary>
    /// k252: the build menu's buttons (numbers in OzBuildMenuLayout). The close X 30 % bigger. The bottom row: STOP, CLEAR and a wide
    /// green SET RALLY with a white map pin left of the words, the row's most prominent button (its function, tooltip and sounds
    /// unchanged; armed it reads CANCEL with an amber keyline, as before). PLACE leaves the bottom row: a tile at the end of the
    /// CANNONS row with the icon of the gun that waits, the word PLACE and an amber glow; greyed, without the glow, while nothing
    /// waits (its function, tooltip, key and sounds unchanged; it no longer blinks as a filled button). Every other element of the
    /// menu stays where it was. The ready guns also float by the building's badge (OzReadyGuns).
    /// </summary>
    public partial class OpenZProductionWindow
    {
        UnitIcon placeIcon; Image placeEdge, rallyFill, rallyEdge, rallyPin; string rallyShown;
        readonly Image[] placeGlow = new Image[3];
        static readonly float[] GlowPad = { 2f, 4.5f, 7.5f }, GlowAlpha = { 0.34f, 0.17f, 0.07f };
        static readonly Color RallyFill = new Color32(38, 78, 34, 255), RallyFillHover = new Color32(52, 104, 45, 255), RallyFillDown = new Color32(28, 58, 25, 255);
        static readonly Color RallyEdgeHover = new Color32(170, 240, 130, 255);

        /// <summary>Build: once, after the buttons exist (STOP, PLACE, SET RALLY, CLEAR were made in the bottom row's four slots).</summary>
        void DressButtons()
        {
            // the close X: 30 % bigger, same square button, same place (centre kept)
            var cl = OzBuildMenuLayout.Close;
            bClose.Rt.Place(cl); Rehatch(bClose, cl.Size);
            var x = bClose.Content.Find("X") as RectTransform;
            if (x) x.Place(new OzRect(OzBuildMenuLayout.CloseIconX, OzBuildMenuLayout.CloseIconY, OzBuildMenuLayout.CloseIconSize, OzBuildMenuLayout.CloseIconSize));

            // the bottom row: STOP, CLEAR, then SET RALLY over the last two slots
            bStop.Rt.Place(OzBuildMenuLayout.Stop);
            bClear.Rt.Place(OzBuildMenuLayout.Clear);
            var rr = OzBuildMenuLayout.Rally;
            bRally.Rt.Place(rr); Rehatch(bRally, rr.Size);
            rallyFill = Layer(bRally, "GoFill", "Buttons/btn_s_mask", RallyFill, OzBuildMenuLayout.RallyRim);   // the dark green face ...
            rallyEdge = Layer(bRally, "GoEdge", "Buttons/btn_s_mask", OzUi.Go, 0f);                             // ... inside a green rim (made after: drawn under it)
            float ps = OzBuildMenuLayout.RallyPinSize;
            rallyPin = Hud.Icon(bRally.Content, "Pin", "pin", 0f, (rr.h - ps) * 0.5f, ps, OzUi.Ink);
            if (bRally.Label != null) bRally.Label.FontSize = OzBuildMenuLayout.RallyFont;
            PlaceRallyWords("SET RALLY");

            // PLACE: from the bottom row into the tile grid (its column and row: PlaceTileAt, from RebuildTiles)
            var size = new Vector2(OzBuildMenuLayout.PlaceW, OzLayout.ProdTileH);
            bPlace.Rt.SetParent(body, false);
            bPlace.Rt.Place(new OzRect(OzBuildMenuLayout.PlaceX(0), OzLayout.ProdTileY, size.x, size.y)); Rehatch(bPlace, size);
            for (int g = 0; g < placeGlow.Length; g++)
            {   // the amber glow: the button's silhouette, three soft steps outward, under everything of the tile
                var im = Hud.Img(bPlace.Rt, "Glow" + g, Color.clear);
                var s = OzKit.GetSprite("Buttons/btn_s_mask"); if (s != null) { im.sprite = s; im.type = Image.Type.Sliced; }
                float p = GlowPad[g]; im.rectTransform.Fill(-p, -p, -p, -p);
                im.transform.SetSiblingIndex(0);
                placeGlow[g] = im;
            }
            var es = OzKit.GetSprite("Buttons/btn_s_edge");
            if (es != null) placeEdge = Layer(bPlace, "Edge", "Buttons/btn_s_edge", OzUi.Amber, 0f);
            placeIcon = new UnitIcon(bPlace.Content, "Icon", new Vector2(OzBuildMenuLayout.PlaceIconSize, OzBuildMenuLayout.PlaceIconSize), false, 0f);
            placeIcon.Rt.Place(new OzRect((size.x - OzBuildMenuLayout.PlaceIconSize) * 0.5f, OzBuildMenuLayout.PlaceIconY, OzBuildMenuLayout.PlaceIconSize, OzBuildMenuLayout.PlaceIconSize));
            placeIcon.SetGlyph("cannon");
            bPlace.Icon = placeIcon;
            bPlace.LabelColor = OzUi.Amber;
            if (bPlace.Label != null) bPlace.Label.At(size.x * 0.5f, OzBuildMenuLayout.PlaceWordMid, OzH.C, true, size.x - 6f);
            bPlace.Style = HudButtonStyle.Normal; bPlace.Alert = false;
            bPlace.Refresh();
        }

        /// <summary>RebuildTiles: the PLACE tile right after the last gun of the CANNONS row (OzBuildMenuLayout.PlaceSlot). Returns the
        /// menu's row count with it (the same as the tiles' rows for every build list of the game).</summary>
        int PlaceTileAt(List<string>[] perClass, int row)
        {
            int withPlace = OzBuildMenuLayout.PlaceSlot(perClass[0].Count, perClass[1].Count, perClass[2].Count, out int col, out int r);
            bPlace.Rt.Place(new OzRect(OzBuildMenuLayout.PlaceX(col), OzLayout.ProdTileY + OzLayout.ProdRowH * r, OzBuildMenuLayout.PlaceW, OzLayout.ProdTileH));
            return Mathf.Max(row, withPlace);
        }

        /// <summary>Refresh, every frame the menu is open: the PLACE tile (gun icon, glow) and the SET RALLY face.</summary>
        void RefreshPlaceTile(ZObject b)
        {
            bool ready = b.StoredCannons.Count > 0;
            if (bPlace.Style != HudButtonStyle.Normal || bPlace.Alert) { bPlace.Style = HudButtonStyle.Normal; bPlace.Alert = false; bPlace.Refresh(); }
            if (ready) placeIcon.Set(b.StoredCannons[0], b.Owner); else placeIcon.SetGlyph("cannon");
            float pulse = OzMotion.Reduced ? 1f : 0.78f + 0.22f * Mathf.Sin(OzMotion.Now * (2f * Mathf.PI / 1.6f));
            for (int g = 0; g < placeGlow.Length; g++)
            {
                var im = placeGlow[g]; if (!im) continue;
                if (im.enabled != ready) im.enabled = ready;
                if (ready) { var c = OzUi.A(OzUi.Amber, GlowAlpha[g] * pulse * (bPlace.Hovered ? 1.35f : 1f)); if (im.color != c) im.color = c; }
            }
            if (placeEdge && placeEdge.enabled != ready) placeEdge.enabled = ready;

            // SET RALLY: green face (lighter under the pointer, darker pressed), green rim (amber while the tool is armed)
            if (rallyFill)
            {
                var f = bRally.Pressed ? RallyFillDown : bRally.Hovered ? RallyFillHover : RallyFill;
                if (rallyFill.color != f) rallyFill.color = f;
            }
            if (rallyEdge)
            {
                var e = bRally.Selected ? (Color)OzUi.Amber : bRally.Hovered ? RallyEdgeHover : (Color)OzUi.Go;
                if (rallyEdge.color != e) rallyEdge.color = e;
            }
            PlaceRallyWords(hud.Rally.Armed ? "CANCEL" : "SET RALLY");
        }

        /// <summary>The pin and the words as one group, centred on the button.</summary>
        void PlaceRallyWords(string text)
        {
            if (text == rallyShown || bRally.Label == null) return;
            rallyShown = text;
            bRally.Label.Set(text);
            var rr = OzBuildMenuLayout.Rally; float ps = OzBuildMenuLayout.RallyPinSize, gap = OzBuildMenuLayout.RallyPinGap;
            float lw = bRally.Label.preferredWidth;
            if (lw < 1f || lw > rr.w) lw = text.Length * OzBuildMenuLayout.RallyFont * 0.74f;
            float left = Mathf.Max(8f, (rr.w - (ps + gap + lw)) * 0.5f);
            if (rallyPin) rallyPin.rectTransform.Place(new OzRect(Mathf.Round(left), (rr.h - ps) * 0.5f, ps, ps));
            bRally.Label.FillParent(OzH.L, Mathf.Round(left + ps + gap), 0, 0, 0);
        }

        /// <summary>A plain layer of a button drawn right over its face (under the hatch and the content).</summary>
        static Image Layer(HudButton b, string name, string rel, Color c, float inset)
        {
            var im = Hud.Img(b.Rt, name, c);
            var s = OzKit.GetSprite(rel); if (s != null) { im.sprite = s; im.type = Image.Type.Sliced; }
            im.rectTransform.Fill(inset, inset, inset, inset);
            im.transform.SetSiblingIndex(b.Bg.transform.GetSiblingIndex() + 1);
            return im;
        }

        /// <summary>The disabled hatch keeps its period on a resized button.</summary>
        static void Rehatch(HudButton b, Vector2 size)
        {
            if (b.Hatch && b.Hatch.texture) b.Hatch.uvRect = new Rect(0, 0, size.x * 2f / b.Hatch.texture.width, size.y * 2f / b.Hatch.texture.height);
        }
    }
}
