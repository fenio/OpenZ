using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace OpenZ
{
    // ============================================================================================================================
    // menu's CHANGES & UPDATES screen, the item directly above CREDITS. A centred modal like OPTIONS / CONTROLS (same plate and
    // scrim, 1100 u wide, 864 u tall on the 1080 u canvas: 1100 x 864 px at 1080p, 1630 x 1280 px at 2560x1600) that reads
    // two paragraphs, then per section a bold amber band and per entry a compact card (bold title, then the labelled lines
    // row is ever drawn (no label, no gap), `why` stays in ChangesEntry only so an older JSON still parses; the label column
    // is narrowed to the short IN Z / NOW labels and the card spacing tightened). Only title / intro / sections are read: the JSON's not_in_yet list
    // and every entry's `source` have no field here, so they can never show. Every text block is measured (TMP preferred size)
    // and stacked, then checked for overlap / overflow (log line). Scroll: wheel, drag the text, drag or click the slim
    // scrollbar, arrows / W S, PgUp / PgDn / Space, Home / End; soft fade at the top / bottom edge (RectMask2D softness).
    // ESC, Backspace, BACK or a click outside the plate closes it. Sounds: panel open / close, hover on BACK, a light tick per
    // scroll step, ui_disabled when a key hits the end.
    // Hooks in OpenZMainMenu.cs: Side.Changes, AddItem, BuildMain -> BuildChanges, OpenSide reset, SideRt / SideCg, SidePanels,
    // Keys (the modal owns the keyboard), Update -> TickChanges; OpenZMainMenu.MenuExe.cs TickExe: the scrim.
    // ============================================================================================================================
    public partial class OpenZMainMenu
    {
        [Serializable] public sealed class ChangesEntry { public string title, in_z, now, why; }
        [Serializable] public sealed class ChangesSection { public string heading; public ChangesEntry[] entries; }
        [Serializable] public sealed class ChangesDoc { public string title, intro; public ChangesSection[] sections; }

        public const string ChangesResource = "OpenZ/Text/changes_updates";
        const float CuW = 1100f, CuH = 864f, CuPad = 36f, CuHeadY = 22f, CuRuleY = 64f, CuFoot = 76f, CuGutter = 18f, CuFade = 30f;
        const float CuTextSize = 15f, CuTitleSize = 18f, CuLabelW = 52f, CuTextX = 88f, CuCardPad = 24f, CuCapMid = 0.605f;

        RectTransform changesRt, cuView, cuContent, cuTrack, cuTrackHit; CanvasGroup changesCg; Image cuThumb; OzText cuWhere;
        float cuScroll, cuTarget, cuDragY0, cuDragS0, cuTickAt; int cuDrag, cuSec = -2, cuEntries, cuOverlaps, cuOverflow;
        readonly List<float> cuSectionY = new List<float>(); readonly List<string> cuSectionName = new List<string>();
        readonly List<OzText> cuTexts = new List<OzText>(); readonly List<Rect> cuRects = new List<Rect>();
        string cuLoadError;

        ChangesDoc LoadChanges()
        {
            cuLoadError = null;
            try
            {
                var ta = Resources.Load<TextAsset>(ChangesResource);
                if (ta == null) { cuLoadError = "Resources/" + ChangesResource + ".json is missing"; return null; }
                var d = JsonUtility.FromJson<ChangesDoc>(ta.text);
                Resources.UnloadAsset(ta);
                return d;
            }
            catch (Exception e) { cuLoadError = e.Message; return null; }
        }

        void BuildChanges()
        {
            changesRt = SidePanel("Changes", "Panels/options_panel", out changesCg);
            Modal(changesRt, CuW, CuH, Side.Changes, true);
            changesRt.gameObject.SetActive(true);        // built while active: TMP under an inactive parent never wakes, so wrapped text measured one line tall
            var p = changesRt;
            var doc = LoadChanges();
            cuEntries = 0;
            if (doc?.sections != null) foreach (var s in doc.sections) cuEntries += s?.entries?.Length ?? 0;

            Hud.Label(p, "Title", string.IsNullOrEmpty(doc?.title) ? "CHANGES & UPDATES" : doc.title.ToUpperInvariant(), 20, OzUi.Ink, 0.16f).At(CuPad, CuHeadY);
            Hud.Label(p, "Sub", $"OPENZ VS THE 1996 ORIGINAL · {cuEntries} CHANGES", 10, OzUi.Muted, 0.2f, OzStyle.Plain).At(CuW - CuPad, CuHeadY + 6, OzH.R, false, 520);
            Hud.Img(p, "HeadRule", OzUi.A(OzUi.Khaki, 0.18f)).rectTransform.TL(CuPad, CuRuleY, CuW - 2 * CuPad, 1);
            Hud.Img(p, "FootRule", OzUi.A(OzUi.Khaki, 0.18f)).rectTransform.TL(CuPad, CuH - CuFoot, CuW - 2 * CuPad, 1);

            // the scroll view: a soft fade at the top / bottom edge (softness is in screen px: scaled to the canvas)
            float vx = CuPad, vy = CuRuleY + 1, vw = CuW - 2 * CuPad - CuGutter, vh = CuH - CuFoot - CuRuleY - 1;
            cuView = Hud.Node("View", p).Place(new OzRect(vx, vy, vw, vh));
            var mask = cuView.gameObject.AddComponent<RectMask2D>();
            mask.softness = new Vector2Int(0, Mathf.Max(1, Mathf.RoundToInt(CuFade * Screen.height / OzLayout.RefH)));
            cuContent = Hud.Node("Content", cuView);
            cuContent.anchorMin = new Vector2(0, 1); cuContent.anchorMax = new Vector2(1, 1); cuContent.pivot = new Vector2(0.5f, 1);
            cuContent.offsetMin = cuContent.offsetMax = Vector2.zero;

            cuTexts.Clear(); cuRects.Clear(); cuSectionY.Clear(); cuSectionName.Clear(); cuSec = -2;
            float cw = vw, y = CuFade + 10f;
            if (doc == null || doc.sections == null)
            {
                y += CuBlock("Missing", "The change list could not be loaded (" + Esc(cuLoadError ?? "no sections") + ").", 16, OzUi.Muted, 28, y, cw - 56) + 20f;
                Debug.LogWarning("[OpenZ Menu] CHANGES & UPDATES: " + (cuLoadError ?? "no sections in the JSON"));
            }
            else
            {
                var paras = (doc.intro ?? "").Replace("\r", "").Split(new[] { "\n\n" }, StringSplitOptions.RemoveEmptyEntries);
                for (int i = 0; i < paras.Length; i++)
                    y += CuBlock("Intro" + i, Esc(paras[i].Trim()), 16, i == 0 ? OzUi.Ink : OzUi.Ink2, 28, y, cw - 56) + (i < paras.Length - 1 ? 14f : 0f);
                y += 34f;
                for (int si = 0; si < doc.sections.Length; si++)
                {
                    var s = doc.sections[si]; if (s == null) continue;
                    cuSectionY.Add(y); cuSectionName.Add((s.heading ?? "").ToUpperInvariant());
                    y += CuBand(s, si, y, cw) + 14f;
                    if (s.entries != null)
                        for (int ei = 0; ei < s.entries.Length; ei++)
                            if (s.entries[ei] != null) y += CuCard(s.entries[ei], si + "_" + ei, y, cw) + 12f;
                    y += 22f;
                }
                y += FeedbackBlock(cuContent, cuView, y, cw, Side.Changes, "changes & updates") + 26f;
                Hud.Img(cuContent, "EndRule", OzUi.A(OzUi.Amber, 0.55f)).rectTransform.TL(cw * 0.5f - 48f, y + 4f, 96f, 2f);
                y += 22f;
                CuTrackRect(Hud.Label(cuContent, "End", "END OF THE LIST", 11, OzUi.Muted, 0.24f, OzStyle.Plain).At(cw * 0.5f, y, OzH.C, false, 400));
                y += 20f;
            }
            y += CuFade + 16f;
            cuContent.sizeDelta = new Vector2(0, y);

            // slim scrollbar in the gutter (a wider invisible hit strip for the drag)
            float tx = CuW - CuPad - 4f, ty = vy + 16f, th = vh - 32f;
            cuTrack = Hud.Node("Track", p).Place(new OzRect(tx, ty, 4f, th));
            Hud.Img(cuTrack, "Bg", OzUi.A(OzUi.GM950, 0.9f)).rectTransform.Fill();
            cuThumb = Hud.Img(cuTrack, "Thumb", OzUi.Amber);
            cuThumb.rectTransform.anchorMin = new Vector2(0, 1); cuThumb.rectTransform.anchorMax = new Vector2(1, 1); cuThumb.rectTransform.pivot = new Vector2(0.5f, 1);
            cuTrackHit = Hud.Node("TrackHit", p).Place(new OzRect(tx - 7f, ty - 6f, 18f, th + 12f));

            // footer: BACK (hover sound; the close plays the panel sound), where you are, the keys
            const float bw = 150f, bh = 38f;
            var back = HudButton.Make(p, "Back", "BACK", new Vector2(bw, bh), HudButtonKind.Small, 12, 0.16f, HudButtonStyle.Normal, false, OzH.L);
            back.Rt.Place(new OzRect(CuPad, CuH - CuFoot + (CuFoot - bh) * 0.5f, bw, bh));
            Hud.Keycap(back.Content, "Esc", "ESC", bw - 10, (bh - 14) * 0.5f);
            AddHot(back.Rt, View.Main, CloseSide, null, back, null, Side.Changes);
            cuWhere = Hud.Label(p, "Where", "", 11, OzUi.Amber, 0.2f, OzStyle.Plain).At(CuPad + bw + 26f, CuH - CuFoot * 0.5f, OzH.L, true, 280);
            Hud.Label(p, "Keys", "WHEEL · DRAG · ↑ ↓ · PGUP / PGDN · HOME / END · ESC CLOSE", 10, OzUi.Muted, 0.2f, OzStyle.Plain)
                .At(CuW - CuPad, CuH - CuFoot * 0.5f, OzH.R, true, 560);

            changesRt.gameObject.SetActive(false);
            cuScroll = cuTarget = 0f; cuDrag = 0;
            CuCheck();
            Debug.Log($"[OpenZ Menu] CHANGES & UPDATES built: {cuSectionY.Count} sections, {cuEntries} entries, {cuTexts.Count} text blocks measured, content {y:0} u, view {vh:0} u, "
                      + $"overlaps {cuOverlaps}, overflow {cuOverflow}, not_in_yet / source never read");
        }

        /// <summary>A wrapped text block at (x, y) in the content, w wide; its measured height (TMP preferred size).</summary>
        float CuBlock(string name, string rich, float size, Color c, float x, float y, float w, bool italic = false)
        {
            var t = Hud.Label(cuContent, name, italic ? "<i>" + rich + "</i>" : rich, size, c, 0f, OzStyle.Plain).Body(x, y, w);
            float h = Mathf.Max(size * 1.25f, t.preferredHeight + 2f);
            t.rectTransform.sizeDelta = new Vector2(w, h);
            CuTrackRect(t);
            return h;
        }

        void CuTrackRect(OzText t)
        {
            var rt = t.rectTransform;
            cuTexts.Add(t); cuRects.Add(new Rect(rt.anchoredPosition.x, -rt.anchoredPosition.y, rt.sizeDelta.x, rt.sizeDelta.y));
        }

        float CuBand(ChangesSection s, int si, float y, float cw)
        {
            const float bh = 40f;
            Hud.Img(cuContent, "Band" + si, OzUi.A(OzUi.Amber, 0.94f)).rectTransform.TL(0, y, cw, bh);
            Hud.Img(cuContent, "BandEdge" + si, OzUi.AmberHi).rectTransform.TL(0, y, 5f, bh);
            CuTrackRect(Hud.Label(cuContent, "Heading" + si, "<b>" + Esc((s.heading ?? "").ToUpperInvariant()) + "</b>", 15, OzUi.GM950, 0.22f, OzStyle.Plain)
                .At(22f, y + bh * 0.5f, OzH.L, true, cw - 240f));
            int n = s.entries?.Length ?? 0;
            CuTrackRect(Hud.Label(cuContent, "Count" + si, n + (n == 1 ? " CHANGE" : " CHANGES"), 11, OzUi.A(OzUi.GM950, 0.72f), 0.2f, OzStyle.Plain)
                .At(cw - 20f, y + bh * 0.5f, OzH.R, true, 170f));
            return bh;
        }

        float CuCard(ChangesEntry e, string id, float y, float cw)
        {
            var bg = Hud.Img(cuContent, "Card" + id, OzUi.A(OzUi.GM800, 0.62f)).rectTransform;
            var edge = Hud.Img(cuContent, "CardEdge" + id, OzUi.A(OzUi.Khaki, 0.22f)).rectTransform;
            float y0 = y; y += 16f;
            y += CuBlock("Title" + id, "<b>" + Esc(e.title ?? "") + "</b>", CuTitleSize, OzUi.Ink, CuCardPad, y, cw - 2 * CuCardPad) + 9f;
            y += CuRow(id + "Z", "IN Z", OzUi.Muted, Esc(e.in_z ?? ""), OzUi.Ink2, false, y, cw) + 6f;
            y += CuRow(id + "N", "NOW", OzUi.Ink, Esc(e.now ?? ""), OzUi.Ink, false, y, cw);
            y += 17f;
            bg.TL(0, y0, cw, y - y0); edge.TL(0, y0, 3f, y - y0);
            return y - y0;
        }

        /// <summary>One labelled line: the label caps centred on the text's first line, the text wrapped in its own column.</summary>
        float CuRow(string id, string label, Color lc, string text, Color tc, bool italic, float y, float cw)
        {
            float h = CuBlock("Text" + id, text, CuTextSize, tc, CuTextX, y, cw - CuTextX - CuCardPad, italic);
            CuTrackRect(Hud.Label(cuContent, "Label" + id, "<b>" + label + "</b>", 11, lc, 0.2f, OzStyle.Plain).At(CuCardPad, y + CuCapMid * CuTextSize, OzH.L, true, CuLabelW));
            return h;
        }

        /// <summary>Every text block: no two boxes overlap, and no text is wider / taller than its box (TMP preferred size).</summary>
        void CuCheck()
        {
            cuOverlaps = cuOverflow = 0;
            for (int i = 0; i < cuRects.Count; i++)
            {
                var a = cuRects[i];
                for (int j = i + 1; j < cuRects.Count; j++)
                {
                    var b = cuRects[j];
                    if (a.xMin < b.xMax - 0.5f && b.xMin < a.xMax - 0.5f && a.yMin < b.yMax - 0.5f && b.yMin < a.yMax - 0.5f)
                    { cuOverlaps++; if (cuOverlaps <= 3) Debug.LogWarning($"[OpenZ Menu] CHANGES overlap: {cuTexts[i].gameObject.name} / {cuTexts[j].gameObject.name}"); }
                }
                if (cuTexts[i].graphic is TMPro.TMP_Text tm)
                {
                    var pv = tm.GetPreferredValues(tm.text, a.width, 0f);
                    if (pv.x > a.width + 0.5f || pv.y > a.height + 0.5f || tm.overflowMode == TMPro.TextOverflowModes.Ellipsis)
                    { cuOverflow++; if (cuOverflow <= 3) Debug.LogWarning($"[OpenZ Menu] CHANGES overflow: {tm.gameObject.name} {pv.x:0}x{pv.y:0} in {a.width:0}x{a.height:0}"); }
                }
            }
        }

        void CuScrollBy(float dy, bool key)
        {
            if (cuContent == null) return;
            float max = Mathf.Max(0f, cuContent.sizeDelta.y - cuView.rect.height);
            float t = Mathf.Clamp(cuTarget + dy, 0f, max);
            if (Mathf.Abs(t - cuTarget) < 0.5f) { if (key) CuSound("ui_disabled"); return; }       // already at that end
            cuTarget = t; CuSound("ui_slider_tick");
        }

        void CuSound(string id) { if (OzMotion.Now - cuTickAt < 0.07f) return; cuTickAt = OzMotion.Now; Sfx(id); }

        /// <summary>Update, main view: input while the panel is open, smooth scroll, the scrollbar and the section readout.</summary>
        void TickChanges(float dt)
        {
            if (!changesRt || !changesRt.gameObject.activeSelf || cuContent == null) return;
            float max = Mathf.Max(0f, cuContent.sizeDelta.y - cuView.rect.height);
            if (side == Side.Changes && view == View.Main)
            {
                float inv = 1f / Mathf.Max(0.01f, changesRt.lossyScale.y);                                   // screen px -> panel units (canvas scale x the panel's own fit scale beside the list)
                var m = Mouse.current;
                if (m != null)
                {
                    Vector2 mp = m.position.ReadValue();
                    if (m.leftButton.wasPressedThisFrame)
                    {
                        if (max > 0f && RectTransformUtility.RectangleContainsScreenPoint(cuTrackHit, mp, null))
                        {
                            cuDrag = 2;
                            if (!RectTransformUtility.RectangleContainsScreenPoint(cuThumb.rectTransform, mp, null))   // a click on the track: jump there
                            {
                                RectTransformUtility.ScreenPointToLocalPointInRectangle(cuTrack, mp, null, out var lp);
                                float trh = cuTrack.rect.height, thumbH = cuThumb.rectTransform.sizeDelta.y;
                                cuTarget = Mathf.Clamp01((cuTrack.rect.yMax - lp.y - thumbH * 0.5f) / Mathf.Max(1f, trh - thumbH)) * max;
                                CuSound("ui_slider_tick");
                            }
                        }
                        else if (RectTransformUtility.RectangleContainsScreenPoint(cuView, mp, null)) cuDrag = 1;
                        cuDragY0 = mp.y; cuDragS0 = cuTarget;
                    }
                    if (cuDrag != 0 && !m.leftButton.isPressed) cuDrag = 0;
                    if (cuDrag == 1) { cuTarget = Mathf.Clamp(cuDragS0 + (mp.y - cuDragY0) * inv, 0f, max); cuScroll = cuTarget; }          // the text follows the mouse
                    else if (cuDrag == 2)
                    {
                        float travel = Mathf.Max(1f, cuTrack.rect.height - cuThumb.rectTransform.sizeDelta.y);
                        cuTarget = Mathf.Clamp(cuDragS0 - (mp.y - cuDragY0) * inv / travel * max, 0f, max); cuScroll = cuTarget;
                    }
                    float wheel = m.scroll.ReadValue().y;
                    if (Mathf.Abs(wheel) > 0.01f) CuScrollBy(-Mathf.Sign(wheel) * 72f, false);
                }
                var kb = Keyboard.current;
                if (kb != null && !OzControlsTable.Capturing)
                {
                    float page = Mathf.Max(72f, cuView.rect.height - 72f);
                    if (kb.upArrowKey.wasPressedThisFrame || kb.wKey.wasPressedThisFrame) CuScrollBy(-72f, true);
                    if (kb.downArrowKey.wasPressedThisFrame || kb.sKey.wasPressedThisFrame) CuScrollBy(72f, true);
                    if (kb.pageUpKey.wasPressedThisFrame) CuScrollBy(-page, true);
                    if (kb.pageDownKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame) CuScrollBy(page, true);
                    if (kb.homeKey.wasPressedThisFrame) CuScrollBy(-1e6f, true);
                    if (kb.endKey.wasPressedThisFrame) CuScrollBy(1e6f, true);
                }
            }
            else cuDrag = 0;

            cuTarget = Mathf.Clamp(cuTarget, 0f, max);
            cuScroll = OzMotion.Reduced ? cuTarget : Mathf.Lerp(cuScroll, cuTarget, 1f - Mathf.Exp(-dt * 14f));
            if (Mathf.Abs(cuScroll - cuTarget) < 0.05f) cuScroll = cuTarget;
            cuContent.anchoredPosition = new Vector2(0, cuScroll);

            float trackH = cuTrack.rect.height, vis = max <= 0 ? 1f : cuView.rect.height / cuContent.sizeDelta.y;
            float th = Mathf.Max(28f, trackH * vis);
            cuThumb.rectTransform.sizeDelta = new Vector2(0, th);
            cuThumb.rectTransform.anchoredPosition = new Vector2(0, max <= 0 ? 0 : -(trackH - th) * cuScroll / max);
            var tc = cuDrag == 2 ? (Color)OzUi.AmberHi : (Color)OzUi.Amber; if (cuThumb.color != tc) cuThumb.color = tc;
            cuTrack.gameObject.SetActiveFast(max > 0);

            int sec = -1;
            for (int i = 0; i < cuSectionY.Count; i++) if (cuSectionY[i] <= cuScroll + cuView.rect.height * 0.3f) sec = i;
            if (sec != cuSec && cuWhere != null)
            {
                cuSec = sec;
                cuWhere.Set(sec < 0 ? (cuSectionY.Count > 0 ? "INTRO" : "") : $"{cuSectionName[sec]} · {sec + 1} / {cuSectionY.Count}");
            }
        }

        // ---------------------------------------------------------------- proof hook (Play only): frames at the current resolution
        public float ProofChangesMax => cuContent == null ? 0f : Mathf.Max(0f, cuContent.sizeDelta.y - cuView.rect.height);
        public string ProofChangesInfo => $"sections {cuSectionY.Count}, entries {cuEntries}, texts {cuTexts.Count}, overlaps {cuOverlaps}, overflow {cuOverflow}, scroll {cuScroll:0} / {ProofChangesMax:0} u, side {side}";
        public void ProofChanges(string dir) => StartCoroutine(CuProof(dir));

        IEnumerator CuProof(string dir)
        {
            Directory.CreateDirectory(dir);
            string res = Screen.width + "x" + Screen.height;
            if (side != Side.Changes) OpenSide(Side.Changes);
            yield return new WaitForSecondsRealtime(0.9f);
            CuCheck();
            ScreenCapture.CaptureScreenshot(Path.Combine(dir, $"changes_{res}_top.png"));
            yield return new WaitForSecondsRealtime(0.5f);
            int mid = Mathf.Min(3, cuSectionY.Count - 1);                                                 // a middle section band near the top of the view
            if (mid >= 0) { cuTarget = Mathf.Min(ProofChangesMax, cuSectionY[mid] - 12f); CuSound("ui_slider_tick"); }
            yield return new WaitForSecondsRealtime(1.0f);
            ScreenCapture.CaptureScreenshot(Path.Combine(dir, $"changes_{res}_mid.png"));
            yield return new WaitForSecondsRealtime(0.5f);
            CuScrollBy(1e6f, true);                                                                       // End
            yield return new WaitForSecondsRealtime(1.0f);
            ScreenCapture.CaptureScreenshot(Path.Combine(dir, $"changes_{res}_end.png"));
            yield return new WaitForSecondsRealtime(0.5f);
            Debug.Log($"[OpenZ Menu] CHANGES proof {res}: {ProofChangesInfo}; frames changes_{res}_top / _mid / _end.png in {dir}");
        }
    }
}
