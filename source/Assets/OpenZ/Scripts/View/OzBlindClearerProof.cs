using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// The proof pictures of the clearer blind sectors (AttackRangeRings.BlindClearer.cs: the grey over the blind ground, the grey faint dots
    /// over the blind arcs; Play mode, Level 16 or Level 2 started from the main menu; menu OpenZ/V1.1/Blind Sectors: Clearer Grey Pictures).
    /// The same views as the earlier blind sector pictures: Level 16 the gun at (16,84) by day and by night and the gun at (91,36) by day; Level 2
    /// the gatlings at (31,7) (first: the other team crews it early), (63,39) by day and by night and (6,49) by day. An empty gun is crewed first (the
    /// nearest robot of the player's team gets the rules' own enter order); a gun the other team holds cannot stay selected and is left out
    /// with a note. Each gun is selected at the standard zoom (70 m, the normal RTS camera on the gun, full screen), ~1 s after the selection
    /// (the ring's hold). Each shot writes its sectors, lines, grey dots to proof.txt, and the measured look against a reference frame taken
    /// right after without the sectors (kept beside the picture as _ref_no_sectors.png, never in the owner's folder): the grey's darkening over
    /// the blind cells (median and darkest quarter), the ring's dots over the blind and the open arcs (contrast with the ground and redness,
    /// each as a share of the reference's). Then the audit of every gun of the level (the rules against the drawn cells). The clock is frozen
    /// Restart the level after: the crews stay in the guns.
    /// </summary>
    public class OzBlindClearerProof : MonoBehaviour
    {
        static string Milestones => Path.GetFullPath(Path.Combine(Application.dataPath, "../../../../work/records/milestones"));
        static string Dir => Path.Combine(Milestones, "k333c_blind_sectors_clearer");
        public static float StandardHeight = 70f, DayTime = 0.08f, NightTime = 0.62f, HoldWait = 1.0f;

        struct View { public string level, name; public int tx, ty; public bool day, night; }
        static readonly View[] Views =
        {
            new View { level = "L16", name = "L16_gun_16_84", tx = 16, ty = 84, day = true, night = true },
            new View { level = "L16", name = "L16_gun_91_36", tx = 91, ty = 36, day = true },
            new View { level = "L02", name = "L02_gatling_31_7", tx = 31, ty = 7, day = true },
            new View { level = "L02", name = "L02_gatling_63_39", tx = 63, ty = 39, day = true, night = true },
            new View { level = "L02", name = "L02_gatling_6_49", tx = 6, ty = 49, day = true },
        };

        readonly StringBuilder log = new StringBuilder();
        readonly List<string> dirs = new List<string>();
        ZGame g; ZWorld W; RtsCamera cam; DayNight dn; PlayerInput input; AttackRangeRings rr;

        public static void Run()
        {
            if (Object.FindFirstObjectByType<OzBlindClearerProof>()) { Debug.LogWarning("[OpenZ] clearer blind sectors proof: already running"); return; }
            new GameObject("OzBlindClearerProof").AddComponent<OzBlindClearerProof>();
        }

        void Note(string s) { log.AppendLine(s); Debug.Log("[OpenZ] clearer blind sectors proof: " + s); }

        IEnumerator Start()
        {
            float wait = 0f;
            while ((!(g = ZGame.I) || g.World == null || !AttackRangeRings.I) && wait < 15f) { wait += Time.unscaledDeltaTime; yield return null; }
            rr = AttackRangeRings.I;
            if (!g || g.World == null || !rr) { Note("no running level (start Level 16 or Level 2 from the main menu)"); Destroy(gameObject); yield break; }
            W = g.World; cam = Object.FindFirstObjectByType<RtsCamera>(); dn = Object.FindFirstObjectByType<DayNight>(); input = rr.input ? rr.input : Object.FindFirstObjectByType<PlayerInput>();
            Directory.CreateDirectory(Dir); dirs.Add(Dir);
            if (Directory.Exists(Milestones))
                foreach (var v in Directory.GetDirectories(Milestones, "v1_1_*"))
                {   // the owner's picture folder of run 28
                    var fo = Path.Combine(v, "for_owner");
                    if (!Directory.Exists(fo)) continue;
                    var p = Path.Combine(fo, "run28", "pictures");
                    Directory.CreateDirectory(p); dirs.Add(p);
                }
            Note($"level {g.level}, {Screen.width}x{Screen.height}, blind sectors {(rr.blindSectors ? "on" : "OFF")}; grey over the blind ground {rr.blindDarkDay:0.00} / {rr.blindDarkNight:0.00}, " +
                 $"grey ground {(rr.blindGreyGround ? "on" : "OFF")} (colour taken out {rr.blindGreyWarm:0.00} warm / {rr.blindGreyCool:0.00} green), grey dots {(rr.blindDotsGrey ? "on" : "OFF")} alpha {rr.blindDotAlphaDay:0.00} / {rr.blindDotAlphaNight:0.00}, lines {rr.blindLineDay:0.00} / {rr.blindLineNight:0.00}; pictures to {string.Join(" and ", dirs)}");
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
                {   // the selection keeps only the player's own units: an enemy gun's ring never shows
                    Note($"RETAKE {v.name}: the other team holds this gun (crewed by its own robots), so it cannot be selected; restart the level and run the menu at once");
                    continue;
                }
                if (cam) { var p = ZGame.PxToWorld(gun.CenterX, gun.CenterY); cam.focus = new Vector3(p.x, 0f, p.z); cam.SetHeightImmediate(StandardHeight); }
                if (v.day) { SetTime(DayTime); yield return new WaitForSecondsRealtime(0.6f); yield return Shot(gun, $"k333c_blind_{v.name}_day"); shots++; }
                if (v.night) { SetTime(NightTime); yield return new WaitForSecondsRealtime(0.6f); yield return Shot(gun, $"k333c_blind_{v.name}_night"); shots++; }
            }
            if (shots == 0) Note($"no view of this level ({g.level}): start Level 16 or Level 2 from the main menu");
            Note(AttackRangeRings.AuditBlind(out int bad).TrimEnd());
            Note($"audit: {bad} cells differ (must be 0)");
            if (input) input.Selection.Clear();
            if (dn) { SetTime(t0); dn.running = run0; }
            if (cam) { cam.focus = f0; cam.SetHeightImmediate(h0); }
            foreach (var d in dirs) File.WriteAllText(Path.Combine(d, d == Dir ? "proof_" + g.level + ".txt" : $"k333c_blind_proof_{g.level}.txt"), log.ToString());
            Debug.Log("[OpenZ] clearer blind sectors proof: done, " + shots + " pictures -> " + string.Join(" and ", dirs));
            Destroy(gameObject);
        }

        void SetTime(float t) { if (dn) { dn.timeOfDay = t; dn.Apply(t); } }

        /// <summary>An empty gun gets a crew the player's way: the nearest robot of the player's team (a lone one first) is put on a free tile
        /// beside the gun when it is far, and gets the order a right-click on the empty gun gives; it walks the last metres and gets in.</summary>
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
            rr.ShowForSelection();                                                                          // the ring (and its sectors) start now
            yield return new WaitForSecondsRealtime(HoldWait);                                              // in the ring's hold
            yield return new WaitForEndOfFrame();
            var tex = ScreenCapture.CaptureScreenshotAsTexture();
            int dimDots = rr.BlindDimDots, ringDots = rr.BlindRingDots, greyDots = rr.BlindGreyDots, shown = rr.BlindShown;
            bool greyOn = rr.BlindGreyGroundOn; string greyWhy = rr.BlindGreyGroundWhy;
            bool selected = input && input.Selection.Contains(gun);
            var png = tex.EncodeToPNG();
            foreach (var d in dirs) if (shown > 0 || d == Dir) File.WriteAllBytes(Path.Combine(d, name + ".png"), png);   // a shot without the ring never goes to the owner
            var core = new OzBlindSectorsCore();
            var vp = g.Views.TryGetValue(gun.Id, out var view) && view ? view.transform.position : g.CenterWorld(gun, false);
            core.Compute(W, gun.CenterX, gun.CenterY, gun.AttackRadius, gun.LosActive, W.Sight.ExemptOf(gun), vp.x * 8.0, -vp.z * 8.0);
            // the measurement's reference: the very next frame without the sectors (the ring's dots in the team colour all round), put back at
            // once. Only for the numbers below; the picture above is the one that counts (it goes to the owner's folder, this one stays here)
            rr.blindSectors = false;
            yield return null;
            yield return new WaitForEndOfFrame();
            var refTex = ScreenCapture.CaptureScreenshotAsTexture();
            rr.blindSectors = true;
            File.WriteAllBytes(Path.Combine(Dir, name + "_ref_no_sectors.png"), refTex.EncodeToPNG());
            Note($"{name}.png {tex.width}x{tex.height}, camera {(cam ? cam.height : 0f):0} m, night {NightGrade.NightK:0.00}: {gun.Type} range {gun.AttackRadius} px, " +
                 $"{(gun.LosActive ? "direct fire" : "fires over obstacles")}, {core.Runs.Count} sector(s), {core.LineEdges} side lines from the gun ({core.StepLines} at steps), " +
                 $"{core.Marks.Count} mark(s); ring dots over blind arcs {dimDots} of {ringDots} ({(ringDots > 0 ? 100f * dimDots / ringDots : 0f):0} %), grey dots drawn {greyDots}; grey ground shader {(greyOn ? "ON" : "OFF (" + greyWhy + ")")}; rings with sectors drawn {shown}" +
                 (shown == 0 || !selected ? " -- RETAKE: no ring with sectors on screen (the gun is not selected)" : ""));
            Note("  " + Measure(core, tex, refTex));
            Destroy(tex); Destroy(refTex);
        }

        static float Lum(Color32 c) => 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;
        static float Red(Color32 c) => c.r - 0.5f * (c.g + c.b);

        /// <summary>The picture against its reference without the sectors (the kit's previews measure their pictures the same way,
        /// tools/preview_k333c.py measure()): (1) the grey = the brightness change over the blind cells on screen (each cell centre's 3x3
        /// pixels): its median and its darkest quarter (blind cells hidden by a rock, a tree or a building do not change, so the darkest quarter
        /// is the grey where the ground shows), and the colour left there (the saturation as a share of the reference's: grey ground is low); (2) the ring's dots over the blind arcs and over the open arcs: their contrast with the ground
        /// under them (|mean 3x3 at the dot - the ground 1 m inside|) and their redness (red - (green + blue) / 2 above the ground's), each
        /// as a share of the same in the reference (1 = unchanged; a grey dot's redness is about 0). The HUD bands (top 12 %, bottom 36 % of the
        /// screen) and the eased arc ends are left out.</summary>
        string Measure(OzBlindSectorsCore core, Texture2D on, Texture2D off)
        {
            var c = Camera.main;
            if (!c || on.width != off.width || on.height != off.height) return "measure: no camera / sizes differ: left out";
            Color32[] a = on.GetPixels32(), b = off.GetPixels32();
            int w = on.width, h = on.height;
            float sx = (float)w / Mathf.Max(1, Screen.width), sy = (float)h / Mathf.Max(1, Screen.height);
            bool Pix(Vector3 world, out int px, out int py)
            {
                var s = c.WorldToScreenPoint(world); px = Mathf.RoundToInt(s.x * sx); py = Mathf.RoundToInt(s.y * sy);
                return s.z > 0f && px >= 3 && px < w - 3 && py >= Mathf.RoundToInt(0.36f * h) && py < Mathf.RoundToInt(0.88f * h);
            }
            float Mean(Color32[] p, System.Func<Color32, float> f, int x, int y) { float s = 0; for (int j = -1; j <= 1; j++) for (int i = -1; i <= 1; i++) s += f(p[(y + j) * w + x + i]); return s / 9f; }
            float Sat(Color32[] p, int x, int y)
            {
                float r = 0, gg = 0, bb = 0;
                for (int j = -1; j <= 1; j++) for (int i = -1; i <= 1; i++) { var q = p[(y + j) * w + x + i]; r += q.r; gg += q.g; bb += q.b; }
                float mx = Mathf.Max(r, Mathf.Max(gg, bb)), mn = Mathf.Min(r, Mathf.Min(gg, bb));
                return (mx - mn) / Mathf.Max(mx, 9f);
            }
            float Max(Color32[] p, int x, int y) { float m = 0; for (int j = -2; j <= 2; j++) for (int i = -2; i <= 2; i++) m = Mathf.Max(m, Lum(p[(y + j) * w + x + i])); return m; }
            // ---- (1) the grey over the blind cells
            var ratios = new List<float>(); var sats = new List<float>();
            int range = core.RangePx, ax = core.GunX >> 4, ay = core.GunY >> 4, rt = range / 16 + 2;
            for (int ty = ay - rt; ty <= ay + rt; ty++)
                for (int tx = ax - rt; tx <= ax + rt; tx++)
                {
                    int cx = tx * 16 + 8, cy = ty * 16 + 8;
                    double rx = cx - core.RingX, ry = cy - core.RingY;
                    if (System.Math.Sqrt(rx * rx + ry * ry) > range - 10 || core.MeshAlphaAt(cx, cy) < 0.9) continue;
                    float wx = cx / 8f, wz = -cy / 8f;
                    if (!Pix(new Vector3(wx, g.GroundY(wx, wz) + 0.1f, wz), out int px, out int py)) continue;
                    float l1 = Mean(a, Lum, px, py), l0 = Mean(b, Lum, px, py);
                    if (l0 >= 3f)
                    {
                        ratios.Add(l1 / l0);
                        // the colour left where the ground shows (darker by 5 % or more) and is coloured in the reference (not a grey road):
                        // saturation (max - min) / max of the 3x3 mean colour, as a share of the reference's (grey ground ~0.1-0.3, plain darkening ~1)
                        float s1 = Sat(a, px, py), s0 = Sat(b, px, py);
                        if (l1 / l0 <= 0.95f && s0 >= 0.06f) sats.Add(s1 / s0);
                    }
                }
            string fill = "grey: no blind cell on screen";
            if (ratios.Count > 0)
            {
                ratios.Sort();
                fill = $"grey over {ratios.Count} blind cells on screen: median {100f * (ratios[ratios.Count / 2] - 1f):+0.0;-0.0} %, darkest quarter {100f * (ratios[ratios.Count / 4] - 1f):+0.0;-0.0} %";
            }
            if (sats.Count >= 4) { sats.Sort(); fill += $", colour left {sats[sats.Count / 2]:0.00} ({sats.Count} cells)"; }
            else fill += $", colour left n/a ({sats.Count} cells: judge by eye)";
            // ---- (2) the ring's dots: bins of half a degree, blind (as BlindArcs: just inside the ring line) or open, only those whose class holds
            // 9 bins either side (clear of the eased arc ends) and that show a dot in the reference
            const int Bins = 720;
            var blind = new bool[Bins];
            double rr0 = range - 2.0;
            for (int i = 0; i < Bins; i++) { double t = i * 2.0 * System.Math.PI / Bins; blind[i] = core.BlindPoint(core.RingX + System.Math.Cos(t) * rr0, core.RingY - System.Math.Sin(t) * rr0); }
            float radius = (range + 3) / 8f, ccx = (float)(core.RingX / 8.0), ccz = (float)(-core.RingY / 8.0);
            double[] c0 = new double[2], c1 = new double[2], r0 = new double[2], r1 = new double[2]; int[] nb = new int[2];
            for (int i = 0; i < Bins; i++)
            {
                bool clean = true;
                for (int k = -9; k <= 9 && clean; k++) if (blind[(i + k + Bins) % Bins] != blind[i]) clean = false;
                if (!clean) continue;
                float t = i * 2f * Mathf.PI / Bins, cs = Mathf.Cos(t), sn = Mathf.Sin(t);
                float x1 = ccx + cs * radius, z1 = ccz + sn * radius, x0 = ccx + cs * (radius - 1.0f), z0 = ccz + sn * (radius - 1.0f);
                if (!Pix(new Vector3(x1, g.GroundY(x1, z1) + 0.12f, z1), out int px, out int py)) continue;
                if (!Pix(new Vector3(x0, g.GroundY(x0, z0) + 0.1f, z0), out int gx, out int gy)) continue;
                float ground = Mean(b, Lum, gx, gy);
                if (Max(b, px, py) - ground < 8f) continue;                                                  // no dot here in the reference (a gap between two dots)
                float redG = Mean(b, Red, gx, gy);
                int cl = blind[i] ? 1 : 0;
                c0[cl] += Mathf.Abs(Mean(b, Lum, px, py) - ground); c1[cl] += Mathf.Abs(Mean(a, Lum, px, py) - ground);
                r0[cl] += Mean(b, Red, px, py) - redG; r1[cl] += Mean(a, Red, px, py) - redG; nb[cl]++;
            }
            string Share(int cl) => nb[cl] < 10 || c0[cl] <= 0 ? $"n/a ({nb[cl]} bins: judge by eye)"
                : $"contrast {c1[cl] / c0[cl]:0.00}, redness {(r0[cl] > 1 ? (r1[cl] / r0[cl]).ToString("0.00") : "n/a")} ({nb[cl]} bins)";
            return $"{fill}; ring dots over the blind arcs: {Share(1)}; over the open arcs: {Share(0)} (shares of the reference without the sectors = 1)";
        }
    }
}
