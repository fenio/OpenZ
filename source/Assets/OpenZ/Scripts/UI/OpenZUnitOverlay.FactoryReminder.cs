using System.Collections.Generic;
using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// Factory reminder (k247), the overlay's part (rules: OzFactoryReminder.cs, look: OzFactoryReminder.View.cs): DrawBadges asks
    /// FactoryReminder for every producer (it watches captures and says whether the badge carries the mark this frame; a marked
    /// factory keeps its badge even with idle badges off), every drawn badge gets Reminder(...) after Apply, and Hide() draws the one
    /// mouseover tip: with the pointer on a marked factory or on its badge (not over the HUD, not while its build menu is open),
    /// after the HUD tooltip's 0.35 s, the game's tooltip panel says "CLICK TO SET PRODUCTION & RALLY POINT" (or the one still to
    /// do). Silent, view only.
    /// </summary>
    public partial class OpenZUnitOverlay
    {
        readonly HashSet<int> remindNow = new HashSet<int>(); int remindFrame = -1;
        RectTransform remTip; OzText remTipText; CanvasGroup remTipCg; string remTipShown; int remTipFor = -1; float remTipSince, remTipAt = -10f;
        /// <summary>Play check: frames the reminder tip was on screen, the text it showed last, frames a badge carried the mark.</summary>
        public static int ReminderTipFrames, ReminderBadgeFrames; public static string ReminderTipLast = "";

        /// <summary>DrawBadges, for every producer: watch it and say whether its badge carries the reminder this frame.</summary>
        bool FactoryReminder(ZObject b, bool own)
        {
            if (remindFrame != Time.frameCount) { remindFrame = Time.frameCount; remindNow.Clear(); }
            bool on = OzFactoryReminder.Watch(game.World, b, hud.Player) && own;
            if (on) remindNow.Add(b.Id);
            return on;
        }

        /// <summary>This frame's mark of the building with this id (after DrawBadges asked FactoryReminder).</summary>
        bool Reminding(int id)
        {
            bool on = remindFrame == Time.frameCount && remindNow.Contains(id);
            if (on) ReminderBadgeFrames++;
            return on;
        }

        /// <summary>Hide(), every frame: the mouseover tip, drawn or hidden.</summary>
        void FactoryReminderTip()
        {
            int id = -1; string text = null;
            if (hud != null && game != null && game.World != null && canvas && canvas.enabled && !hud.Modal && !hud.PointerOverUi
                && remindFrame == Time.frameCount && remindNow.Count > 0)
            {
                var hov = hud.input ? hud.input.Hover : null;
                var open = hud.Production ? hud.Production.Building : null;
                foreach (var rid in remindNow)
                {
                    var b = game.World.Get(rid);
                    if (b == null || b == open) continue;                                              // its build menu is open: he is setting it now
                    if (b != hov && !(BadgeRects.TryGetValue(rid, out var r) && r.Contains(hud.Pointer))) continue;
                    text = OzFactoryReminder.Tip(b, hud.Player);
                    if (text != null) { id = rid; break; }
                }
            }
            float now = OzMotion.Now;
            if (id != remTipFor) { remTipFor = id; remTipSince = now; }
            bool show = id >= 0 && now - remTipSince > 0.35f && !Mouse0();                           // the HUD tooltip's wait; not while the left button is held
            if (show && remTip == null) BuildReminderTip();
            if (remTip == null) return;
            if (show && !remTip.gameObject.activeSelf) remTipAt = now;
            remTip.gameObject.SetActiveFast(show);
            if (!show) return;
            remTip.SetAsLastSibling();
            remTipCg.alpha = OzMotion.T(remTipAt, OzMotion.Dur(0.1f, true));
            if (text != remTipShown)
            {
                remTipShown = text; remTipText.Set(text);
                float w = Mathf.Clamp(remTipText.preferredWidth + 2f * OzLayout.TipPadX, 160f, OzLayout.TipMaxW);
                remTip.sizeDelta = new Vector2(w, 2f * OzLayout.TipPadY + 14f);
            }
            float sf = canvas.scaleFactor > 0 ? canvas.scaleFactor : 1f;
            var size = remTip.sizeDelta; var cs = root.rect.size; var pu = hud.Pointer / sf;               // the root's origin is bottom-left, the panel's pivot top-left
            if (!ReminderTipUnderBadge(id, size, cs, sf, out var p))   // k247b (UI/OpenZUnitOverlay.ReminderTip.cs): under the badge's "!", aligned to it; beside the pointer only while its badge is not drawn
            {
                p = pu + new Vector2(OzLayout.TipDX, -OzLayout.TipDY);
                if (p.x + size.x > cs.x - OzLayout.M) p.x = pu.x - OzLayout.TipDX - size.x;
                if (p.y - size.y < OzLayout.M) p.y = pu.y + OzLayout.TipDY + size.y;
                p.x = Mathf.Clamp(p.x, OzLayout.M, Mathf.Max(OzLayout.M, cs.x - OzLayout.M - size.x));
            }
            remTip.anchoredPosition = SnapU(p, sf);
            ReminderTipFrames++; ReminderTipLast = text;
        }

        static bool Mouse0() { var m = UnityEngine.InputSystem.Mouse.current; return m != null && m.leftButton.isPressed; }

        /// <summary>The game's tooltip panel (OpenZHud.BuildTooltip: Panels/tooltip, the title line), on the overlay over the badges.</summary>
        void BuildReminderTip()
        {
            remTip = Hud.Node("ReminderTip", root); remTip.anchorMin = remTip.anchorMax = Vector2.zero; remTip.pivot = new Vector2(0, 1);
            var bg = Hud.Kit(remTip, "Bg", "Panels/tooltip", new OzRect(0, 0, 48, 48), null); bg.rectTransform.Fill();
            if (!OzKit.Available) bg.color = OzUi.A(OzUi.GM900, 0.97f);
            remTipText = Hud.Label(remTip, "Title", "", OzLayout.TipTitleSize, OzUi.Ink, 0.1f).At(OzLayout.TipPadX, OzLayout.TipPadY + 1, OzH.L, false, OzLayout.TipMaxW);
            remTipCg = remTip.gameObject.AddComponent<CanvasGroup>(); remTipCg.blocksRaycasts = false; remTipCg.interactable = false;
            remTip.gameObject.SetActive(false);
        }
    }
}
