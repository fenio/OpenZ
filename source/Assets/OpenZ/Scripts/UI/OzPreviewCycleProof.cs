using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Video;

namespace OpenZ
{
    /// <summary>
    /// menu flyby decodes. Start the exe with <c>-oz-preview-cycle</c> (add <c>-oz-preview-cycle-quit</c> to close it when done):
    /// in the main menu every Resources clip of every level (<c>OpenZ/Menu/&lt;id&gt;_preview</c>, <c>_preview_2</c> .. <c>_9</c>) is
    /// prepared on its own VideoPlayer, one at a time, played for 3 s, and logged to Player.log:
    /// <c>[OpenZ PreviewCycle] L17 #2 L17_preview_2 2560x1600 60 fps 17.0 s: LIVE 176 frames in 3.0 s (prepare 0.42 s)</c>
    /// or STILL with the reason (error text, prepare timeout, no frame). Ends with a summary line. Nothing runs without the argument.
    /// </summary>
    public sealed class OzPreviewCycleProof : MonoBehaviour
    {
        static bool Arg(string a) { foreach (var s in System.Environment.GetCommandLineArgs()) if (s.Equals(a, System.StringComparison.OrdinalIgnoreCase)) return true; return false; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (!Arg("-oz-preview-cycle")) return;
            var go = new GameObject("OzPreviewCycleProof"); DontDestroyOnLoad(go); go.AddComponent<OzPreviewCycleProof>();
        }

        /// <summary></summary>
        public static void Run() { var go = new GameObject("OzPreviewCycleProof"); DontDestroyOnLoad(go); go.AddComponent<OzPreviewCycleProof>(); }

        IEnumerator Start()
        {
            yield return new WaitForSecondsRealtime(2f);                                                    // the menu builds its own previews first
            var rt = new RenderTexture(256, 160, 0, RenderTextureFormat.ARGB32) { name = "OzPreviewCycle" }; rt.Create();
            int live = 0, total = 0; var bad = new List<string>();
            Debug.Log($"[OpenZ PreviewCycle] start ({(Application.isEditor ? "editor" : "player")} {Application.unityVersion}, {SystemInfo.operatingSystem}, GPU {SystemInfo.graphicsDeviceName})");
            foreach (var d in OzLevelDef.All)
            {
                var clips = new List<VideoClip>();
                var c0 = Resources.Load<VideoClip>($"OpenZ/Menu/{d.Id}_preview");
                if (c0) { clips.Add(c0); for (int k = 2; k <= 9; k++) { var c = Resources.Load<VideoClip>($"OpenZ/Menu/{d.Id}_preview_{k}"); if (!c) break; clips.Add(c); } }
                if (clips.Count == 0) { Debug.Log($"[OpenZ PreviewCycle] {d.Id}: NO Resources clip (the menu falls back to StreamingAssets menu_{d.Id}.mp4 or the still)"); bad.Add(d.Id + " none"); continue; }
                for (int i = 0; i < clips.Count; i++)
                {
                    total++;
                    var clip = clips[i];
                    var go = new GameObject("Cycle_" + clip.name); go.transform.SetParent(transform, false);
                    var vp = go.AddComponent<VideoPlayer>();
                    vp.playOnAwake = false; vp.waitForFirstFrame = true; vp.skipOnDrop = true; vp.isLooping = false;
                    vp.audioOutputMode = VideoAudioOutputMode.None; vp.renderMode = VideoRenderMode.RenderTexture; vp.targetTexture = rt;
                    vp.source = VideoSource.VideoClip; vp.clip = clip;
                    string err = null; bool prepared = false;
                    vp.errorReceived += (v, m) => err = m;
                    vp.prepareCompleted += v => prepared = true;
                    float t0 = Time.unscaledTime; vp.Prepare();
                    while (!prepared && err == null && Time.unscaledTime - t0 < 8f) yield return null;
                    float prep = Time.unscaledTime - t0;
                    string head = $"[OpenZ PreviewCycle] {d.Id} #{i + 1} {clip.name} {clip.width}x{clip.height} {clip.frameRate:0.##} fps {clip.length:0.0} s";
                    if (!prepared)
                    {
                        Debug.LogWarning($"{head}: STILL ({(err != null ? "error: " + err : "prepare timed out after 8 s")})"); bad.Add(clip.name);
                        Destroy(go); continue;
                    }
                    vp.Play();
                    float t1 = Time.unscaledTime; long f0 = -1;
                    while (Time.unscaledTime - t1 < 3f && err == null) { if (f0 < 0 && vp.frame >= 0) f0 = vp.frame; yield return null; }
                    long frames = f0 >= 0 ? vp.frame - f0 : 0;
                    bool ok = err == null && frames >= 30;
                    if (ok) live++; else bad.Add(clip.name);
                    var msg = $"{head}: {(ok ? "LIVE" : "STILL")} {frames} frames in 3.0 s (prepare {prep:0.00} s){(err != null ? ", error: " + err : "")}{(!ok && err == null ? ", no frames decoded after Play (decoder / codec / level?)" : "")}";
                    if (ok) Debug.Log(msg); else Debug.LogWarning(msg);
                    vp.Stop(); Destroy(go);
                    yield return null;
                }
            }
            rt.Release(); Destroy(rt);
            Debug.Log($"[OpenZ PreviewCycle] summary: {live}/{total} LIVE{(bad.Count > 0 ? "; STILL: " + string.Join(", ", bad) : "")}");
            if (Arg("-oz-preview-cycle-quit") && !Application.isEditor) Application.Quit();
            Destroy(gameObject);
        }
    }
}
