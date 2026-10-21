namespace OpenZ
{
    /// <summary>
    /// K327: the repair key (R by default) clicks SET RALLY while this build menu is open (the rule: View/OzRallyKeyCore.cs, the key:
    /// View/OzRallyKey.cs). The key's cap stands in the button's tooltip, as PLACE shows its key (the hook in OpenZProductionWindow.cs).
    /// </summary>
    public partial class OpenZProductionWindow
    {
        /// <summary>The menu / the rally tool as they were at the end of the last frame: a menu that opens or closes in the same frame
        /// as the key keeps only its own action (OzRallyKeyCore route Swallow).</summary>
        public bool RallyKeyWasOpen { get; private set; }
        public bool RallyKeyWasArmed { get; private set; }
        /// <summary>Checks: SET RALLY clicks made by the key.</summary>
        public static int RallyKeyClicks;

        void LateUpdate()
        {
            RallyKeyWasOpen = Building != null;
            RallyKeyWasArmed = hud != null && hud.Rally && hud.Rally.Armed;
        }

        void OnDisable() { RallyKeyWasOpen = RallyKeyWasArmed = false; }

        /// <summary>The key's press as a click on SET RALLY: the button lights as under the pointer (seen in the menu's 100 ms close
        /// fade), then the button's own click runs (the same delegate the HUD calls on a mouse click) and the HUD's click sound after
        /// it, in the HUD's order. It arms the rally tool for this building and closes the menu (or cancels an armed tool). False = no
        /// menu open or no live button: nothing done.</summary>
        public bool RallyKeyClick()
        {
            if (Building == null || bRally == null || !bRally.Interactable || bRally.OnClick == null) return false;
            if (rallyFill) rallyFill.color = RallyFillHover;
            if (rallyEdge) rallyEdge.color = RallyEdgeHover;
            RallyKeyClicks++;
            bRally.OnClick(new HudClick());
            OpenZHud.UiClickSound();
            return true;
        }
    }
}
