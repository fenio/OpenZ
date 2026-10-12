using System.Collections.Generic;
using OpenZ.Sim;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace OpenZ
{
    /// <summary>
    /// Dev only: on in the editor (PlayerPrefs "oz_dev" = 0 turns it off for takes), off in builds unless "-ozdev" is on the
    /// command line or oz_dev = 1. While the dev flag is on, P belongs to this overlay and stored cannons are placed with the
    /// production window's PLACE button or their own key (default C; PlayerInput skips that key only while it is bound to P). Spawns use the normal sim API (CreateRobotGroup with
    /// Zod's squad size, CreateObject), so everything spawned behaves exactly like produced units. Hidden = inactive panels:
    /// no blocker, no input, no per-frame work beyond the P check.
    /// </summary>
    [DefaultExecutionOrder(-40)]
    public class OzCheatSpawner : MonoBehaviour
    {
        public static bool DevEnabled
        {
            get
            {
                // The testing overlay is available in every build, the release exe included (it is not listed in the controls).
                if (Application.isEditor) return PlayerPrefs.GetInt("oz_dev", 1) != 0;
                if (ScriptedTake) return false;
                if (PlayerPrefs.GetInt("oz_dev", 1) != 0) return true;
                foreach (var a in System.Environment.GetCommandLineArgs()) if (a == "-ozdev") return true;
                return false;
            }
        }
        /// <summary>
        /// builds only) never installs the overlay, so nothing typed or clicked on the game window can spawn units into the take.
        /// </summary>
        static bool ScriptedTake
        {
            get
            {
                foreach (var a in System.Environment.GetCommandLineArgs())
                    if (a.Equals("-ozend", System.StringComparison.OrdinalIgnoreCase) || a.Equals("-ozkill", System.StringComparison.OrdinalIgnoreCase)) return true;
                return false;
            }
        }
        /// <summary>PlayerInput: P is ours while the dev flag is on.</summary>
        public static bool OwnsP => I && I.isActiveAndEnabled;
        public static OzCheatSpawner I { get; private set; }
        public static bool Visible => I && I.shown;

        static readonly string[] Robots = { "grunt", "psycho", "tough", "sniper", "pyro", "laser", "electrobot" };
        static readonly string[] Vehicles = { "jeep", "light", "medium", "heavy", "apc", "missile_launcher", "crane" };
        const float Cell = 60f, Gap = 14f, Pad = 12f, Head = 22f, LabelH = 13f; const int Cols = 4;

        struct Entry { public RectTransform rt; public string type; public Team team; public Texture tex; }
        readonly List<Entry> entries = new List<Entry>();
        readonly List<RectTransform> panels = new List<RectTransform>();
        OpenZHud hud; ZGame game; bool built, shown;
        Entry? drag; RawImage ghost; RectTransform ghostRt; Image hoverFrame;

        static bool hooked;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoInstall()
        {
            if (!hooked) { hooked = true; UnityEngine.SceneManagement.SceneManager.sceneLoaded += (s, m) => TryInstall(); }
            TryInstall();
        }

        static void TryInstall()
        {
            if (!DevEnabled) return;
            var g = FindFirstObjectByType<ZGame>();
            if (g && !g.GetComponent<OzCheatSpawner>()) g.gameObject.AddComponent<OzCheatSpawner>();
        }

        void Awake() { I = this; }
        void OnDestroy() { if (I == this) I = null; }

        void Update()
        {
            if (!hud) hud = OpenZHud.I;
            if (!game) game = ZGame.I;
            if (!hud || !hud.Root || !game || game.World == null) return;
            var kb = Keyboard.current;
            if (kb != null && kb.pKey.wasPressedThisFrame && !hud.Modal) Show(!shown);
            if (!shown) return;
            if (!hud.hudVisible || hud.Modal) { Show(false); return; }
            Interact();
        }

        public void Show(bool on)
        {
            if (on && !built) Build();
            shown = on;
            foreach (var p in panels) p.gameObject.SetActive(on);
            if (!on) EndDrag();
            AudioDirector.UI(on ? "ui_confirm" : "ui_cancel");
        }

        // ------------------------------------------------------------------ layout
        void Build()
        {
            built = true;
            panels.Add(Column("CheatBlue", Team.Blue, "BLUE · CPU", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(14f, -64f)));
            panels.Add(Column("CheatRed", Team.Red, "RED · PLAYER", new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-14f, -64f)));
            panels.Add(NeutralRow());
            ghostRt = Hud.Node("CheatGhost", hud.Root);
            ghostRt.Anchor(Vector2.zero, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(Cell, Cell));
            ghost = ghostRt.gameObject.AddComponent<RawImage>(); ghost.raycastTarget = false; ghost.color = new Color(1f, 1f, 1f, 0.85f);
            ghostRt.gameObject.SetActive(false);
            panels.Add(ghostRt); ghostRt.gameObject.SetActive(false);
        }

        RectTransform Column(string name, Team team, string title, Vector2 anchor, Vector2 pivot, Vector2 pos)
        {
            float w = Pad * 2f + Cols * Cell + (Cols - 1) * Gap;
            int rows = Rows(Robots.Length) + Rows(Vehicles.Length) + Rows(ZBuildLists.Cannons.Length);
            float h = Pad + Head + 3 * (Head - 4f) + rows * (Cell + LabelH + Gap) + Pad * 0.5f;
            var p = Hud.Node(name, hud.Root); p.Anchor(anchor, pivot, pos, new Vector2(w, h));
            Hud.Plate(p, "md", Head + 6f, OzUi.Mark(team));
            Hud.Label(p, "Title", title, 12, OzUi.Ink, 0.16f).At(Pad, 8);
            float y = Pad + Head + 4f;
            y = Section(p, "ROBOT SQUADS", Robots, team, y);
            y = Section(p, "VEHICLES", Vehicles, team, y);
            Section(p, "GUNS", ZBuildLists.Cannons, team, y);
            hud.AddBlocker(p);
            return p;
        }

        RectTransform NeutralRow()
        {
            int n = Vehicles.Length + ZBuildLists.Cannons.Length;
            float w = Pad * 2f + n * Cell + (n - 1) * Gap, h = Pad + Head + Cell + LabelH + Pad * 0.5f;
            var p = Hud.Node("CheatNeutral", hud.Root); p.Anchor(new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -86f), new Vector2(w, h));
            Hud.Plate(p, "md", Head + 6f, OzUi.Neutral);
            Hud.Label(p, "Title", "EMPTY · NEUTRAL (any robot can capture)", 12, OzUi.Ink, 0.16f).At(Pad, 8);
            int i = 0;
            foreach (var t in Vehicles) Tile(p, t, Team.Neutral, Pad + i++ * (Cell + Gap), Pad + Head);
            foreach (var t in ZBuildLists.Cannons) Tile(p, t, Team.Neutral, Pad + i++ * (Cell + Gap), Pad + Head);
            hud.AddBlocker(p);
            return p;
        }

        static int Rows(int n) => (n + Cols - 1) / Cols;

        float Section(RectTransform p, string title, string[] types, Team team, float y)
        {
            Hud.Label(p, "H_" + title, title, 10, OzUi.A(OzUi.Ink, 0.7f), 0.18f).At(Pad, y);
            y += Head - 4f;
            for (int i = 0; i < types.Length; i++)
                Tile(p, types[i], team, Pad + (i % Cols) * (Cell + Gap), y + (i / Cols) * (Cell + LabelH + Gap));
            return y + Rows(types.Length) * (Cell + LabelH + Gap);
        }

        void Tile(RectTransform p, string type, Team team, float x, float y)
        {
            var cell = Hud.Node("Cell_" + type, p).TL(x, y, Cell, Cell + LabelH);
            var bg = Hud.Img(cell, "Bg", OzUi.A(OzUi.GM950, 0.75f)); bg.rectTransform.TL(0, 0, Cell, Cell);
            Hud.Border(bg.transform, OzUi.A(team == Team.Neutral ? OzUi.Neutral : OzUi.Mark(team), 0.55f));
            var tex = team == Team.Neutral ? UnitArt.Icon(type, Team.Red) : ((Texture)UnitArt.VehiclePortrait(type, team) ?? UnitArt.Icon(type, team));   // crewed = team portrait
            var raw = Hud.Node("Art", cell).TL(3, 3, Cell - 6, Cell - 6).gameObject.AddComponent<RawImage>();
            raw.raycastTarget = false; raw.texture = tex;
            raw.color = team == Team.Neutral ? new Color(0.62f, 0.62f, 0.62f, 1f) : Color.white;      // grey = empty
            if (!tex) Hud.Label(cell, "Code", UnitInfo.Code(type), 12, OzUi.Ink).At(8, 22);
            string label = type == "electrobot" ? "ELECTRO" : UnitInfo.Short(type);   // fits the 60 px cell
            if (team != Team.Neutral && ZBuildLists.IsRobot(type)) label += " ×" + (game.World.S.Unit(type)?.group_amount ?? 1);
            Hud.Label(cell, "Name", label, 9, OzUi.A(OzUi.Ink, 0.85f), 0.08f).At(1, Cell + 1);
            entries.Add(new Entry { rt = cell, type = type, team = team, tex = tex });
        }

        // ------------------------------------------------------------------ drag + drop
        void Interact()
        {
            var m = Mouse.current; if (m == null) return;
            Vector2 mp = m.position.ReadValue();
            if (drag == null)
            {
                if (!m.leftButton.wasPressedThisFrame) { V3Hover(mp); return; }                               // sound_v3
                foreach (var e in entries)
                    if (OpenZHud.Contains(e.rt, mp))
                    {
                        drag = e; ghost.texture = e.tex; ghost.color = e.team == Team.Neutral ? new Color(0.62f, 0.62f, 0.62f, 0.85f) : new Color(1f, 1f, 1f, 0.85f);
                        ghostRt.gameObject.SetActive(true); ghostRt.SetAsLastSibling(); AudioDirector.UI("ui_drag_pick");   // sound_v3
                        break;
                    }
                if (drag == null) return;
            }
            ghostRt.anchoredPosition = hud.ScreenToCanvas(mp);
            if (m.rightButton.wasPressedThisFrame || (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)) { EndDrag(); AudioDirector.UI("ui_cancel"); return; }
            if (!m.leftButton.wasReleasedThisFrame) return;
            var d = drag.Value; EndDrag();
            if (hud.IsOverUi(mp)) { AudioDirector.UI("ui_drag_cancel"); return; }   // sound_v3                                                           // dropped back on a panel: nothing
            var cam = Camera.main;
            if (!cam || !game.GroundPoint(cam.ScreenPointToRay(mp), out var gp)) { AudioDirector.UI("ui_drag_cancel"); return; }   // sound_v3
            Spawn(d.type, d.team, ZGame.WorldToPx(gp));
        }

        int v3Hover = -1;
        void V3Hover(Vector2 mp)
        {   // sound_v3: the tiles tick like HUD buttons
            int hi = -1; for (int k = 0; k < entries.Count; k++) if (OpenZHud.Contains(entries[k].rt, mp)) { hi = k; break; }
            if (hi != v3Hover) { v3Hover = hi; if (hi >= 0) AudioDirector.UI("ui_hover"); }
        }

        void EndDrag() { drag = null; if (ghostRt) ghostRt.gameObject.SetActive(false); }

        void Spawn(string type, Team team, Vector2Int px)
        {
            var W = game.World;
            bool robot = ZBuildLists.IsRobot(type);
            int foot = robot ? 1 : 2;
            if (!FreeTile(W, px.x >> 4, px.y >> 4, foot, robot, out int tx, out int ty)) { hud.Toast("NO ROOM HERE", HudStyle.Danger); AudioDirector.UI("ui_error"); return; }
            ZObject o;
            if (robot)
            {
                o = W.CreateRobotGroup(type, tx * 16, ty * 16, team);
                game.SpawnUnitView(o); foreach (var mm in o.Minions) game.SpawnUnitView(mm);
            }
            else
            {
                o = W.CreateObject(ZBuildLists.IsCannon(type) ? "cannon" : "vehicle", type, tx * 16, ty * 16, team);
                game.SpawnUnitView(o);
            }
            Debug.Log($"[OzCheat] spawned {team} {type} id {o?.Id} at tile {tx},{ty}");
            { var wp = ZGame.PxToWorld(tx * 16 + 8, ty * 16 + 8); wp.y = game.GroundY(wp.x, wp.z); OzSoundV3.CheatDrop(type, robot, ZBuildLists.IsCannon(type), wp); }   // sound_v3
            AudioDirector.UI("ui_confirm");
        }

        // ------------------------------------------------------------------ self-test (real input events, one step per frame)
        public static string TestReport = "idle";
        public static void SelfTest(Vector2 dropScreen) { if (I) { TestReport = "running"; I.StartCoroutine(I.TestRoutine(dropScreen)); } }

        System.Collections.IEnumerator TestRoutine(Vector2 drop)
        {
            var log = new System.Text.StringBuilder();
            System.Collections.IEnumerator F(int n) { for (int i = 0; i < n; i++) yield return null; }
            void MouseAt(Vector2 p, bool left) { var st = new UnityEngine.InputSystem.LowLevel.MouseState { position = p }; if (left) st = st.WithButton(UnityEngine.InputSystem.LowLevel.MouseButton.Left); InputSystem.QueueStateEvent(Mouse.current, st); }
            int before = game.World.Objects.Count;
            InputSystem.QueueStateEvent(Keyboard.current, new UnityEngine.InputSystem.LowLevel.KeyboardState(Key.P)); yield return F(2);
            InputSystem.QueueStateEvent(Keyboard.current, new UnityEngine.InputSystem.LowLevel.KeyboardState()); yield return F(2);
            log.Append($"P shows: {shown}; placing={(FindFirstObjectByType<PlayerInput>()?.Placing != null)}. ");
            int ok = 0;
            foreach (var pick in new[] { ("tough", Team.Red), ("heavy", Team.Blue), ("gatling", Team.Neutral) })
            {
                var e = entries.Find(x => x.type == pick.Item1 && x.team == pick.Item2);
                Vector2 c = RectTransformUtility.WorldToScreenPoint(null, e.rt.TransformPoint(e.rt.rect.center));
                int n0 = game.World.Objects.Count;
                MouseAt(c, false); yield return F(2); MouseAt(c, true); yield return F(2);
                for (int k = 1; k <= 6; k++) { MouseAt(Vector2.Lerp(c, drop, k / 6f), true); yield return F(1); }
                MouseAt(drop, false); yield return F(3);
                int made = game.World.Objects.Count - n0;
                log.Append($"{pick.Item2} {pick.Item1}: +{made} objects. "); if (made > 0) ok++;
                drop += new Vector2(90f, 0f);
            }
            InputSystem.QueueStateEvent(Keyboard.current, new UnityEngine.InputSystem.LowLevel.KeyboardState(Key.P)); yield return F(2);
            InputSystem.QueueStateEvent(Keyboard.current, new UnityEngine.InputSystem.LowLevel.KeyboardState()); yield return F(2);
            log.Append($"P again hides: {!shown}; over UI at the column when hidden: {hud.IsOverUi(new Vector2(40f, Screen.height * 0.5f))}.");
            TestReport = (ok == 3 && !shown ? "PASS " : "FAIL ") + log;
            Debug.Log("[OzCheat] self-test " + TestReport);
        }

        /// <summary>The dropped tile, else the nearest tile (spiral, radius 6) whose foot x foot box is passable for its kind.</summary>
        internal static bool FreeTile(ZWorld W, int cx, int cy, int foot, bool robot, out int tx, out int ty)
        {
            for (int r = 0; r <= 6; r++)
                for (int dy = -r; dy <= r; dy++)
                    for (int dx = -r; dx <= r; dx++)
                    {
                        if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy)) != r) continue;
                        int x = cx + dx, y = cy + dy; bool ok = true;
                        for (int j = 0; j < foot && ok; j++) for (int i = 0; i < foot && ok; i++) ok = W.Path.TilePassable(x + i, y + j, robot);
                        if (ok) { tx = x; ty = y; return true; }
                    }
            tx = cx; ty = cy; return false;
        }
    }
}
