using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using OpenZ.Sim;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace OpenZ
{
    /// <summary>
    /// <c></c>
    /// (1) a Zod nuke line (DebugNuke, no strike): transmission-camera frames of the red-button insert with the separate hammer-fist
    /// prop (14d) + the talk frame (hats / facing re-check); (2) frames the red fort and destroys it in the sim (a loss): RTS + a low
    /// close camera on the concrete blocks (6) and the InfernoFlame columns + heat plume (11); (3) the defeat end screen, then the
    /// victory one and both with the L17 strings: banner element bounds in reference px (1080 high canvas) vs the backdrop rules (12).
    /// </summary>
    public sealed class EndingRestProof : MonoBehaviour
    {
        public static string Dir = global::OpenZ.OzRelease.Milestones("bundle_ending_bugs_rest");
        public static string Last = "";
        static EndingRestProof run;
        public static bool Busy => run != null;

        static void Log(string s) { Directory.CreateDirectory(Dir); File.AppendAllText(Dir + "/proof_log.txt", System.DateTime.Now.ToString("HH:mm:ss.f") + " " + s + "\n"); Debug.Log("[EndingRestProof] " + s); Last = s; }

        public static string Run()
        {
            if (run) return "busy";
            var go = new GameObject("EndingRestProof"); run = go.AddComponent<EndingRestProof>();
            run.StartCoroutine(run.Seq());
            return "started";
        }

        IEnumerator Seq()
        {
            var g = ZGame.I; var W = g ? g.World : null;
            Log("bundle_ending_bugs_rest Play proof " + (g ? g.level : "?") + " " + Screen.width + "x" + Screen.height + ", PropFist " + ZodStage.PropFist + ", ConcreteParts " + BuildingBreaker.ConcreteParts +
                ", InfernoFlame columns " + OzFortInferno.Columns + " (" + OzFortInferno.ColumnHeightMin + "-" + OzFortInferno.ColumnHeightMax + " m)");
            float s0 = Time.unscaledTime;
            // ---- (1) the red-button insert
            string r = ZodBriefing.DebugNuke(); Log("DebugNuke: " + r);
            var st = FindFirstObjectByType<ZodStage>();
            float[] times = { 0.04f, 0.16f, 0.27f, 0.33f, 0.37f, 0.44f, 0.62f, 0.86f, 1.0f, 1.1f };
            int next = 0; bool talk = false; float tq = Time.unscaledTime;
            while (Time.unscaledTime - tq < 25f)
            {
                yield return new WaitForEndOfFrame();
                if (!st) st = FindFirstObjectByType<ZodStage>();
                if (!st) continue;
                if (!talk && Time.unscaledTime - tq > 1.6f && !st.Slamming)
                {
                    talk = true; Save(st.CaptureFeed(1280, 960), "00_talk_feed.png");
                    Log("talk frame 00_talk_feed.png: " + st.HqStatus() + " | " + st.BoysStatus());
                }
                if (!st.PressPlaced || st.PressU < 0f || st.PressU > 3f) { if (next > 0) break; continue; }
                while (next < times.Length && st.PressU >= times[next])
                {
                    string f = "fist_" + ((int)(times[next] * 100f)).ToString("D3") + ".png";
                    Save(st.CaptureFeed(1280, 960), f);
                    Log("insert " + f + " u " + st.PressU.ToString("F2") + ": " + st.FistStatus() + " | " + st.PressStatus());
                    next++;
                }
                if (next >= times.Length) break;
            }
            var snd = OzSoundLog.Since(s0 - 0.5f); var lines = new List<string>();
            foreach (var e in snd) { var s = OzSoundLog.Format(new List<OzSoundLog.Entry> { e }); if (s.Contains("Zod")) lines.Add(s.Trim()); }
            Log("insert sounds: " + string.Join(" | ", lines.ToArray()));
            yield return new WaitForSecondsRealtime(4f);
            // ---- (2) the red fort falls (a loss)
            ZObject fort = null; if (W != null) foreach (var o in W.Objects) if (o.IsFort && o.Owner == Team.Red && !o.Destroyed) { fort = o; break; }
            if (fort == null) { Log("no red fort"); Done(); yield break; }
            BuildingBreaker bb = null; var objF = typeof(BuildingBreaker).GetField("obj", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
            foreach (var b in FindObjectsByType<BuildingBreaker>(FindObjectsSortMode.None)) if (objF != null && objF.GetValue(b) == fort) { bb = b; break; }
            var at = bb ? bb.transform.position : Vector3.zero;
            var rts = FindFirstObjectByType<RtsCamera>();
            if (rts) { rts.focus = at; rts.height = 58f; }
            yield return new WaitForSecondsRealtime(1.2f);
            float k0 = Time.unscaledTime; int c0 = BuildingBreaker.ConcreteBlocks;
            fort.SetHealth(0); W.UpdateObjectHealth(fort);
            Log("KILL red fort #" + fort.Id + " at " + at.ToString("F0") + " (concrete blocks prepared " + c0 + ")");
            float[] ft = { 1.0f, 2.2f, 4.5f, 8f, 12f };
            int fi = 0;
            while (fi < ft.Length)
            {
                yield return null;
                float t = Time.unscaledTime - k0;
                if (t < ft[fi]) continue;
                string tag = ft[fi].ToString("F1").Replace('.', '_');
                Shot(Camera.main, "rts_" + tag + ".png", 1920, 1080);
                if (ft[fi] >= 4f) Shot(Close(at), "close_" + tag + ".png", 1920, 1080);
                var inf = OzFortInferno.Last;
                Log("t+" + ft[fi].ToString("F1") + " inferno " + (inf ? "K " + inf.K.ToString("F2") + " light " + inf.LightNow.ToString("F1") + " columns " + inf.ColumnCount : "none") +
                    ", columns drawn " + OzFortInferno.ColumnsDrawn + ", concrete blocks " + BuildingBreaker.ConcreteBlocks + ", " + Parts(bb) + ", fps " + (1f / Mathf.Max(1e-4f, Time.smoothDeltaTime)).ToString("F0"));
                fi++;
            }
            var snd2 = OzSoundLog.Since(k0 - 0.2f); var cnt = new SortedDictionary<string, int>();
            foreach (var e in snd2) { string key = e.key + "/" + (e.clip ?? "-") + " " + e.outcome; int n; cnt.TryGetValue(key, out n); cnt[key] = n + 1; }
            var parts = new List<string>(); foreach (var kv in cnt) if (kv.Key.Contains("debris") || kv.Key.Contains("collapse") || kv.Key.Contains("fire") || kv.Key.Contains("flame") || kv.Key.Contains("concrete")) parts.Add(kv.Key + " x" + kv.Value);
            Log("fall sounds: " + string.Join(", ", parts.ToArray()));
            // ---- (3) the end screens
            float we = Time.unscaledTime;
            while (!OpenZEndScreen.AnyShown && Time.unscaledTime - we < 20f) yield return null;
            yield return new WaitForSecondsRealtime(3.5f);
            var es = FindFirstObjectByType<OpenZEndScreen>();
            yield return Banner(es, "defeat_L02");
            var show = typeof(OpenZEndScreen).GetMethod("Show", BindingFlags.NonPublic | BindingFlags.Instance, null, new[] { typeof(Team) }, null);
            if (es && show != null)
            {
                show.Invoke(es, new object[] { Team.Red }); yield return new WaitForSecondsRealtime(3.5f); yield return Banner(es, "victory_L02");
                string lv = g.level; g.level = "L17";
                show.Invoke(es, new object[] { Team.Red }); yield return new WaitForSecondsRealtime(3.5f); yield return Banner(es, "victory_L17");
                show.Invoke(es, new object[] { Team.Blue }); yield return new WaitForSecondsRealtime(3.5f); yield return Banner(es, "defeat_L17");
                g.level = lv;
            }
            else Log("no end screen / Show(Team) not found");
            Log("DONE in " + (Time.unscaledTime - s0).ToString("F0") + " s");
            Done();
        }

        static readonly Dictionary<string, Vector2> Rules = new Dictionary<string, Vector2> { { "defeat", new Vector2(214f, 414f) }, { "victory", new Vector2(202.5f, 426f) } };   // backdrop rule rows (ref px: the upper rule's bottom edge, the lower rule's top edge), bd_*.png 3840x2160
        static readonly Dictionary<string, Vector2> FaceRows = new Dictionary<string, Vector2> { { "defeat", new Vector2(137f / 517f, 395f / 517f) }, { "victory", new Vector2(137f / 523f, 401f / 523f) } };

        IEnumerator Banner(OpenZEndScreen es, string tag)
        {
            yield return new WaitForEndOfFrame();
            var tex = ScreenCapture.CaptureScreenshotAsTexture(); Save(tex, "banner_" + tag + ".png");
            if (!es) { Log(tag + ": no end screen"); yield break; }
            string kind = tag.StartsWith("defeat") ? "defeat" : "victory";
            var canvas = typeof(OpenZEndScreen).GetField("canvas", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(es) as Canvas; float sf = canvas ? canvas.scaleFactor : 1f;
            if (!canvas) { Log(tag + ": no canvas"); yield break; }
            System.Func<Vector3, float> refY = w => (Screen.height - w.y) / sf;
            float chipTop = float.NaN, chipBot = float.NaN, subTop = float.NaN, subBot = float.NaN, subW = float.NaN, wmTop = float.NaN, wmBot = float.NaN; string subText = "";
            var cs = new Vector3[4];
            foreach (var rt in canvas.GetComponentsInChildren<RectTransform>(true))
            {
                if (!rt.gameObject.activeInHierarchy) continue;
                if (rt.name == "Eyebrow") { rt.GetWorldCorners(cs); chipTop = refY(cs[1]); chipBot = refY(cs[0]); }
                else if (rt.name == "Subtitle")
                {
                    var tmp = rt.GetComponent<TMP_Text>();
                    if (tmp) { tmp.ForceMeshUpdate(); var b = tmp.textBounds; subTop = refY(rt.TransformPoint(new Vector3(0, b.max.y, 0))); subBot = refY(rt.TransformPoint(new Vector3(0, b.min.y, 0))); subW = (rt.TransformPoint(new Vector3(b.max.x, 0, 0)).x - rt.TransformPoint(new Vector3(b.min.x, 0, 0)).x) / sf; subText = tmp.text; }
                }
                else if (rt.name == "Wordmark" || (rt.parent && rt.parent.name == "Wordmark" && rt.name == "Face"))
                {
                    if (rt.name != "Face") continue;
                    rt.GetWorldCorners(cs); float top = refY(cs[1]), bot = refY(cs[0]), hgt = bot - top;
                    var fr = FaceRows[kind]; wmTop = top + fr.x * hgt; wmBot = top + fr.y * hgt;
                }
            }
            var rl = Rules[kind]; float k16 = 1600f / 1080f, canvasW16 = 2560f / k16;
            Log(tag + " banner (ref px, 1080 canvas): upper rule bottom " + rl.x + " | chip " + chipTop.ToString("F1") + "-" + chipBot.ToString("F1") + " (gap " + (chipTop - rl.x).ToString("F1") + ")" +
                " | wordmark glyphs " + wmTop.ToString("F1") + "-" + wmBot.ToString("F1") + " (gap to chip " + (wmTop - chipBot).ToString("F1") + ")" +
                " | subtitle '" + subText + "' glyphs " + subTop.ToString("F1") + "-" + subBot.ToString("F1") + " width " + subW.ToString("F0") + " (gap to wordmark " + (subTop - wmBot).ToString("F1") + ", to the lower rule " + (rl.y - subBot).ToString("F1") + ")" +
                " | lower rule top " + rl.y + " || 1080p: min gap " + Mathf.Min(chipTop - rl.x, Mathf.Min(wmTop - chipBot, Mathf.Min(subTop - wmBot, rl.y - subBot))).ToString("F1") +
                " px; 16:10 full size: x" + k16.ToString("F2") + " = " + (k16 * Mathf.Min(chipTop - rl.x, Mathf.Min(wmTop - chipBot, Mathf.Min(subTop - wmBot, rl.y - subBot)))).ToString("F1") + " px, subtitle " + subW.ToString("F0") + " of " + canvasW16.ToString("F0") + " canvas units wide at 16:10");
        }

        string Parts(BuildingBreaker bb)
        {
            if (!bb) return "no breaker";
            var f = typeof(BuildingBreaker).GetField("bigParts", BindingFlags.NonPublic | BindingFlags.Instance);
            var list = f != null ? f.GetValue(bb) as List<Rigidbody> : null; if (list == null) return "no parts list";
            int n = 0, moving = 0, sleeping = 0; float maxV = 0f, maxSpin = 0f, mass = 0f;
            foreach (var rb in list) { if (!rb || !rb.gameObject.activeInHierarchy) continue; n++; mass += rb.mass; float v = rb.isKinematic ? 0f : rb.linearVelocity.magnitude; if (v > 0.3f) moving++; if (rb.isKinematic || rb.IsSleeping()) sleeping++; maxV = Mathf.Max(maxV, v); maxSpin = Mathf.Max(maxSpin, rb.isKinematic ? 0f : rb.angularVelocity.magnitude); }
            return "big parts " + n + " (moving " + moving + ", settled " + sleeping + ", max speed " + maxV.ToString("F1") + " m/s, max spin " + maxSpin.ToString("F1") + " rad/s, mass avg " + (n > 0 ? mass / n : 0f).ToString("F0") + " kg)";
        }

        Camera close;
        Camera Close(Vector3 at)
        {
            if (close) return close;
            var main = Camera.main;
            var go = new GameObject("EndingRestProofCloseCam"); close = go.AddComponent<Camera>();
            if (main) close.CopyFrom(main);
            close.enabled = false; close.fieldOfView = 40f; close.farClipPlane = 1500f;
            var d = main ? Vector3.ProjectOnPlane(main.transform.position - at, Vector3.up).normalized : Vector3.back;
            if (d.sqrMagnitude < 1e-4f) d = Vector3.back;
            go.transform.position = at + d * 34f + Vector3.up * 16f;
            go.transform.LookAt(at + Vector3.up * 3f);
            var src = main ? main.GetUniversalAdditionalCameraData() : null; var dst = close.GetUniversalAdditionalCameraData();
            if (src && dst) { dst.renderPostProcessing = src.renderPostProcessing; dst.antialiasing = src.antialiasing; dst.antialiasingQuality = src.antialiasingQuality; dst.volumeLayerMask = src.volumeLayerMask; dst.requiresDepthTexture = true; dst.requiresColorTexture = true; }
            return close;
        }

        static void Shot(Camera c, string file, int w, int h)
        {
            if (!c) return;
            var prev = c.targetTexture;
            var rt = RenderTexture.GetTemporary(w, h, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            c.targetTexture = rt; c.Render();
            var act = RenderTexture.active; RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); tex.Apply();
            RenderTexture.active = act; c.targetTexture = prev; RenderTexture.ReleaseTemporary(rt);
            Save(tex, file);
        }

        static void Save(Texture2D tex, string file)
        {
            if (!tex) return;
            Directory.CreateDirectory(Dir); File.WriteAllBytes(Dir + "/" + file, tex.EncodeToPNG()); Destroy(tex);
        }

        void Done() { if (close) Destroy(close.gameObject); run = null; Destroy(gameObject); }
    }
}
