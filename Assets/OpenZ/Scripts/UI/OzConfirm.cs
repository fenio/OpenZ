using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace OpenZ
{
    /// <summary>
    /// One self-contained premium modal for both scenes (the HUD's HudButton poll does not run in the menu scene): its own overlay
    /// canvas above everything, a GM_950 scrim, the kit plate with an amber tab, the title, one line on what happens, YES (danger,
    /// ENTER) and NO (ESC). It polls the mouse itself (left release over the pressed button, like the HUD), so it works with the
    /// game paused (unscaled time). While it is open the menus below take no input (OpenZHud / OpenZPauseMenu / OpenZMainMenu check
    /// <see cref="IsOpen"/>). Sounds: ui_panel_open on open, ui_hover on hover, ui_confirm on YES, ui_cancel on NO (the caller's sfx).
    /// </summary>
    public sealed class OzConfirm : MonoBehaviour
    {
        public static bool IsOpen => current && current.open;
        /// <summary>Proof / checks: opens, answers (YES / NO).</summary>
        public static int Opened, Yes, No;
        /// <summary>The frame the modal was answered: the ESC / click that closed it must not also reach the menu below.</summary>
        public static int ClosedFrame = -1;
        /// <summary>Open, or answered this frame: menus below ignore input.</summary>
        public static bool Blocks => IsOpen || ClosedFrame == Time.frameCount;
        static OzConfirm current;

        Canvas canvas; RectTransform panel; CanvasGroup cg; Image scrim; OzText title, body;
        HudButton yes, no; HudButton hover, pressed;
        Action onYes, onNo; Action<string> sfx; bool open; float openAt, closeAt = -10f; int openFrame;
        const float W = 520f, H = 214f, BtnW = 200f, BtnH = 50f;

        /// <summary>Ask: title + one line; YES runs onYes (after the modal closed), NO / ESC / a click outside runs onNo.</summary>
        public static void Ask(string what, string line, Action onYes, Action<string> sfx, int sortingOrder = 400, Action onNo = null)
        {
            if (current == null)
            {
                var go = new GameObject("OzConfirm", typeof(RectTransform)) { layer = 5 };
                current = go.AddComponent<OzConfirm>();
                current.Build(sortingOrder);
            }
            current.Show(what, line, onYes, onNo, sfx, sortingOrder);
        }

        /// <summary>Proof hook: answer the open modal as a click would.</summary>
        public static void ProofAnswer(bool y) { if (IsOpen) current.Answer(y); }

        void Build(int order)
        {
            canvas = gameObject.AddComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = order;
            var cs = gameObject.AddComponent<CanvasScaler>(); cs.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            cs.referenceResolution = new Vector2(1920, 1080); cs.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight; cs.matchWidthOrHeight = 1f;
            var root = Hud.Node("Root", transform); root.Fill();
            cg = root.gameObject.AddComponent<CanvasGroup>(); cg.blocksRaycasts = false;
            scrim = Hud.Img(root, "Scrim", OzUi.A(OzUi.GM950, 0.7f)); scrim.rectTransform.Fill();
            panel = Hud.Node("Panel", root).Place(new OzRect(OzAnchor.C, OzLayout.RefW * 0.5f - W * 0.5f, OzLayout.RefH * 0.5f - H * 0.5f, W, H));
            var back = Hud.Img(panel, "Back", OzUi.A(OzUi.GM950, 0.97f)); back.rectTransform.Fill(4, 4, 4, 4);
            Hud.Plate(panel, "md", 0f, OzUi.Alert);
            Hud.Label(panel, "Stencil", "CONFIRM", 9, OzUi.Muted, 0.3f, OzStyle.Plain).At(28, 22);
            title = Hud.Label(panel, "Title", "ARE YOU SURE?", 26, OzUi.Ink, 0.06f).At(28, 40, OzH.L, false, W - 56);
            body = Hud.Label(panel, "Body", "", 13, OzUi.Ink2, 0.04f, OzStyle.Plain).At(28, 84, OzH.L, false, W - 56);
            float by = H - BtnH - 26;
            no = HudButton.Make(panel, "No", "NO", new Vector2(BtnW, BtnH), HudButtonKind.Large, 16, 0.16f, HudButtonStyle.Normal, true);
            no.Rt.Place(new OzRect(28, by, BtnW, BtnH)); no.Priority = int.MaxValue - 1;
            Hud.Keycap(no.Content, "Esc", "ESC", BtnW - 10, (BtnH - 14) * 0.5f);
            yes = HudButton.Make(panel, "Yes", "YES", new Vector2(BtnW, BtnH), HudButtonKind.Large, 16, 0.16f, HudButtonStyle.Danger, true);
            yes.Rt.Place(new OzRect(W - 28 - BtnW, by, BtnW, BtnH)); yes.Priority = int.MaxValue - 1;
            Hud.Keycap(yes.Content, "Enter", "ENTER", BtnW - 10, (BtnH - 14) * 0.5f);
            gameObject.SetActive(false);
        }

        void Show(string what, string line, Action y, Action n, Action<string> s, int order)
        {
            onYes = y; onNo = n; sfx = s; canvas.sortingOrder = order;
            title.Set(what); body.Set(line);
            open = true; openAt = Time.unscaledTime; openFrame = Time.frameCount; hover = pressed = null;
            gameObject.SetActive(true);
            Opened++;
            Play("ui_panel_open");
            OzSoundLog.Add("ui", "confirm", "ui_panel_open", "opened: " + what + " / " + line);
            Debug.Log($"[OpenZ Confirm] open: {what} ({line})");
        }

        void Play(string id) { try { if (sfx != null) sfx(id); else OzSfx.Ui(id); } catch (Exception) { } }

        void Answer(bool y)
        {
            if (!open) return;
            open = false; closeAt = Time.unscaledTime; ClosedFrame = Time.frameCount;
            if (y) Yes++; else No++;
            Play(y ? "ui_confirm" : "ui_cancel");
            OzSoundLog.Add("ui", "confirm", y ? "ui_confirm" : "ui_cancel", y ? "YES" : "NO");
            Debug.Log($"[OpenZ Confirm] {(y ? "YES" : "NO")}");
            var a = y ? onYes : onNo; onYes = onNo = null;
            a?.Invoke();
        }

        static bool Over(HudButton b, Vector2 p) => b != null && b.Rt && RectTransformUtility.RectangleContainsScreenPoint(b.Rt, p, null);

        void Update()
        {
            float now = Time.unscaledTime;
            if (!open)
            {
                float k = Mathf.Clamp01((now - closeAt) / 0.1f);
                cg.alpha = 1f - k;
                if (k >= 1f) gameObject.SetActive(false);
                return;
            }
            float kin = OzEase.OutCubic(Mathf.Clamp01((now - openAt) / Mathf.Max(0.01f, OzMotion.Dur(OzMotion.Short, true))));
            cg.alpha = kin; panel.localScale = Vector3.one * Mathf.Lerp(0.94f, 1f, kin);
            if (Time.frameCount == openFrame) return;                                                    // the click / key that opened it
            var kb = Keyboard.current; var m = Mouse.current;
            if (kb != null)
            {
                if (kb.escapeKey.wasPressedThisFrame || OzKeyNames.LetterPressed(kb, 'N') || kb.backspaceKey.wasPressedThisFrame) { Answer(false); return; }
                if (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame || OzKeyNames.LetterPressed(kb, 'Y')) { Answer(true); return; }
            }
            if (m == null) return;
            var p = m.position.ReadValue();
            var h = Over(yes, p) ? yes : Over(no, p) ? no : null;
            if (h != hover)
            {
                if (hover != null) { hover.Hovered = false; hover.Refresh(); }
                hover = h; if (h != null) { h.Hovered = true; h.Refresh(); Play("ui_hover"); }
            }
            if (m.leftButton.wasPressedThisFrame)
            {
                pressed = h; if (h != null) { h.Pressed = true; h.Refresh(); }
                else if (!RectTransformUtility.RectangleContainsScreenPoint(panel, p, null)) { Answer(false); return; }   // a click outside = NO
            }
            if (m.leftButton.wasReleasedThisFrame && pressed != null)
            {
                var b = pressed; pressed = null; b.Pressed = false; b.Refresh();
                if (b == h) Answer(b == yes);
            }
            if (m.rightButton.wasPressedThisFrame) Answer(false);
        }

        void OnDestroy() { if (current == this) current = null; }
    }
}
