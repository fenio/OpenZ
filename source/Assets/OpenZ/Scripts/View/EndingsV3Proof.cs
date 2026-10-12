using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using OpenZ.Sim;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace OpenZ
{
    /// <summary>
    /// <c></c>
    /// (A) a Zod nuke line (DebugNuke, no strike): feed frames every 0.2 s (cigar across the insert cut, mouth interior) + the desk
    /// insert (thin hinged lids, black bezels under the red wash); (B) the BLUE fort is killed in the sim (a real win): RTS frames
    /// 0-1.2 s after the impact at 10 fps (white wash fade, ring, refraction band), the mushroom at +4.3 / +7.3 s, the inferno at
    /// +8 / +15 s (RTS + a low close camera), feed frames of the win line every 0.25 s, Zod's chin-down peak; (C) the end screen: mean
    /// luma of every frame for its first second (one ease, no step) + the full frame at +3.6 s and its layout line; (D) zod_lose_01 with
    /// the HQ on fire (facepalm + hat) and zod_win_02 with the gaze at the boys (the "celebrate" punch): feed frames every 0.2 s;
    /// </summary>
    public sealed class EndingsV3Proof : MonoBehaviour
    {
        public static string Dir = global::OpenZ.OzRelease.Milestones("task_endings_fix_v3");
        public static string Last = "";
        static EndingsV3Proof run;
        public static bool Busy => run != null;

        static void Log(string s) { Directory.CreateDirectory(Dir); File.AppendAllText(Dir + "/proof_log.txt", System.DateTime.Now.ToString("HH:mm:ss.f") + " " + s + "\n"); Debug.Log("[EndingsV3Proof] " + s); Last = s; }

        public static string Run()
        {
            if (run) return "busy";
            var go = new GameObject("EndingsV3Proof"); run = go.AddComponent<EndingsV3Proof>();
            run.StartCoroutine(run.Seq());
            return "started";
        }

        IEnumerator Seq()
        {
            var g = ZGame.I; var W = g ? g.World : null;
            float s0 = Time.unscaledTime;
            Log("task_endings_fix_v3 Play proof " + (g ? g.level : "?") + " " + Screen.width + "x" + Screen.height + ", music " + AudioDirector.MusicEnabled + ", chin clamp " + ZodTalkRig.MaxChinDownDeg + " deg");
            // ---- (A) the nuke line: feed every 0.2 s + the insert
            Log("DebugNuke: " + ZodBriefing.DebugNuke());
            var st = FindFirstObjectByType<ZodStage>();
            float tq = Time.unscaledTime, nextFeed = 0f; int fi = 0; bool insA = false, insB = false;
            while (Time.unscaledTime - tq < 14f)
            {
                yield return new WaitForEndOfFrame();
                if (!st) st = FindFirstObjectByType<ZodStage>();
                if (!st) continue;
                float e = Time.unscaledTime - tq;
                if (e >= nextFeed) { nextFeed += 0.2f; Save(st.CaptureFeed(640, 480), "a_feed_" + (fi++).ToString("D2") + "_" + e.ToString("F1") + ".png"); }
                if (st.PressPlaced && !insA && st.PressU >= 0.3f) { insA = true; Save(st.CaptureFeed(1280, 960), "a_insert_030.png"); Log("insert u " + st.PressU.ToString("F2") + ": " + st.PressStatus()); }
                if (st.PressPlaced && !insB && st.PressU >= 0.9f) { insB = true; Save(st.CaptureFeed(1280, 960), "a_insert_090.png"); }
                var rig = FindFirstObjectByType<ZodTalkRig>();
                if (rig && rig.Mouth01 > 0.6f && e > 1f && !File.Exists(Dir + "/a_mouth_open.png")) { Save(st.CaptureFeed(1280, 960), "a_mouth_open.png"); Log("mouth open " + rig.Mouth01.ToString("F2") + " at " + e.ToString("F2") + " s, mouth interior " + rig.HasMouthPlug + ", side cigar " + rig.HasSideCigar); }
                if (e > 3f && ZodBriefing.DebugHq().Contains("no line")) break;
            }
            Log("nuke line done: feed frames " + fi + ", chin-down peak " + ZodTalkRig.ChinDownPeak.ToString("F1") + " (shown " + ZodTalkRig.ChinDownOutPeak.ToString("F1") + ", clamps " + ZodTalkRig.ChinClamps + "; roll peak " + ZodTalkRig.RollPeak.ToString("F1") + ", roll clamps " + ZodTalkRig.RollClamps + ")");
            yield return new WaitForSecondsRealtime(3f);
            // ---- (B) the blue fort falls (a win)
            ZObject fort = null; if (W != null) foreach (var o in W.Objects) if (o.IsFort && o.Owner == Team.Blue && !o.Destroyed) { fort = o; break; }
            if (fort == null) { Log("no blue fort"); Done(); yield break; }
            BuildingBreaker bb = null; var objF = typeof(BuildingBreaker).GetField("obj", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
            foreach (var b in FindObjectsByType<BuildingBreaker>(FindObjectsSortMode.None)) if (objF != null && objF.GetValue(b) == fort) { bb = b; break; }
            var at = bb ? bb.transform.position : Vector3.zero;
            float imp0 = OpenZNukeStrike.LastImpactTime;
            float k0 = Time.unscaledTime;
            ZodTalkRig.ResetChinStats();
            fort.SetHealth(0); W.UpdateObjectHealth(fort);
            Log("KILL blue fort #" + fort.Id + " at " + at.ToString("F0"));
            // wait for the impact (game time stamp changes)
            while (Mathf.Approximately(OpenZNukeStrike.LastImpactTime, imp0) && Time.unscaledTime - k0 < 40f) yield return null;
            float ti = Time.unscaledTime;
            Log("impact " + (ti - k0).ToString("F1") + " s after the kill at " + OpenZNukeStrike.LastImpactPoint.ToString("F0"));
            float[] ring = { 0f, 0.1f, 0.2f, 0.3f, 0.4f, 0.5f, 0.6f, 0.7f, 0.8f, 0.9f, 1.0f, 1.1f, 1.2f };
            float[] big = { 4.3f, 7.3f, 8f, 15f };
            int ri = 0, bi = 0; float nextZ = 0f; int zi = 0; bool endSeen = false; float endAt = -1f;
            var luma = new List<string>();
            while (Time.unscaledTime - ti < 60f)
            {
                yield return new WaitForEndOfFrame();
                float e = Time.unscaledTime - ti;
                if (ri < ring.Length && e >= ring[ri])
                {
                    Shot(Camera.main, "b_ring_" + ((int)(ring[ri] * 10f)).ToString("D2") + ".png", 960, 540);
                    Log("ring t+" + e.ToString("F2") + " (real s), fort shockwave R " + OpenZFortShockwave.RadiusAt(Time.time - OpenZFortShockwave.LastFireTime).ToString("F0") + " m, ground ring " + OpenZFortShockwave.LastGroundRing.ToString("F3"));
                    ri++;
                }
                if (bi < big.Length && e >= big[bi])
                {
                    string tag = big[bi].ToString("F1").Replace('.', '_');
                    Shot(Camera.main, "b_rts_" + tag + ".png", 1920, 1080);
                    if (big[bi] >= 8f) Shot(Close(at), "b_close_" + tag + ".png", 1920, 1080);
                    var inf = OzFortInferno.Last; var mu = OpenZNukeMushroom.Current;
                    Log("t+" + big[bi].ToString("F1") + ": " + (mu ? mu.Status(Camera.main) : "no mushroom") + " | inferno " + (inf ? "K " + inf.K.ToString("F2") + " light " + inf.LightNow.ToString("F1") + ", bed " + (OzFortInferno.BedRadius * 2f).ToString("F0") + " m" : "none") + " | fps " + (1f / Mathf.Max(1e-4f, Time.smoothDeltaTime)).ToString("F0"));
                    bi++;
                }
                if (!st) st = FindFirstObjectByType<ZodStage>();
                if (st && e >= nextZ && ZodBriefing.DebugHq().Contains(" | line "))
                {
                    nextZ = e + 0.25f;
                    Save(st.CaptureFeed(640, 480), "b_winfeed_" + (zi++).ToString("D2") + ".png");
                }
                if (!endSeen && OpenZEndScreen.AnyShown) { endSeen = true; endAt = Time.unscaledTime; Log("end screen shown " + (endAt - ti).ToString("F1") + " s after the impact"); }
                if (endSeen)
                {
                    float ee = Time.unscaledTime - endAt;
                    if (ee <= 1.0f)
                    {
                        var tex = ScreenCapture.CaptureScreenshotAsTexture();
                        luma.Add(ee.ToString("F3") + " " + MeanLuma(tex).ToString("F1"));
                        Destroy(tex);
                    }
                    else if (ee >= 3.6f)
                    {
                        var tex = ScreenCapture.CaptureScreenshotAsTexture(); Save(tex, "c_endscreen_win.png");
                        var es = FindFirstObjectByType<OpenZEndScreen>();
                        Log("end screen luma (s since shown, mean luma 0-255): " + string.Join(" | ", luma.ToArray()));
                        Log("end screen layout: " + (es ? es.LayoutStatus : "none"));
                        break;
                    }
                }
            }
            Log("ring segments faded on cliffs: " + OpenZFortShockwave.RingCliffFaded);
            Log("win feed frames " + zi + ", chin-down peak " + ZodTalkRig.ChinDownPeak.ToString("F1") + " (shown " + ZodTalkRig.ChinDownOutPeak.ToString("F1") + ", clamps " + ZodTalkRig.ChinClamps + "; roll peak " + ZodTalkRig.RollPeak.ToString("F1") + ", roll clamps " + ZodTalkRig.RollClamps + ")");
            // ---- (E) the ending's sounds
            var snd = OzSoundLog.Since(k0 - 0.5f);
            File.WriteAllText(Dir + "/ozsoundlog.txt", OzSoundLog.Format(snd));
            var keys = new SortedDictionary<string, int>();
            foreach (var en in snd) if (en.key != null && (en.key.StartsWith("ui_") || en.key.StartsWith("mus") || en.key.StartsWith("ter_") || en.key.Contains("static") || en.key.Contains("ann_")))
                { string k = en.key + " " + en.outcome; int n; keys.TryGetValue(k, out n); keys[k] = n + 1; }
            var parts = new List<string>(); foreach (var kv in keys) parts.Add(kv.Key + " x" + kv.Value);
            Log("ending sounds: " + string.Join(", ", parts.ToArray()));
            // ---- (D) zod_lose_01 with the HQ burning (facepalm), then zod_win_02 with the gaze at the boys
            while (!ZodBriefing.DebugHq().Contains("no line") && Time.unscaledTime - ti < 90f) yield return null;
            ZodTalkRig.ResetChinStats();
            if (st) st.HqFire(true);
            Log("DebugLine zod_lose_01: " + ZodBriefing.DebugLine("zod_lose_01"));
            yield return Feed(st, "d_lose01_", 7f);
            Log("lose_01 chin-down peak " + ZodTalkRig.ChinDownPeak.ToString("F1") + " (shown " + ZodTalkRig.ChinDownOutPeak.ToString("F1") + ", clamps " + ZodTalkRig.ChinClamps + ") | " + ZodBriefing.DebugHq());
            if (st) st.HqFire(false);
            while (!ZodBriefing.DebugHq().Contains("no line") && Time.unscaledTime - ti < 110f) yield return null;
            yield return new WaitForSecondsRealtime(0.5f);
            ZodTalkRig.ResetChinStats();
            Log("DebugLine zod_win_02: " + ZodBriefing.DebugLine("zod_win_02"));
            if (st) st.GazeBoys = true;
            yield return Feed(st, "d_win02_", 13f);
            Log("win_02 chin-down peak " + ZodTalkRig.ChinDownPeak.ToString("F1") + " (shown " + ZodTalkRig.ChinDownOutPeak.ToString("F1") + ", clamps " + ZodTalkRig.ChinClamps + "; roll peak " + ZodTalkRig.RollPeak.ToString("F1") + ", roll clamps " + ZodTalkRig.RollClamps + ")");
            Log("DONE in " + (Time.unscaledTime - s0).ToString("F0") + " s");
            Done();
        }

        IEnumerator Feed(ZodStage st, string prefix, float seconds)
        {
            float t0 = Time.unscaledTime, next = 0f; int n = 0;
            while (Time.unscaledTime - t0 < seconds)
            {
                yield return new WaitForEndOfFrame();
                float e = Time.unscaledTime - t0;
                if (st && e >= next) { next += 0.2f; Save(st.CaptureFeed(640, 480), prefix + (n++).ToString("D2") + "_" + e.ToString("F1") + ".png"); }
                if (e > 2f && ZodBriefing.DebugHq().Contains("no line")) break;
            }
        }

        static float MeanLuma(Texture2D t)
        {
            if (!t) return -1f;
            var px = t.GetPixels32(); double sum = 0; int n = 0;
            for (int i = 0; i < px.Length; i += 61) { var c = px[i]; sum += 0.2126 * c.r + 0.7152 * c.g + 0.0722 * c.b; n++; }
            return n > 0 ? (float)(sum / n) : -1f;
        }

        Camera close;
        Camera Close(Vector3 at)
        {
            if (close) return close;
            var main = Camera.main;
            var go = new GameObject("EndingsV3ProofCloseCam"); close = go.AddComponent<Camera>();
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
