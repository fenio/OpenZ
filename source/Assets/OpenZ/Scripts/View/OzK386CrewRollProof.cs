using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// Version 1.1 item 386 Play check (menus OpenZ/V1.1/K386 ...): the player's vehicle factory builds each vehicle it can make (jeep,
    /// light, medium, heavy tank, APC, MLRS; the tech is lifted to the top for the check and put back), the player's fort builds a
    /// crane and a jeep (its own door). From the first frame its view exists until 1 s after its hull is full size, every frame is
    /// read at the END of the frame (after every script posed the hull and its crew): the hull's grow share, the crew's drawn point
    /// against its anchor (jeep: the hips on the seat; hatch commander: the root his pose asks for) and the figure's size against the
    /// size the hull asks for (as cm at the top of his helmet). Pass: both, and the figure's size against the DRAWN hull's (as cm at the helmet), at most 1 cm in every frame the crew is drawn,
    /// and at least one jeep frame that exercised the fault (hull smaller with the driver shown, or a factory push followed). A 6-frame strip
    /// of the jeep coming out (35 m, pitch 45, the game camera's yaw) is written as six pictures and one sheet. Test only: a staged
    /// 1 s build per vehicle, nothing saved.
    /// </summary>
    public class OzK386CrewRollProof : MonoBehaviour
    {
        public static string Report = "idle";
        public static readonly string[] FactoryTypes = { "jeep", "light", "medium", "heavy", "apc", "missile_launcher" };
        public static readonly string[] FortTypes = { "crane", "jeep" };
        public const float PassCm = 1f, StripDist = 35f, StripPitch = 45f;
        static readonly float[] StripAt = { 0f, 0.45f, 0.9f, 1.4f, 2.0f, 2.8f };   // s after the jeep's view exists

        string outDir, runTag; bool before; bool onlyJeep;
        readonly StringBuilder rep = new StringBuilder();
        readonly StringBuilder csv = new StringBuilder("label,type,frame,t_s,grow,shown,seated,anchor_off_cm,size_off_cm,ratio_off_cm,head_drift_cm,size,want_size,hull_lossy\n");
        int fails, passes, jeepExercised;

        /// <summary>before = the old crew (BoardingView.K386On off for this run: full size, not moved with the push; put back after); onlyJeep = the
        /// strip run (the jeep at the factory only).</summary>
        public static void Run(string outDir, string runTag, bool before, bool onlyJeep)
        {
            if (Report == "running") { Debug.LogWarning("[OpenZ K386] a check is running already"); return; }
            Report = "running";
            var go = new GameObject("OzK386CrewRollProof");
            var p = go.AddComponent<OzK386CrewRollProof>(); p.outDir = outDir; p.runTag = runTag; p.before = before; p.onlyJeep = onlyJeep;
        }

        void Start() { StartCoroutine(Main()); }

        static ZObject Find(ZWorld W, System.Predicate<ZObject> f) { foreach (var o in W.Objects) if (o != null && f(o)) return o; return null; }

        static ZObject Nearest(ZWorld W, ZObject from, System.Predicate<ZObject> f)
        {
            ZObject best = null; long bd = long.MaxValue;
            foreach (var o in W.Objects)
            {
                if (o == null || o.Destroyed || !f(o)) continue;
                long dx = o.CenterX - (from != null ? from.CenterX : 0), dy = o.CenterY - (from != null ? from.CenterY : 0), d = dx * dx + dy * dy;
                if (d < bd) { bd = d; best = o; }
            }
            return best;
        }

        bool Own(ZWorld W, ZObject b, Team me)
        {
            if (b == null) return false;
            if (b.Owner == me) return true;
            int z = W.GetZoneIndex(b.CenterX, b.CenterY);
            var flag = Find(W, o => o.Type == "flag" && W.GetZoneIndex(o.CenterX, o.CenterY) == z);
            if (flag != null) W.AwardZone(flag, me);
            return b.Owner == me;
        }

        void Line(string s) { rep.AppendLine(s); Debug.Log("[OpenZ K386] " + s); }

        IEnumerator Main()
        {
            var g = ZGame.I;
            if (!g || g.World == null) { Report = "FAIL no level running"; Destroy(gameObject); yield break; }
            var W = g.World; var me = g.player;
            Directory.CreateDirectory(outDir);
            bool grow0 = BoardingView.K386On; BoardingView.K386On = !before;
            int ti = (int)me; int tech0 = ti >= 0 && ti < W.TechBonus.Length ? W.TechBonus[ti] : 0;
            if (ti >= 0 && ti < W.TechBonus.Length) W.TechBonus[ti] = 5;
            Line($"{runTag}: crew grows with its hull {(BoardingView.K386On ? "ON (the kit)" : "OFF (the old full-size crew, not moved with the factory push: BEFORE)")}; player {me}; tech lifted to the top for the check");
            var fort = Nearest(W, null, o => o.IsFort && o.Owner == me);
            var fac = Nearest(W, fort, o => o.Type == "vehicle_factory");
            if (fac != null && !Own(W, fac, me)) { Line("FAIL  the vehicle factory #" + fac.Id + " could not be made the player's"); fac = null; }
            if (fac == null) Line("FAIL  no vehicle factory on this level for the player");
            var list = new List<(ZObject b, string type, string label)>();
            if (fac != null) foreach (var t in onlyJeep ? new[] { "jeep" } : FactoryTypes) list.Add((fac, t, "factory_" + t));
            if (!onlyJeep && fort != null) foreach (var t in FortTypes) list.Add((fort, t, "fort_" + t));
            foreach (var (b, type, label) in list)
            {
                yield return Build(g, W, me, b, type, label, label == "factory_jeep");
                yield return new WaitForSeconds(0.5f);
            }
            if (ti >= 0 && ti < W.TechBonus.Length) W.TechBonus[ti] = tech0;
            BoardingView.K386On = grow0;
            if (jeepExercised == 0)
            {
                Line("FAIL  no jeep frame exercised the fault: the hull never drew smaller while the driver was shown and no factory push moved a posed driver (see the door fits in the K386 State line and report it)");
                fails++;
            }
            string verdict = fails == 0 && passes > 0 ? "PASS" : "FAIL";
            Line($"K386 CREW ROLL-OUT {runTag.ToUpperInvariant()}: {verdict} ({passes} PASS, {fails} FAIL); counters: crew drawn smaller with its hull {BoardingView.K386ScaledFrames} frames (smallest share {BoardingView.K386MinShare:0.000}), hull pushes followed {BoardingView.K386Follows} (largest {BoardingView.K386FollowMaxM:0.00} m)");
            Line($"LOOK at {outDir}/k386_{runTag}_jeep_strip_sheet.png (6 frames, 35 m): the driver's head and hands stay inside the jeep in every frame, at the wheel, as small as the jeep");
            try { File.WriteAllText(Path.Combine(outDir, "k386_" + runTag + "_report.txt"), rep.ToString()); File.WriteAllText(Path.Combine(outDir, "k386_" + runTag + "_frames.csv"), csv.ToString()); } catch (System.Exception e) { Debug.LogWarning("[OpenZ K386] write failed: " + e.Message); }
            Report = verdict;
            Destroy(gameObject);
        }

        IEnumerator Build(ZGame g, ZWorld W, Team me, ZObject b, string type, string label, bool strip)
        {
            var known = new HashSet<int>(); foreach (var o in W.Objects) if (o != null) known.Add(o.Id);
            if (b.Building) W.StopBuilding(me, b);
            if (!W.StartBuilding(me, b, type)) { Line($"FAIL  {label}: {b.Type} #{b.Id} refused to build a {type}"); fails++; yield break; }
            b.BInit = W.Time - 20.0; b.BFinal = W.Time + 1.0;                                             // staged: done in 1 s
            var rally0 = new List<ZWaypoint>(b.RallyPoints);                                               // each new vehicle drives 10 tiles on past the door: the next one has a clear way out
            if (b.GetCreationPoint(out int ax, out int ay) && b.GetCreationMovePoint(out int bx, out int by))
            {
                float dx = bx - ax, dy = by - ay, l = Mathf.Sqrt(dx * dx + dy * dy);
                if (l > 0.1f) W.SetRallyPoints(me, b, new List<ZWaypoint> { new ZWaypoint { mode = WpMode.Move, refId = -1, x = bx + (int)(dx / l * 160f), y = by + (int)(dy / l * 160f), playerGiven = true } });
            }
            ZObject u = null; float wait = 0f;
            while (u == null && wait < 15f)
            {
                foreach (var o in W.Objects) if (o != null && !known.Contains(o.Id) && o.Type == type && o.Kind == ZKind.Vehicle && o.Owner == me) { u = o; break; }
                wait += Time.deltaTime; yield return null;
            }
            W.StopBuilding(me, b);
            b.RallyPoints.Clear(); b.RallyPoints.AddRange(rally0);                                         // put back
            if (u == null) { Line($"FAIL  {label}: no {type} came out in 15 s"); fails++; yield break; }
            UnitView v = null; wait = 0f;
            while (!v && wait < 3f) { g.Views.TryGetValue(u.Id, out v); wait += Time.deltaTime; yield return null; }
            if (!v) { Line($"FAIL  {label}: {type} #{u.Id} has no view"); fails++; yield break; }
            var eof = new WaitForEndOfFrame();
            int follows0 = BoardingView.K386Follows;
            var rows = new List<(int f, float t, BoardingView.CrewSample s, float lossy)>();
            Texture2D sheet = strip ? new Texture2D(2880, 1080, TextureFormat.RGB24, false) : null; int shot = 0;
            float t0 = Time.time, fullAt = -1f; int frame = 0; float minGrow = 1f; bool everShown = false;
            while (v && !u.Destroyed && Time.time - t0 < 16f)
            {
                yield return eof;
                if (!v) break;
                float t = Time.time - t0;
                float grow = v.ExitScaleNow; minGrow = Mathf.Min(minGrow, grow);
                if (BoardingView.K386Sample(v, out var s)) { rows.Add((frame, t, s, Mathf.Abs(v.transform.lossyScale.x))); everShown |= s.shown; }
                if (strip && shot < StripAt.Length && t >= StripAt[shot]) { Strip(g, v, sheet, shot, grow, label); shot++; }
                if (grow >= 0.999f && fullAt < 0f) fullAt = t;
                if (fullAt >= 0f && t - fullAt > 1f && (!strip || shot >= StripAt.Length)) break;
                frame++;
            }
            if (sheet) { try { File.WriteAllBytes(Path.Combine(outDir, "k386_" + runTag + "_jeep_strip_sheet.png"), sheet.EncodeToPNG()); } catch (System.Exception) { } Destroy(sheet); }
            // the final pose (hull full size) is the reference for the head-in-hull drift (information: the seated clip sways a little)
            Vector3 headRef = Vector3.zero; float fullLossy = 1f, ratioRef = 0f; bool haveRef = false;
            for (int i = rows.Count - 1; i >= 0; i--) if (rows[i].s.shown && rows[i].s.grow >= 0.999f) { headRef = rows[i].s.headInHull; fullLossy = rows[i].lossy; ratioRef = rows[i].s.size / Mathf.Max(1e-5f, rows[i].lossy); haveRef = true; break; }
            float maxAnchor = 0f, maxSize = 0f, maxRatio = 0f, maxDrift = 0f; int shownFrames = 0, grownShown = 0;
            foreach (var (f, t, s, lossy) in rows)
            {
                float anchorCm = (s.anchor - s.drawn).magnitude * 100f;
                float sizeCm = s.wantSize > 1e-5f ? Mathf.Abs(s.size / s.wantSize - 1f) * s.heightFull * s.grow * 100f : 0f;
                // the drawn figure against the DRAWN hull (both read from their own transforms, nothing taken from the kit): figure size / hull
                // size stays what it is at full size; as cm at the helmet, at the size drawn now
                float ratioCm = haveRef && ratioRef > 1e-6f ? Mathf.Abs(s.size / Mathf.Max(1e-5f, lossy) / ratioRef - 1f) * s.heightFull * s.grow * 100f : 0f;
                float driftCm = haveRef ? (s.headInHull - headRef).magnitude * fullLossy * 100f : 0f;
                csv.AppendLine($"{label},{s.type},{f},{t:0.000},{s.grow:0.0000},{(s.shown ? 1 : 0)},{(s.seated ? 1 : 0)},{anchorCm:0.00},{sizeCm:0.00},{ratioCm:0.00},{driftCm:0.00},{s.size:0.0000},{s.wantSize:0.0000},{lossy:0.0000}");
                if (!s.shown) continue;
                shownFrames++; if (s.grow < 0.999f) grownShown++;
                maxAnchor = Mathf.Max(maxAnchor, anchorCm); maxSize = Mathf.Max(maxSize, sizeCm); maxRatio = Mathf.Max(maxRatio, ratioCm); maxDrift = Mathf.Max(maxDrift, driftCm);
            }
            int followsHere = BoardingView.K386Follows - follows0;
            // the fault's frames were really exercised: the hull drew smaller with the driver shown, or the factory push moved a posed driver,
            // or (BEFORE) the driver stood metres off his hull
            if (type == "jeep" && (grownShown > 0 || followsHere > 0 || maxAnchor > 50f)) jeepExercised++;
            string who = rows.Count > 0 ? (rows[0].s.seated ? "seated driver" : "hatch commander") : "no crew figure";
            if (!everShown && type == "jeep")
            {   // the jeep's driver is always drawn (BoardingView: showSeatedCrew): no figure at all is a fault of its own
                Line($"FAIL  {label}: {type} #{u.Id}: no driver figure drawn in any of the {rows.Count} sampled frames (the jeep came out without its seated driver?)");
                fails++; yield break;
            }
            if (!everShown)
            {   // a buttoned-up tank: nothing of the crew is drawn while it rolls out (Zod: the lid opens only when it stops / is told)
                Line($"PASS  {label}: {type} #{u.Id}: hull grew from {minGrow:0.000} to 1 in {(fullAt >= 0f ? fullAt.ToString("0.00") : ">16")} s; {who}: not drawn in any of the {rows.Count} sampled frames (hatch shut), nothing to be out of place");
                passes++; yield break;
            }
            bool ok = maxAnchor <= PassCm && maxSize <= PassCm && maxRatio <= PassCm && (haveRef || type != "jeep");                     // a jeep driver is always drawn, so he has a full-size frame
            Line($"{(ok ? "PASS" : "FAIL")}  {label}: {type} #{u.Id}: hull grew from {minGrow:0.000} to 1 in {(fullAt >= 0f ? fullAt.ToString("0.00") : ">16")} s; {who} drawn in {shownFrames} frames ({grownShown} while the hull was smaller than full), hull pushes followed {followsHere}: drawn point vs its anchor at most {maxAnchor:0.00} cm, size vs the hull's at most {maxSize:0.00} cm at the helmet, figure vs drawn hull size ratio at most {maxRatio:0.00} cm at the helmet{(haveRef ? "" : " (not measured: no full-size frame with the figure drawn)")} (wanted <= {PassCm:0} cm each); head in the hull's space moved at most {maxDrift:0.0} cm (the seated clip's own sway; information)");
            if (ok) passes++; else fails++;
        }

        void Strip(ZGame g, UnitView v, Texture2D sheet, int i, float grow, string label)
        {
            var main = Camera.main; if (!main) return;
            var go = new GameObject("K386_ShotCam");
            try
            {
                var cam = go.AddComponent<Camera>(); cam.CopyFrom(main);
                var ad = go.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>(); ad.renderPostProcessing = true;
                go.transform.rotation = Quaternion.Euler(StripPitch, main.transform.eulerAngles.y, 0f);
                go.transform.position = v.transform.position + Vector3.up * 1f - go.transform.forward * StripDist;
                var full = Render(cam, 1920, 1080);
                File.WriteAllBytes(Path.Combine(outDir, $"k386_{runTag}_jeep_strip_{i + 1}_t{StripAt[i]:0.00}s_grow{grow:0.000}.png"), full.EncodeToPNG());
                Destroy(full);
                var rt = new RenderTexture(960, 540, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
                cam.targetTexture = rt; cam.Render(); cam.Render();
                RenderTexture.active = rt;
                sheet.ReadPixels(new Rect(0, 0, 960, 540), (i % 3) * 960, (1 - i / 3) * 540); sheet.Apply();
                RenderTexture.active = null; cam.targetTexture = null; rt.Release(); Destroy(rt);
                Line($"      picture {i + 1}/6 of the jeep at {StripAt[i]:0.00} s, hull grow {grow:0.000}");
            }
            catch (System.Exception e) { Debug.LogWarning("[OpenZ K386] picture failed: " + e.Message); }
            finally { Destroy(go); }
        }

        static Texture2D Render(Camera cam, int w, int h)
        {
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            cam.targetTexture = rt;
            for (int k = 0; k < 2; k++) cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); tex.Apply();
            RenderTexture.active = null; cam.targetTexture = null; rt.Release(); Destroy(rt);
            return tex;
        }
    }
}
