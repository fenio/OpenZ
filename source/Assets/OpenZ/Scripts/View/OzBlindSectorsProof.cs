using System.Collections;
using System.IO;
using System.Text;
using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// The proof pictures of the blind sectors (Play mode, a Level 16 started from the main menu; menu OpenZ/V1.1/Blind Sectors:
    /// Proof Pictures): the red fort's tower guns selected one after the other at the standard zoom (70 m, the normal RTS camera,
    /// full screen), by day and by night, ~1 s after the selection (the ring's hold); then the tree that opens most of the tower
    /// howitzer's blind cells is felled by the rules' own death path and the howitzer is selected again by day. The same for the
    /// blue fort's tower howitzer on the east tower (an enemy gun, selected to see its ring: its blind sectors are much larger, the
    /// blue fort stands in the trees). Each step writes its sectors (OzBlindSectorsCore) and the audit lines to proof.txt. The
    /// </summary>
    public class OzBlindSectorsProof : MonoBehaviour
    {
        static string Dir => Path.GetFullPath(Path.Combine(Application.dataPath, "../../../../work/records/milestones/k333_blind_sectors"));
        /// <summary>The tree the headless check found for the red fort's tower howitzer on Level 16 (it opens 4 of its 8 blind cells).</summary>
        public static Vector2Int TreeTile = new Vector2Int(54, 101);
        /// <summary>The same for the blue fort's tower howitzer at (54,9): the tree at (57,7) opens 23 of its 107 blind cells.</summary>
        public static Vector2Int BlueGun = new Vector2Int(54, 9), BlueTree = new Vector2Int(57, 7);
        public static float StandardHeight = 70f, DayTime = 0.08f, NightTime = 0.62f;

        readonly StringBuilder log = new StringBuilder();
        ZGame g; ZWorld W; RtsCamera cam; DayNight dn; PlayerInput input; AttackRangeRings rr;

        public static void Run()
        {
            if (Object.FindFirstObjectByType<OzBlindSectorsProof>()) { Debug.LogWarning("[OpenZ] blind sectors proof: already running"); return; }
            new GameObject("OzBlindSectorsProof").AddComponent<OzBlindSectorsProof>();
        }

        void Note(string s) { log.AppendLine(s); Debug.Log("[OpenZ] blind sectors proof: " + s); }

        IEnumerator Start()
        {
            float wait = 0f;
            while ((!(g = ZGame.I) || g.World == null || !AttackRangeRings.I) && wait < 15f) { wait += Time.unscaledDeltaTime; yield return null; }
            rr = AttackRangeRings.I;
            if (!g || g.World == null || !rr) { Note("no running level (start Level 16 from the main menu)"); Destroy(gameObject); yield break; }
            W = g.World; cam = Object.FindFirstObjectByType<RtsCamera>(); dn = Object.FindFirstObjectByType<DayNight>(); input = rr.input ? rr.input : Object.FindFirstObjectByType<PlayerInput>();
            Directory.CreateDirectory(Dir);
            Note($"level {g.level} ({W.Map.file}), {Screen.width}x{Screen.height}, blind sectors {(rr.blindSectors ? "on" : "OFF")}, dark {rr.blindDarkDay:0.00} / {rr.blindDarkNight:0.00}, lines {rr.blindLineDay:0.00} / {rr.blindLineNight:0.00}");
            float t0 = dn ? dn.timeOfDay : 0f; bool run0 = dn && dn.running;
            if (dn) dn.running = false;
            float h0 = cam ? cam.height : StandardHeight; var f0 = cam ? cam.focus : Vector3.zero;

            // the red fort's tower guns
            var towers = new System.Collections.Generic.List<ZObject>();
            foreach (var o in W.Objects) if (o.Kind == ZKind.Cannon && !o.Destroyed && o.Owner == Team.Red && W.Sight.ExemptOf(o) > 0) towers.Add(o);
            if (towers.Count == 0) Note("no red fort tower gun on this level");
            ZObject fort = towers.Count > 0 ? W.Get(W.Sight.ExemptOf(towers[0])) : null;
            if (cam && fort != null) { var p = ZGame.PxToWorld(fort.CenterX, fort.CenterY - 40); cam.focus = new Vector3(p.x, 0f, p.z); cam.SetHeightImmediate(StandardHeight); }
            ZObject howitzer = null;
            foreach (var t in towers) if (!t.LosActive) howitzer = t;

            foreach (var (name, tod) in new[] { ("day", DayTime), ("night", NightTime) })
            {
                SetTime(tod);
                yield return new WaitForSecondsRealtime(0.6f);
                foreach (var t in towers) yield return Shot(t, $"L16_red_fort_{t.Type}_{t.LocX >> 4}_{t.LocY >> 4}_{name}");
            }
            // a tree felled in the howitzer's sectors, then the howitzer again by day
            if (howitzer != null) yield return Fell(howitzer, TreeTile, "L16_red_fort");
            // the blue fort's tower howitzer: by day, by night, a tree felled
            ZObject blue = null;
            foreach (var o in W.Objects) if (o.Kind == ZKind.Cannon && !o.Destroyed && (o.LocX >> 4) == BlueGun.x && (o.LocY >> 4) == BlueGun.y) blue = o;
            if (blue == null || blue.AttackRadius <= 0) Note($"no crewed gun at ({BlueGun.x},{BlueGun.y}): the blue fort step is left out");
            else
            {
                var bf = W.Get(W.Sight.ExemptOf(blue));
                if (cam) { var p = ZGame.PxToWorld(bf != null ? bf.CenterX : blue.CenterX, (bf != null ? bf.CenterY : blue.CenterY) + 40); cam.focus = new Vector3(p.x, 0f, p.z); cam.SetHeightImmediate(StandardHeight); }
                foreach (var (name, tod) in new[] { ("day", DayTime), ("night", NightTime) })
                {
                    SetTime(tod);
                    yield return new WaitForSecondsRealtime(0.6f);
                    yield return Shot(blue, $"L16_blue_fort_{blue.Type}_{blue.LocX >> 4}_{blue.LocY >> 4}_{name}");
                }
                yield return Fell(blue, BlueTree, "L16_blue_fort");
            }
            Note(AttackRangeRings.AuditBlind(out int v).TrimEnd());
            Note($"audit: {v} cells differ (must be 0)");
            if (input) input.Selection.Clear();
            if (dn) { SetTime(t0); dn.running = run0; }
            if (cam) { cam.focus = f0; cam.SetHeightImmediate(h0); }
            File.WriteAllText(Path.Combine(Dir, "proof.txt"), log.ToString());
            Debug.Log("[OpenZ] blind sectors proof: done -> " + Dir);
            Destroy(gameObject);
        }

        void SetTime(float t) { if (dn) { dn.timeOfDay = t; dn.Apply(t); } }

        IEnumerator Fell(ZObject gun, Vector2Int tile, string prefix)
        {
            ZObject tree = null;
            foreach (var o in W.TreeTiles) if (!o.Destroyed && (o.LocX >> 4) == tile.x && (o.LocY >> 4) == tile.y && W.Get(o.Id) == o) tree = o;
            if (tree == null) { Note($"no standing tree at tile ({tile.x},{tile.y}): not felled"); yield break; }
            SetTime(DayTime);
            Note($"felling the tree #{tree.Id} at tile ({tile.x},{tile.y}) (SetHealth(0) + UpdateObjectHealth, the rules' own death path)");
            tree.SetHealth(0); W.UpdateObjectHealth(tree);
            yield return new WaitForSecondsRealtime(3f);
            yield return Shot(gun, $"{prefix}_{gun.Type}_{gun.LocX >> 4}_{gun.LocY >> 4}_after_tree_felled_day");
        }

        IEnumerator Shot(ZObject gun, string name)
        {
            if (input) { input.Selection.Clear(); input.Selection.Add(gun); }
            yield return null; yield return null;
            rr.ShowForSelection();                                                                          // the ring (and its sectors) start now
            yield return new WaitForSecondsRealtime(1.0f);                                                  // in the ring's hold
            yield return new WaitForEndOfFrame();
            var tex = ScreenCapture.CaptureScreenshotAsTexture();
            File.WriteAllBytes(Path.Combine(Dir, name + ".png"), tex.EncodeToPNG());
            var core = new OzBlindSectorsCore();
            var vp = g.Views.TryGetValue(gun.Id, out var view) && view ? view.transform.position : g.CenterWorld(gun, false);
            core.Compute(W, gun.CenterX, gun.CenterY, gun.AttackRadius, gun.LosActive, W.Sight.ExemptOf(gun), vp.x * 8.0, -vp.z * 8.0);
            Note($"{name}.png {tex.width}x{tex.height}, camera {(cam ? cam.height : 0f):0} m, night {NightGrade.NightK:0.00}: {gun.Type} range {gun.AttackRadius} px, " +
                 $"{(gun.LosActive ? "direct fire" : "fires over obstacles")}, {core.Runs.Count} sector(s), {core.LineEdges} side lines, blockers {core.BarrierTiles} barrier + {core.SightTiles} sight tiles, rings with sectors drawn {rr.BlindShown}");
            Destroy(tex);
        }
    }
}
