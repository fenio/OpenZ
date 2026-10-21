// OpenZ (GPL-3.0) - Level 16: the shadows of the trees and of the scenery round the map stay when the camera zooms far out.
// Above OzL16DrawAhead.FarAbove the whole view shows its second versions (each plant cell's one joined far mesh, the second
// version of the scenery round the map), and those were made without shadows: every tree shadow in the picture went out at once
// during the zoom and came back on the way in. While the game camera stands at or above the height where that switch can come
// (FarAbove - FarBand), the second version of every group whose full version casts a shadow casts one too, the way its full
// version does; lower down they are as they were, so the standard view draws exactly what it drew. The far shapes are thinned,
// so their shadows are a little lighter, never gone. Drawn only: no rule reads it.
// Play check: return OpenZ.OzL16FarShadows.Status();
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace OpenZ
{
    [DefaultExecutionOrder(216)]
    public sealed class OzL16FarShadows : MonoBehaviour
    {
        public static OzL16FarShadows I { get; private set; }
        /// <summary>Off = the second versions without shadows, as before (for a before / after count).</summary>
        public static bool On = true;
        /// <summary>Metres under the switch-on height at which the far shadows go off again (no flutter while the zoom rests there).</summary>
        public static float Band = 6f;
        /// <summary>Counters: second-version renderers without a shadow whose full version has one, of them casting now, switches made.</summary>
        public static int Watched, CastingNow, Switches;

        ZGame game; RtsCamera rts; Camera cam;
        Renderer[] rends; ShadowCastingMode[] mode; bool casting;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { I = null; On = true; Watched = CastingNow = Switches = 0; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Boot() { SceneManager.sceneLoaded -= OnScene; SceneManager.sceneLoaded += OnScene; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AfterFirstScene() { TryAttach(SceneManager.GetActiveScene()); }

        static void OnScene(Scene s, LoadSceneMode m) { if (m == LoadSceneMode.Single) TryAttach(s); }

        static void TryAttach(Scene s)
        {
            if (!Application.isPlaying || I) return;
            var g = ZGame.I ? ZGame.I : FindFirstObjectByType<ZGame>();
            if (!g || g.level != OzL16DrawAhead.Level) return;
            var go = new GameObject("OzL16FarShadows");
            if (s.IsValid() && go.scene != s) SceneManager.MoveGameObjectToScene(go, s);
            go.AddComponent<OzL16FarShadows>();
        }

        void Awake() { I = this; game = ZGame.I ? ZGame.I : FindFirstObjectByType<ZGame>(); }
        void OnDisable() { if (casting) Set(false); }
        void OnDestroy() { if (I == this) I = null; }

        /// <summary>The groups OzL16DrawAhead switches (the scenery, the map's objects, the plant cells): the renderers of their second
        /// version that cast no shadow while their full version casts one, each with the full version's way of casting.</summary>
        void Collect()
        {
            var seen = new HashSet<LODGroup>(); var list = new List<Renderer>(); var how = new List<ShadowCastingMode>(); var taken = new HashSet<Renderer>();
            foreach (var name in new[] { "Environment", "ZodObjects", OzL16DrawAhead.FloraRoot })
            {
                var root = GameObject.Find(name); if (!root) continue;
                foreach (var lg in root.GetComponentsInChildren<LODGroup>(true))
                {
                    if (!lg || !seen.Add(lg) || lg.lodCount < 2) continue;
                    var lods = lg.GetLODs();
                    var full = lods[0].renderers; var second = lods[1].renderers;
                    if (full == null || second == null) continue;
                    var cast = ShadowCastingMode.Off;
                    foreach (var r in full) if (r && r.shadowCastingMode != ShadowCastingMode.Off && r.shadowCastingMode != ShadowCastingMode.ShadowsOnly) { cast = r.shadowCastingMode; break; }
                    if (cast == ShadowCastingMode.Off) continue;
                    foreach (var r in second)
                    {
                        if (!r || r.shadowCastingMode != ShadowCastingMode.Off || System.Array.IndexOf(full, r) >= 0 || !taken.Add(r)) continue;
                        list.Add(r); how.Add(cast);
                    }
                }
            }
            rends = list.ToArray(); mode = how.ToArray(); Watched = rends.Length;
        }

        void Set(bool on)
        {
            casting = on; int n = 0;
            for (int i = 0; rends != null && i < rends.Length; i++)
            {
                var r = rends[i]; if (!r) continue;
                var m = on ? mode[i] : ShadowCastingMode.Off;
                if (r.shadowCastingMode != m) r.shadowCastingMode = m;
                if (on) n++;
            }
            CastingNow = n; Switches++;
        }

        void LateUpdate()
        {
            if (!game || game.level != OzL16DrawAhead.Level) return;
            if (rends == null) Collect();
            if (!rts) rts = FindFirstObjectByType<RtsCamera>();
            if (!cam) cam = rts ? rts.GetComponentInChildren<Camera>() : null;
            if (!cam) cam = Camera.main;
            if (!cam) return;
            // the same height OzL16DrawAhead reads; on from the lowest height at which its wide overview can be in force
            float h = rts ? rts.height : cam.transform.position.y, at = OzL16DrawAhead.FarAbove - OzL16DrawAhead.FarBand;
            bool want = On && (casting ? h >= at - Band : h >= at);
            if (want != casting) Set(want);
        }

        /// <summary>One-line state for the Play check.</summary>
        public static string Status() => !I ? "OzL16FarShadows: not in this scene (level 16 only)"
            : $"OzL16FarShadows: {(On ? "on" : "off")}, far shadows {(I.casting ? "cast" : "not cast (camera low)")}, {Watched} second versions watched, {CastingNow} casting now, switches {Switches}, " +
              $"camera {(I.rts ? I.rts.height : -1f):0} m, on from {OzL16DrawAhead.FarAbove - OzL16DrawAhead.FarBand:0} m, off under {OzL16DrawAhead.FarAbove - OzL16DrawAhead.FarBand - Band:0} m";
    }
}
