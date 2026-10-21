using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// The proof pictures of "lines only on real blind sectors" (OzBlindSectorsCore.RealSectors.cs; Play mode, Level 16 or Level 2 started
    /// from the main menu; menu OpenZ/V1.1/Blind Sectors: Lines Only On Real Sectors). Level 16: the howitzers at (63,42) and (39,36) (in the
    /// jungle among trees: today a fan of lines), the red fort's own howitzer at (48,109), the guns by rocks at (16,84) and (91,36), the red
    /// fort's gun at (54,109) (in the open: no blind ground in its range). Level 2: the gatling by the rocks at (63,39) and the gatling in the
    /// open at (56,64). An empty gun is crewed first (the nearest robot of the player's team gets the rules' own enter order); a gun the other
    /// team holds cannot stay selected and is left out with a RETAKE note. Each gun is selected at the standard zoom (70 m, the normal RTS
    /// camera on the gun, full screen, by day), ~1 s after the selection (the ring's hold). Each shot writes its counted line: the sectors,
    /// how many are real, the lines drawn (the core as the ring makes it), the lines on slivers (must be 0), the lines today (the filter off)
    /// The clock is frozen for the pictures and put back after. Restart the level after: the crews stay in the guns.
    /// </summary>
    public class OzNoLinesProof : MonoBehaviour
    {
        static string Milestones => Path.GetFullPath(Path.Combine(Application.dataPath, "../../../../work/records/milestones"));
        static string Dir => Path.Combine(Milestones, "k427_no_lines_without_blind");
        public static float StandardHeight = 70f, DayTime = 0.08f, HoldWait = 1.0f;

        struct View { public string level, name; public int tx, ty; }
        static readonly View[] Views =
        {
            new View { level = "L16", name = "L16_howitzer_63_42", tx = 63, ty = 42 },
            new View { level = "L16", name = "L16_howitzer_39_36", tx = 39, ty = 36 },
            new View { level = "L16", name = "L16_red_fort_howitzer_48_109", tx = 48, ty = 109 },
            new View { level = "L16", name = "L16_gun_by_rocks_16_84", tx = 16, ty = 84 },
            new View { level = "L16", name = "L16_gun_by_rocks_91_36", tx = 91, ty = 36 },
            new View { level = "L16", name = "L16_red_fort_gun_open_54_109", tx = 54, ty = 109 },
            new View { level = "L02", name = "L02_gatling_by_rocks_63_39", tx = 63, ty = 39 },
            new View { level = "L02", name = "L02_gatling_open_56_64", tx = 56, ty = 64 },
        };

        readonly StringBuilder log = new StringBuilder();
        ZGame g; ZWorld W; RtsCamera cam; DayNight dn; PlayerInput input; AttackRangeRings rr;
        int bad;

        public static void Run()
        {
            if (Object.FindFirstObjectByType<OzNoLinesProof>()) { Debug.LogWarning("[OpenZ] lines only on real sectors proof: already running"); return; }
            new GameObject("OzNoLinesProof").AddComponent<OzNoLinesProof>();
        }

        void Note(string s) { log.AppendLine(s); Debug.Log("[OpenZ] lines only on real sectors proof: " + s); }

        IEnumerator Start()
        {
            float wait = 0f;
            while ((!(g = ZGame.I) || g.World == null || !AttackRangeRings.I) && wait < 15f) { wait += Time.unscaledDeltaTime; yield return null; }
            rr = AttackRangeRings.I;
            if (!g || g.World == null || !rr) { Note("no running level (start Level 16 or Level 2 from the main menu)"); Destroy(gameObject); yield break; }
            W = g.World; cam = Object.FindFirstObjectByType<RtsCamera>(); dn = Object.FindFirstObjectByType<DayNight>(); input = rr.input ? rr.input : Object.FindFirstObjectByType<PlayerInput>();
            Directory.CreateDirectory(Dir);
            var probe = new OzBlindSectorsCore();
            Note($"level {g.level}, {Screen.width}x{Screen.height}, blind sectors {(rr.blindSectors ? "on" : "OFF")}; a real sector: at least {probe.RealMinDeg:0} deg round the gun, " +
                 $"{probe.RealMinWide / 16.0:0.#} tiles across on average, {probe.RealMinDeep / 16.0:0.#} tiles deep, {probe.RealMinArea / 256.0:0} cells; lines only on real sectors {(probe.LinesOnlyReal ? "ON" : "OFF")}; pictures to {Dir}");
            float t0 = dn ? dn.timeOfDay : 0f; bool run0 = dn && dn.running;
            if (dn) dn.running = false;
            float h0 = cam ? cam.height : StandardHeight; var f0 = cam ? cam.focus : Vector3.zero;
            int shots = 0;
            foreach (var v in Views)
            {
                if (v.level != g.level) continue;
                ZObject gun = null;
                foreach (var o in W.Objects) if (o.Kind == ZKind.Cannon && !o.Destroyed && (o.LocX >> 4) == v.tx && (o.LocY >> 4) == v.ty) gun = o;
                if (gun == null) { Note($"no gun at ({v.tx},{v.ty}): {v.name} left out"); continue; }
                if (gun.Owner == Team.Neutral || gun.AttackRadius <= 0) yield return Crew(gun);
                if (gun.Owner == Team.Neutral || gun.AttackRadius <= 0) { Note($"{v.name}: the gun could not be crewed: left out"); continue; }
                if (gun.Owner != g.player)
                {
                    Note($"RETAKE {v.name}: the other team holds this gun, so it cannot be selected; restart the level and run the menu at once");
                    continue;
                }
                if (cam) { var p = ZGame.PxToWorld(gun.CenterX, gun.CenterY); cam.focus = new Vector3(p.x, 0f, p.z); cam.SetHeightImmediate(StandardHeight); }
                SetTime(DayTime); yield return new WaitForSecondsRealtime(0.6f);
                yield return Shot(gun, $"k427_{v.name}_day"); shots++;
            }
            if (shots == 0) Note($"no view of this level ({g.level}): start Level 16 or Level 2 from the main menu");
            Note(AttackRangeRings.AuditBlind(out int cells).TrimEnd());
            Note($"audit: {cells} cells differ (must be 0); shots with a failed count: {bad} (must be 0)");
            if (input) input.Selection.Clear();
            if (dn) { SetTime(t0); dn.running = run0; }
            if (cam) { cam.focus = f0; cam.SetHeightImmediate(h0); }
            File.WriteAllText(Path.Combine(Dir, "proof_" + g.level + ".txt"), log.ToString());
            Debug.Log("[OpenZ] lines only on real sectors proof: done, " + shots + " pictures -> " + Dir);
            Destroy(gameObject);
        }

        void SetTime(float t) { if (dn) { dn.timeOfDay = t; dn.Apply(t); } }

        /// <summary>An empty gun gets a crew the player's way (as the clearer grey proof): the nearest robot of the player's team, put beside
        /// the gun when it is far, gets the order a right-click on the empty gun gives.</summary>
        IEnumerator Crew(ZObject gun)
        {
            ZObject best = null; double bd = double.MaxValue;
            foreach (var o in W.Objects)
            {
                if (!o.IsRobot || o.Destroyed || o.Owner != g.player || o.IsMinion) continue;
                double dx = o.CenterX - gun.CenterX, dy = o.CenterY - gun.CenterY, d = dx * dx + dy * dy + (o.Minions.Count > 0 ? 1e9 : 0);
                if (d < bd) { bd = d; best = o; }
            }
            if (best == null) { Note($"no robot of the player's team to crew the gun at ({gun.LocX >> 4},{gun.LocY >> 4})"); yield break; }
            int gtx = gun.CenterX >> 4, gty = gun.CenterY >> 4;
            if (bd > 48.0 * 48.0 && OzCheatSpawner.FreeTile(W, gtx, gty + 2, 1, true, out int fx, out int fy))
                best.SetCords(fx * 16 + 8 - (best.WidthPix >> 1), fy * 16 + 8 - (best.HeightPix >> 1));
            W.SendWaypoints(g.player, best, new[] { new ZWaypoint { mode = WpMode.Enter, refId = gun.Id, x = gun.CenterX, y = gun.CenterY, attackTo = false, playerGiven = true } });
            float t = 0f;
            while ((gun.Owner == Team.Neutral || gun.AttackRadius <= 0) && t < 20f) { t += Time.unscaledDeltaTime; yield return null; }
            Note($"the gun at ({gun.LocX >> 4},{gun.LocY >> 4}) {(gun.Owner != Team.Neutral ? "crewed" : "NOT crewed")} by the {best.Type} #{best.Id} after {t:0.0} s");
            yield return new WaitForSecondsRealtime(0.5f);
        }

        IEnumerator Shot(ZObject gun, string name)
        {
            if (input) { input.Selection.Clear(); input.Selection.Add(gun); }
            yield return null; yield return null;
            rr.ShowForSelection();
            yield return new WaitForSecondsRealtime(HoldWait);
            yield return new WaitForEndOfFrame();
            var tex = ScreenCapture.CaptureScreenshotAsTexture();
            int shown = rr.BlindShown;
            bool selected = input && input.Selection.Contains(gun);
            File.WriteAllBytes(Path.Combine(Dir, name + ".png"), tex.EncodeToPNG());
            Destroy(tex);
            // the counted line: the core as the ring makes it (the drawn ring's centre: the gun's model), and the same with the filter off (today)
            var vp = g.Views.TryGetValue(gun.Id, out var view) && view ? view.transform.position : g.CenterWorld(gun, false);
            double rx = vp.x * 8.0, ry = -vp.z * 8.0;
            int ex = W.Sight.ExemptOf(gun);
            var c = new OzBlindSectorsCore();
            c.Compute(W, gun.CenterX, gun.CenterY, gun.AttackRadius, gun.LosActive, ex, rx, ry);
            var t = new OzBlindSectorsCore { LinesOnlyReal = false };
            t.Compute(W, gun.CenterX, gun.CenterY, gun.AttackRadius, gun.LosActive, ex, rx, ry);
            int onSliver = 0, keptOfReal = 0, realToday = 0, slivers = 0;
            foreach (var s in c.Sectors) if (!s.real) slivers++;
            foreach (var ln in c.Lines) if (!OnReal(c, ln.th, ln.lo)) onSliver++;
            foreach (var ln in t.Lines)
            {
                if (!OnReal(c, ln.th, ln.lo)) continue;
                realToday++;
                foreach (var q in c.Lines) if (System.Math.Abs(q.th - ln.th) < 1e-9 && System.Math.Abs(q.lo - ln.lo) < 1e-6) { keptOfReal++; break; }
            }
            bool pass = onSliver == 0 && keptOfReal == realToday && (c.RealSectorCount > 0 || c.LineEdges == 0) && c.LineEdges <= t.LineEdges;
            if (!pass) bad++;
            Note($"{name}.png camera {(cam ? cam.height : 0f):0} m: {gun.Type} range {gun.AttackRadius} px, {(gun.LosActive ? "direct fire" : "fires over obstacles (rocks, trees, huts still stop it)")}; " +
                 $"blind sectors {c.Sectors.Count} ({c.RealSectorCount} real, {slivers} slivers); lines drawn {c.LineEdges} ({c.StepLines} at steps; 2 x real sectors = {2 * c.RealSectorCount}), " +
                 $"on slivers {onSliver} (must be 0); lines today {t.LineEdges}; lines of real sectors kept {keptOfReal} of {realToday}; marks {c.Marks.Count}; {(pass ? "PASS" : "FAIL")}" +
                 (shown == 0 && c.Verts.Count > 0 || !selected ? " -- RETAKE: no ring with sectors on screen (the gun is not selected)" : ""));
            foreach (var s in c.Sectors)
                Note($"    sector at {s.thA * 180.0 / System.Math.PI:0} deg, {s.wid * 180.0 / System.Math.PI:0.0} deg wide, blind from {s.near / 8.0:0.0} m, {s.deep / 8.0:0.0} m deep, {s.area / 64.0:0} m2, {s.wide / 8.0:0.0} m across: {(s.real ? "REAL" : "sliver (no lines)")}");
        }

        /// <summary>The line at angle th with its blind edge from lo bounds a real sector (the one just inside it on its blind side).</summary>
        static bool OnReal(OzBlindSectorsCore c, double th, double lo)
        {
            for (int sgn = -1; sgn <= 1; sgn += 2)
            {
                double a = th + sgn * 1e-4, x = c.ApexX + System.Math.Cos(a) * (lo + 6), y = c.ApexY + System.Math.Sin(a) * (lo + 6);
                if (!c.BlindPoint(x, y)) continue;
                double aa = (a % (2 * System.Math.PI) + 2 * System.Math.PI) % (2 * System.Math.PI);
                for (int i = 0; i < c.Sectors.Count; i++)
                {
                    var s = c.Sectors[i];
                    double d = ((aa - s.thA) % (2 * System.Math.PI) + 2 * System.Math.PI) % (2 * System.Math.PI);
                    if (d <= s.wid + 1e-6) return s.real;
                }
            }
            return false;
        }
    }
}
