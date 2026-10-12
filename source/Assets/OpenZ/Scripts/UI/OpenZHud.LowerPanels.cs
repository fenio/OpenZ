using System.Collections.Generic;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// The lower panels and the production window. While the production window of a fort or factory shows
    /// (OpenZProductionWindow.IsOpen), the panels along the bottom (command bar, unit panel with its EXIT CREW tab, the
    /// under-attack tab, the factory list) and the minimap are not drawn and take no pointer; the top group (command strip,
    /// territory tab, menu button, cannon plates, messages, the fort alarm) stays. They are back on the frame the window
    /// closes, whatever closed it.
    ///
    /// The switch is the one the pause menu uses for the whole HUD (SetVisible: Canvas.enabled, a cut without a fade). Each
    /// lower panel has its own nested Canvas (the minimap and the production window always had one) and that Canvas is
    /// switched off. Nothing else about a panel changes: it stays active, keeps its place, its content and its own show /
    /// hide rules, so it returns exactly as it would be had it never been hidden, and the production window keeps the place
    /// its placement rule gave it (that rule goes on treating the panels' places as taken, so the window never sits where a
    /// panel comes back).
    ///
    /// Pointer: every HUD hit test goes through Contains, which answers false for a rectangle inside a hidden panel. A press
    /// there is a press on the map, no button there can be hovered, the minimap takes no click and no order.
    /// Keys: every shortcut of the hidden panels keeps working (next robot / vehicle / gun, the groups, exit crew, the last
    /// message). The factory list key closes the window and shows the list: the list is a lower panel and cannot show beside
    /// the window, so the key always ends with the list on screen.
    /// Alerts: the fort alarm is part of the top group and stays; while the window shows it is kept in front of the window
    /// and, being the one drawn in front, it is also the one that takes the pointer where the two meet.
    /// The under-attack tab belongs to the unit panel and returns with it (its sounds and the voice never depended on it).
    /// </summary>
    public partial class OpenZHud
    {
        readonly List<RectTransform> lowerRoots = new List<RectTransform>();
        readonly List<Canvas> lowerCanvases = new List<Canvas>();
        readonly List<Canvas> lowerAdded = new List<Canvas>();
        bool lowerHidden;
        /// <summary>Hit-test priority of the fort alarm plate: one above the production window's own buttons (10), because the
        /// plate is drawn in front of the window; the pause menu and the end screen (100) stay above it.</summary>
        const int FortAlarmPriority = 11;

        /// <summary>The one state the lower panels follow: the production window of a fort or factory is showing.</summary>
        public bool BuildMenuShowing => Production && Production.IsOpen;
        /// <summary>The lower panels and the minimap are switched off right now.</summary>
        public bool LowerPanelsHidden => lowerHidden;
        /// <summary>The panels that step aside (checks read their rectangles and canvases).</summary>
        public IReadOnlyList<RectTransform> LowerPanelRoots => lowerRoots;

        /// <summary>Adds a panel to the lower group: it gets its own nested Canvas (unless it has one) and follows the switch.
        /// A HUD part placed along the bottom later joins the group with this one call.</summary>
        public void AddLowerPanel(RectTransform rt)
        {
            if (!rt || lowerRoots.Contains(rt)) return;
            var c = rt.GetComponent<Canvas>();
            if (!c)
            {
                c = rt.gameObject.AddComponent<Canvas>();
                if (Canvas) c.additionalShaderChannels = Canvas.additionalShaderChannels;                 // the text keeps the channels it had on the HUD canvas
                lowerAdded.Add(c);
            }
            c.enabled = !lowerHidden;
            lowerRoots.Add(rt); lowerCanvases.Add(c);
        }

        void BuildLowerPanels()
        {
            AddLowerPanel(alertTab);                                                                       // under the unit panel, as built
            AddLowerPanel(Minimap ? Minimap.Panel : null);
            AddLowerPanel(unitPanel);                                                                      // with the EXIT CREW tab (its child)
            AddLowerPanel(cmdBar);
            AddLowerPanel(factoryPanel);
            if (fortToast != null && fortToast.hot != null) fortToast.hot.Priority = FortAlarmPriority;   // in front of the window = takes the click there (no click through the plate onto the window's X)
        }

        /// <summary>Brings the lower panels in line with the production window. Called at the end of every frame (LateUpdate,
        /// after every Update that can open or close the window) and whenever the HUD is shown or hidden as a whole.</summary>
        void SyncLowerPanels()
        {
            // game over with the window up: the HUD is leaving for good, nothing pops back in just to slide out
            bool hide = BuildMenuShowing || (lowerHidden && exited);
            if (Canvas)
                for (int i = 0; i < lowerAdded.Count; i++)
                {
                    var c = lowerAdded[i]; if (!c) continue;
                    var want = c.additionalShaderChannels | Canvas.additionalShaderChannels;
                    if (want != c.additionalShaderChannels) c.additionalShaderChannels = want;
                }
            if (hide != lowerHidden)
            {
                lowerHidden = hide;
                for (int i = 0; i < lowerCanvases.Count; i++) if (lowerCanvases[i]) lowerCanvases[i].enabled = !hide;
                if (hide) DropLowerHover();
            }
            if (hide) FortAlarmInFront();
        }

        /// <summary>The button under the pointer went with its panel: no hover state and no tooltip is left behind.</summary>
        void DropLowerHover()
        {
            var b = HotButton;
            if (b == null || !b.Rt || !LowerHides(b.Rt)) return;
            b.Hovered = false; b.Refresh(); HotButton = null;
            if (tipPanel) { tipPanel.gameObject.SetActive(false); tipFor = null; tipShown = null; }
        }

        /// <summary>A fort alarm raised before the window opened would lie behind it (the window goes to the front when it
        /// opens): while the window shows the alarm plate is the one in front, as it already is when it arrives later.</summary>
        void FortAlarmInFront()
        {
            var win = Production ? Production.WindowRect : null;
            if (fortToast == null || !fortToast.used || !fortToast.rt || !win) return;
            if (fortToast.rt.parent == win.parent && fortToast.rt.GetSiblingIndex() < win.GetSiblingIndex()) fortToast.rt.SetAsLastSibling();
        }

        /// <summary>Contains asks this for every rectangle: true = it belongs to a lower panel that is switched off.</summary>
        static bool LowerHides(RectTransform rt)
        {
            var h = I;
            if (h is null || !h.lowerHidden) return false;
            var roots = h.lowerRoots;
            for (int i = 0; i < roots.Count; i++) { var r = roots[i]; if (r && rt.IsChildOf(r)) return true; }
            return false;
        }

        /// <summary>The factory list key. With the production window up the list cannot show (it is a lower panel): the key
        /// closes the window, like a click on the map, and leaves the list open.</summary>
        void FactoryListKey()
        {
            if (BuildMenuShowing) { Production.Close(); factoryListOpen = true; }
            else factoryListOpen = !factoryListOpen;
        }
    }
}
