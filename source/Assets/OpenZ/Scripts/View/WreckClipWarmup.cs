using System.Collections;
using System.Diagnostics;
using UnityEngine;
using UnityEngine.SceneManagement;
using Debug = UnityEngine.Debug;

namespace OpenZ
{
    /// <summary>
    /// soak_hitch_fix: fallback for wreck fragment sets the editor has NOT cut (WreckClipBaked missing or made with another
    /// SquashOuter). In the level's first frames (the load tail, under the briefing's letterbox) every OZ_WreckFragments holder of the
    /// thigh set cut at its first break made 128 / 151 / 251 ms frames at L17 c3 t+169-170). With every set baked it finds nothing
    /// to do and logs "0 cut". Units of a type that first appears later (built mid-match) still take the runtime cut: the bake is
    /// the fix, this is the net. View only: the sim is never touched.
    /// </summary>
    public class WreckClipWarmup : MonoBehaviour
    {
        /// <summary>Off = no warm-up (A/B).</summary>
        public static bool On = true;
        public static string Last = "not run";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        static void OnSceneLoaded(Scene s, LoadSceneMode m)
        {
            if (!On || m != LoadSceneMode.Single || s.name == "MainMenu" || s.name == "Intro") return;
            var go = new GameObject("OZ_WreckClipWarmup") { hideFlags = HideFlags.HideAndDontSave };
            go.AddComponent<WreckClipWarmup>();
        }

        IEnumerator Start()
        {
            float until = Time.realtimeSinceStartup + 30f;
            while ((!ZGame.I || ZGame.I.World == null) && Time.realtimeSinceStartup < until) yield return null;
            yield return null;                                                                                  // the views of the first units exist now
            if (ZGame.I && ZGame.I.World != null) Run();
            Destroy(gameObject);
        }

        static void Run()
        {
            var sw = Stopwatch.StartNew();
            int holders = 0, baked = 0, cut = 0;
            foreach (var v in FindObjectsByType<UnitView>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (!v) continue;
                foreach (var t in v.GetComponentsInChildren<Transform>(true))
                {
                    if (t.name != "OZ_WreckFragments") continue;
                    holders++;
                    if (WreckPieces.IsBaked(t)) { baked++; continue; }
                    if (WreckPieces.Prewarm(t)) cut++;
                }
            }
            Last = $"{holders} fragment holder(s) on the map, {baked} baked, {cut} set(s) cut now in {sw.Elapsed.TotalMilliseconds:0.0} ms";
            Debug.Log("[OpenZ Wreck] clip warm-up: " + Last + (cut > 0 ? " (not baked: run OpenZ/Wreck/Rebake clipped fragments)" : ""));
        }
    }
}
