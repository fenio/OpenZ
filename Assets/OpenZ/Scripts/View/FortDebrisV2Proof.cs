using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using OpenZ.Sim;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace OpenZ
{
    /// <summary>
    /// <c></c>
    /// chunk counts + size histogram of the blue fort, kills it (the real win: nuke strike), captures the game camera and a low side
    /// camera at the collapse +0.5 / +1.5 / +4 s, the frame time of the collapse frame, the shake gate lines (L02), the crash sounds.
    /// </summary>
    public sealed class FortDebrisV2Proof : MonoBehaviour
    {
        public static readonly string Base = global::OpenZ.OzRelease.Milestones("task_fort_debris_v2");
        public static string Last = "", Status = "idle";
        static FortDebrisV2Proof run;
        string dir = Base;
        bool both;

        void Log(string s) { Directory.CreateDirectory(dir); File.AppendAllText(dir + "/proof_log.txt", System.DateTime.Now.ToString("HH:mm:ss.f") + " " + s + "\n"); Debug.Log("[FortDebrisV2Proof] " + s); Last = s; }

        public static string Run(bool thenL17)
        {
            if (run) return "busy";
            var go = new GameObject("FortDebrisV2Proof"); DontDestroyOnLoad(go);
            run = go.AddComponent<FortDebrisV2Proof>(); run.both = thenL17;
            run.StartCoroutine(run.Seq());
            return "started";
        }

        IEnumerator Seq()
        {
            yield return Level();
            if (both && ZGame.I && ZGame.I.level != "L17")
            {
                Status = "loading L17";
                SceneManager.LoadScene("L17_CarPark");
                float w = Time.unscaledTime;
                while ((!ZGame.I || ZGame.I.level != "L17" || ZGame.I.World == null) && Time.unscaledTime - w < 240f) yield return null;
                yield return new WaitForSecondsRealtime(3f);
                yield return Level();
            }
            Status = "done"; run = null; Destroy(gameObject);
        }

        IEnumerator Level()
        {
            var g = ZGame.I; var W = g ? g.World : null;
            string lv = g ? g.level : "?"; dir = Base + "/" + lv; Status = "running " + lv;
            ZObject fort = null; if (W != null) foreach (var o in W.Objects) if (o.IsFort && o.Owner == Team.Blue && !o.Destroyed) { fort = o; break; }
            if (fort == null) { Log(lv + ": no blue fort"); yield break; }
            BuildingBreaker bb = null; var objF = typeof(BuildingBreaker).GetField("obj", BindingFlags.NonPublic | BindingFlags.Instance);
            float w0 = Time.unscaledTime;
            while (Time.unscaledTime - w0 < 60f)
            {
                foreach (var b in FindObjectsByType<BuildingBreaker>(FindObjectsSortMode.None)) if (objF != null && objF.GetValue(b) == fort) { bb = b; break; }
                if (bb && bb.IsPrepared) break;
                yield return new WaitForSecondsRealtime(0.5f);
            }
            if (!bb) { Log(lv + ": no breaker on the blue fort"); yield break; }
            Log("task_fort_debris_v2 " + lv + " " + Screen.width + "x" + Screen.height + ", ChunkLibrary " + BuildingBreaker.ChunkLibrary + " (loaded " + ConcreteChunkLib.Loaded + ", " + ConcreteChunkLib.All.Count + " chunks, " + ConcreteChunkLib.Heaps.Count + " heaps), cap fort " + BuildingBreaker.ChunkCapFort + " / other " + BuildingBreaker.ChunkCapOther + ", prepared " + bb.IsPrepared + ", gpu " + SystemInfo.graphicsDeviceName);
            Log("blue fort chunks: " + bb.ChunkStats);
            OzShakeGate.LogToConsole = lv == "L02"; int shake0 = OzShakeGate.Log.Count;
            var at = bb.transform.position;
            for (int i = 0; i < 3; i++) BuildingBreaker.LibCrashes[i] = 0;
            float k0 = Time.unscaledTime;
            fort.SetHealth(0); W.UpdateObjectHealth(fort);
            Log("KILL blue fort #" + fort.Id + " at " + at.ToString("F0"));
            while (!bb.Collapsed && Time.unscaledTime - k0 < 45f) yield return null;
            if (!bb.Collapsed) { Log("no collapse within 45 s"); yield break; }
            float tc = Time.unscaledTime;
            var dts = new List<float> { Time.unscaledDeltaTime * 1000f };
            Log("collapse " + (tc - k0).ToString("F1") + " s after the kill");
            float[] ft = { 0.5f, 1.5f, 4f }; int fi = 0; float maxDt = 0f, maxDtAt = 0f; int frames = 0;
            while (Time.unscaledTime - tc < 8f)
            {
                yield return new WaitForEndOfFrame();
                float e = Time.unscaledTime - tc; frames++;
                float dt = Time.unscaledDeltaTime * 1000f; if (dts.Count < 12) dts.Add(dt);
                if (e < 3f && dt > maxDt) { maxDt = dt; maxDtAt = e; }
                if (fi < ft.Length && e >= ft[fi])
                {
                    string tag = ft[fi].ToString("F1").Replace('.', '_');
                    ProofCapture.SaveJpg(dir + "/game_" + tag + ".jpg", 1920);
                    Shot(Low(at), dir + "/low_" + tag + ".jpg");
                    Log("t+" + ft[fi].ToString("F1") + " (" + e.ToString("F2") + "): " + Parts(bb) + ", fps " + (1f / Mathf.Max(1e-4f, Time.smoothDeltaTime)).ToString("F0") + ", cam " + (Camera.main ? Camera.main.transform.position.ToString("F0") + " pitch " + Camera.main.transform.eulerAngles.x.ToString("F0") : "-"));
                    fi++;
                }
            }
            Log("frame times (ms) from the collapse frame: " + string.Join(" ", dts.ConvertAll(x => x.ToString("F1")).ToArray()) + " | max in the first 3 s " + maxDt.ToString("F1") + " ms at +" + maxDtAt.ToString("F2") + " s, " + frames + " frames in 8 s");
            Log("crashes: L large " + BuildingBreaker.LibCrashes[0] + ", M / L thuds " + BuildingBreaker.LibCrashes[1] + ", S / XS knocks " + BuildingBreaker.LibCrashes[2]);
            var cnt = new SortedDictionary<string, int>();
            foreach (var e in OzSoundLog.Since(k0 - 0.2f)) { if (!(e.key.Contains("debris") || e.key.Contains("collapse") || e.key.Contains("concrete"))) continue; string key = e.key + " " + e.outcome; int n; cnt.TryGetValue(key, out n); cnt[key] = n + 1; }
            var parts = new List<string>(); foreach (var kv in cnt) parts.Add(kv.Key + " x" + kv.Value);
            Log("crash sounds (OzSoundLog): " + string.Join(", ", parts.ToArray()));
            if (lv == "L02")
            {
                var sl = new List<string>(); for (int i = shake0; i < OzShakeGate.Log.Count; i++) sl.Add(OzShakeGate.Log[i]);
                Log("shake gate (" + sl.Count + " requests since the kill):\n  " + string.Join("\n  ", sl.ToArray()));
                OzShakeGate.LogToConsole = false;
            }
            if (low) { Destroy(low.gameObject); low = null; }
        }

        string Parts(BuildingBreaker bb)
        {
            var f = typeof(BuildingBreaker).GetField("bigParts", BindingFlags.NonPublic | BindingFlags.Instance);
            var list = f != null ? f.GetValue(bb) as List<Rigidbody> : null; if (list == null) return "no parts list";
            int n = 0, moving = 0, sleeping = 0, air = 0; float maxV = 0f, maxSpin = 0f, maxH = 0f;
            var g = ZGame.I;
            foreach (var rb in list)
            {
                if (!rb || !rb.gameObject.activeInHierarchy) continue; n++;
                float v = rb.isKinematic ? 0f : rb.linearVelocity.magnitude; if (v > 0.3f) moving++; if (rb.isKinematic || rb.IsSleeping()) sleeping++;
                maxV = Mathf.Max(maxV, v); maxSpin = Mathf.Max(maxSpin, rb.isKinematic ? 0f : rb.angularVelocity.magnitude);
                var p = rb.position; float h = p.y - (g ? g.GroundY(p.x, p.z) : 0f); if (h > 1.5f) air++; maxH = Mathf.Max(maxH, h);
            }
            return "part chunks " + n + " (moving " + moving + ", settled " + sleeping + ", >1.5 m up " + air + ", max height " + maxH.ToString("F1") + " m, max speed " + maxV.ToString("F1") + " m/s, max spin " + maxSpin.ToString("F1") + " rad/s)";
        }

        Camera low;
        Camera Low(Vector3 at)
        {
            if (low) return low;
            var main = Camera.main;
            var go = new GameObject("FortDebrisV2LowCam"); low = go.AddComponent<Camera>();
            if (main) low.CopyFrom(main);
            low.enabled = false; low.fieldOfView = 50f; low.farClipPlane = 1500f;
            var d = main ? Vector3.ProjectOnPlane(main.transform.position - at, Vector3.up).normalized : Vector3.back;
            if (d.sqrMagnitude < 1e-4f) d = Vector3.back;
            d = Quaternion.Euler(0f, 35f, 0f) * d;
            float gy = ZGame.I ? ZGame.I.GroundY(at.x, at.z) : at.y;
            go.transform.position = new Vector3(at.x, gy, at.z) + d * 30f + Vector3.up * 4.5f;
            go.transform.LookAt(new Vector3(at.x, gy + 3f, at.z));
            var src = main ? main.GetUniversalAdditionalCameraData() : null; var dst = low.GetUniversalAdditionalCameraData();
            if (src && dst) { dst.renderPostProcessing = src.renderPostProcessing; dst.antialiasing = src.antialiasing; dst.antialiasingQuality = src.antialiasingQuality; dst.volumeLayerMask = src.volumeLayerMask; dst.requiresDepthTexture = true; dst.requiresColorTexture = true; }
            return low;
        }

        static void Shot(Camera c, string path)
        {
            if (!c) return;
            int w = 1920, h = 1080;
            var prev = c.targetTexture;
            var rt = RenderTexture.GetTemporary(w, h, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            c.targetTexture = rt; c.Render();
            var act = RenderTexture.active; RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); tex.Apply();
            RenderTexture.active = act; c.targetTexture = prev; RenderTexture.ReleaseTemporary(rt);
            File.WriteAllBytes(path, tex.EncodeToJPG(92)); Destroy(tex);
        }
    }
}
