using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// crews_v2 proof that the gunner comes out on EXIT: in Play, for the gun, gatling,
    /// howitzer and missile cannon, a crewed cannon of the player is placed on free ground near the player's fort (as the
    /// fort's store would, zserver_events.cpp:557-598: a Grunt driver), the camera flies to it, its gunner mans it, then the
    /// exact player command runs: PlayerInput.EjectUnit (the right-click on the single selected ejectable unit,
    /// zplayer_events.cpp:397-424 -> EJECT_VEHICLE -> ZWorld.Eject = zserver_events.cpp:631-731). It checks the sim (Ejected
    /// event, gun neutral and empty, a new Grunt squad of the player), the view (BoardingView ran the exit: the seated gunner
    /// got up and walked to the new robot, whose own view is visible at the end) and that a fort tower cannon refuses (not
    /// Started by OpenZ/Checks/Cannon exit (4 types) in Play. The placed test guns are removed afterwards.
    /// </summary>
    [DefaultExecutionOrder(-10)]   // the command runs in Update like PlayerInput's click: after the sim tick, before BoardingView (70) reads the events
    public class CannonExitCheck : MonoBehaviour
    {
        ZObject ejectNow; bool ejectResult; int ejectEvents; ZObject ejectLeader; bool ejectDone;
        static readonly string[] Types = { "gun", "gatling", "howitzer", "missile_cannon" };
        ZGame G; ZWorld W; PlayerInput input; RtsCamera rts;
        readonly StringBuilder md = new StringBuilder();
        int pass, fail;
        public static bool Running { get; private set; }

        public static void Begin()
        {
            if (Running || !ZGame.I || ZGame.I.World == null) { Debug.LogWarning("[OpenZ] Cannon exit check: enter Play on L02 or L17 first"); return; }
            ZGame.I.gameObject.AddComponent<CannonExitCheck>();
        }

        void Start() { Running = true; StartCoroutine(Run()); }
        void OnDestroy() { Running = false; }

        IEnumerator Run()
        {
            G = ZGame.I; W = G.World; input = FindFirstObjectByType<PlayerInput>(); rts = FindFirstObjectByType<RtsCamera>();
            var me = G.player;
            md.AppendLine($"# Cannon exit check {G.level} ({System.DateTime.Now:yyyy-MM-dd HH:mm})").AppendLine()
              .AppendLine("Player command = PlayerInput.EjectUnit (right-click on the single selected ejectable cannon, zplayer_events.cpp:397-424).").AppendLine()
              .AppendLine("| gun | sim: Ejected, neutral, empty | new squad (player) | view: exit ghost ran | robot visible at the end | result |")
              .AppendLine("|---|---|---|---|---|---|");
            if (!input) { md.AppendLine("no PlayerInput in the scene"); Write(); yield break; }
            var fort = W.Objects.FirstOrDefault(o => o.IsFort && o.Owner == me && !o.Destroyed);
            if (fort == null) { md.AppendLine("no player fort"); Write(); yield break; }
            var used = new List<RectInt>();
            foreach (var type in Types)
            {
                if (!FreeSpot(fort, used, out int tx, out int ty)) { Row(type, "no free 2x2 spot near the fort", "-", "-", "-", false); continue; }
                used.Add(new RectInt(tx - 1, ty - 1, 4, 4));
                var c = W.CreateObject("cannon", type, tx * 16, ty * 16, me);
                W.Emit(ZEventType.Created, c, fort);                                                       // CombatView spawns the view (as PlaceCannon)
                yield return null;
                var view = G.Views.TryGetValue(c.Id, out var v) ? v : null;
                if (rts && view) { rts.FlyTo(view.transform.position); rts.ZoomTo(26f); }
                yield return new WaitForSeconds(2.5f);                                                     // flight + the gunner mans it
                input.Selection.Clear(); input.Selection.Add(c);
                int exitsBefore = BoardingView.I ? BoardingView.I.ExitsStarted : 0, doneBefore = BoardingView.I ? BoardingView.I.ExitsDone : 0;
                ejectDone = false; ejectNow = c;
                while (!ejectDone) yield return null;
                bool ok = ejectResult; int ejected = ejectEvents; var leader = ejectLeader;
                yield return new WaitForSeconds(0.35f);
                Shot(type);
                yield return new WaitForSeconds(1.6f);
                bool simOk = ok && ejected > 0 && c.Owner == Team.Neutral && c.Drivers.Count == 0;
                bool squad = leader != null && leader.Owner == me && leader.Kind == ZKind.Robot && leader.Type == "grunt";
                bool ghost = BoardingView.I && BoardingView.I.ExitsStarted > exitsBefore && BoardingView.I.ExitsDone > doneBefore;
                bool visible = false;
                if (leader != null && G.Views.TryGetValue(leader.Id, out var lv) && lv && lv.gameObject.activeInHierarchy)
                    visible = lv.GetComponentsInChildren<Renderer>().Any(r => r.enabled);                    // BoardingView re-enabled them (ShowLive)
                Row(type, simOk ? "yes" : $"NO (command {ok}, events {ejected}, owner {c.Owner}, drivers {c.Drivers.Count})", squad ? $"{leader.Type} x{1 + leader.Minions.Count}" : "NO",
                    ghost ? "yes" : "NO", visible ? "yes" : "NO", simOk && squad && ghost && visible);
                // tidy up: the test gun and its crew leave the map
                Kill(c); Kill(leader);
                yield return new WaitForSeconds(0.5f);
            }
            // a fort tower cannon must refuse (Zod: not ejectable)
            var tower = W.Objects.FirstOrDefault(o => o.Kind == ZKind.Cannon && !o.Ejectable && !o.Destroyed && o.Owner == me);
            if (tower != null)
            {
                ejectDone = false; ejectNow = tower;
                while (!ejectDone) yield return null;
                bool refused = !ejectResult && tower.Owner == me && tower.Drivers.Count > 0;
                md.AppendLine().AppendLine($"Fort tower cannon {tower.Type} #{tower.Id}: eject {(refused ? "refused (PASS, Zod: not ejectable)" : "NOT refused (FAIL)")}");
                if (refused) pass++; else fail++;
            }
            else md.AppendLine().AppendLine("No player tower cannon on this level (L02 red fort has gun_49_72 only when red; L17 red fort_front: 4 howitzers).");
            Write();
            Destroy(this);
        }

        void Update()
        {
            if (ejectNow == null) return;
            var c = ejectNow; ejectNow = null;
            var before = new HashSet<int>(W.Objects.Select(o => o.Id));
            ejectResult = input.EjectUnit(c); ejectEvents = 0; ejectLeader = null;
            foreach (var e in W.Events) if (e.type == ZEventType.Ejected && e.objId == c.Id) { ejectEvents++; ejectLeader = W.GetAny(e.otherId); }
            if (ejectLeader == null) ejectLeader = W.Objects.FirstOrDefault(o => !before.Contains(o.Id) && o.Kind == ZKind.Robot && !o.IsMinion);
            ejectDone = true;
        }

        void Kill(ZObject o)                                                                               // as GateBattery.Kill
        {
            if (o == null || o.Destroyed) return;
            foreach (var m in new List<ZObject>(o.Minions)) if (!m.Destroyed) { m.DamageHealth(m.Health + 1); W.UpdateObjectHealth(m, -1); }
            o.DamageHealth(o.Health + 1); W.UpdateObjectHealth(o, -1);
        }

        void Row(string type, string sim, string squad, string ghost, string vis, bool ok)
        {
            md.AppendLine($"| {type} | {sim} | {squad} | {ghost} | {vis} | {(ok ? "PASS" : "FAIL")} |");
            if (ok) pass++; else fail++;
            Debug.Log($"[OpenZ] Cannon exit check {type}: {(ok ? "PASS" : "FAIL")} (sim {sim}, squad {squad}, exit ghost {ghost}, visible {vis})");
        }

        bool FreeSpot(ZObject fort, List<RectInt> used, out int tx, out int ty)
        {
            int fx = fort.CenterX / 16, fy = fort.CenterY / 16;
            int ftx0 = fort.LocX / 16 - 2, fty0 = fort.LocY / 16 - 2, ftx1 = (fort.LocX + fort.WidthPix) / 16 + 2, fty1 = (fort.LocY + fort.HeightPix) / 16 + 2;
            var cand = new List<(int d, int x, int y)>();
            for (int dy = -16; dy <= 16; dy++) for (int dx = -16; dx <= 16; dx++) cand.Add((dx * dx + dy * dy, fx + dx, fy + dy));
            foreach (var (_, x, y) in cand.OrderBy(k => k.d))
            {
                if (x < 2 || y < 2 || x + 3 >= W.Map.width || y + 3 >= W.Map.height) continue;
                if (x + 2 > ftx0 && x < ftx1 && y + 2 > fty0 && y < fty1) continue;
                bool ok = true;
                for (int i = 0; i < 2 && ok; i++) for (int j = 0; j < 2 && ok; j++)
                    if (!W.Path.TilePassable(x + i, y + j, false) || W.Map.ClassAt(x + i, y + j) == ZClass.Water || W.Map.ClassAt(x + i, y + j) == ZClass.DeepWater) ok = false;
                if (!ok || used.Any(r => r.Overlaps(new RectInt(x, y, 2, 2)))) continue;
                var probe = new ZObject { WidthPix = 64, HeightPix = 64 }; probe.SetCords(x * 16 - 16, y * 16 - 16);
                if (W.Objects.Any(o => !o.Destroyed && o.Type != "flag" && probe.IntersectsObject(o))) continue;
                tx = x; ty = y; return true;
            }
            tx = ty = -1; return false;
        }

        string Dir => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "..", "..", "work", "records", "qc"));
        void Shot(string type) { Directory.CreateDirectory(Dir); ScreenCapture.CaptureScreenshot(Path.Combine(Dir, $"cannon_exit_{G.level}_{type}.png")); }
        void Write()
        {
            md.AppendLine().AppendLine($"**{pass} PASS, {fail} FAIL.** Screenshots (0.35 s into the exit): cannon_exit_{G.level}_<type>.png");
            Directory.CreateDirectory(Dir);
            var p = Path.Combine(Dir, $"cannon_exit_check_{G.level}.md");
            File.WriteAllText(p, md.ToString());
            Debug.Log($"[OpenZ] Cannon exit check {G.level}: {pass} PASS, {fail} FAIL -> work/records/qc/{Path.GetFileName(p)}");
        }
    }
}
