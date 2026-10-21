// OpenZ (GPL-3.0) - Level 16: no loose leaf shapes and no trees without ground in the sky of a low shot.
// The level's plants are drawn for the game's steep camera: far away every 32 m cell of plants is ONE joined mesh thinned out to a
// scatter of leaf triangles (from above it reads as a crown), and the jungle that closes the map on all sides stands on ground
// only for the first 24 m outside the map (the rim's floor): beyond that its trees have no ground under them (from above only
// their crowns are seen). A LOW camera that shows the horizon sees both against the sky: leaf triangles hovering over the tree
// line and whole trees standing in the air.
// So, while a low shot can show the horizon (the mission-start flyover until its last shot begins: the same moment the painted
// sky is wanted, ZodFlyover.SkyWanted; or ForceOn for a picture taken with a low camera):
//  - every plant cell shows its full plants (the far, thinned mesh is not drawn), and
//  - plants that stand more than GroundMargin metres outside the map (no ground under them) are not drawn,
//  - the far fall's sheet of water (the part of the wall falls' sheet mesh that stands that far outside the map: a column 68 m
//    high whose cliff a low camera does not show) is not drawn; the four falls inside the map stay.
// With the last shot of the flyover (a cut, a steep camera again) everything is back exactly as it was. Nothing is built or
// destroyed, no rule is read: only what is drawn changes. Level 16 only; it attaches itself to that scene.
// Play check: return OpenZ.OzL16FlyoverFlora.Status();
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace OpenZ
{
    [DefaultExecutionOrder(210)]
    public sealed partial class OzL16FlyoverFlora : MonoBehaviour
    {
        public const string Level = "L16", FloraRoot = "L16_Flora", FallSheets = "L16_WallFallSheets";
        public static OzL16FlyoverFlora I { get; private set; }
        /// <summary>A picture with a low camera outside the flyover (the level card): hold the low-shot look on.</summary>
        public static bool ForceOn;
        /// <summary>True while the low-shot look holds every plant cell on its full plants.</summary>
        public static bool LowLookOn => I && I.on;
        /// <summary>The rim's floor reaches this far outside the map (m): plants farther out have no ground under them.</summary>
        public static float GroundMargin = 24f;
        /// <summary>Counters: how often the low-shot look came on, plant cells held on their full plants, plants not drawn, now.</summary>
        public static int Times, CellsNow, HiddenNow;
        /// <summary>Triangles of the far fall's sheet that a low shot does not draw (0 = the sheet mesh was not found or cannot be read).</summary>
        public static int FallCut;

        ZGame game; Vector2 map;
        LODGroup[] cells; readonly List<Renderer> hidden = new List<Renderer>(); bool on;
        MeshFilter fallMf; Mesh fallFull, fallNear; bool fallTried;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { I = null; ForceOn = false; Times = CellsNow = HiddenNow = FallCut = 0; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Boot() { SceneManager.sceneLoaded -= OnScene; SceneManager.sceneLoaded += OnScene; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AfterFirstScene() { TryAttach(SceneManager.GetActiveScene()); }

        static void OnScene(Scene s, LoadSceneMode m) { if (m == LoadSceneMode.Single) TryAttach(s); }

        static void TryAttach(Scene s)
        {
            if (!Application.isPlaying || I) return;
            var g = ZGame.I ? ZGame.I : FindFirstObjectByType<ZGame>();
            if (!g || g.level != Level) return;
            var go = new GameObject("OzL16FlyoverFlora");
            if (s.IsValid() && go.scene != s) SceneManager.MoveGameObjectToScene(go, s);
            go.AddComponent<OzL16FlyoverFlora>();
        }

        void Awake() { I = this; game = ZGame.I ? ZGame.I : FindFirstObjectByType<ZGame>(); }
        void OnDisable() { Set(false); }
        void OnDestroy() { if (I == this) I = null; if (fallNear) Destroy(fallNear); DestroyFar(); }

        /// <summary>How far p stands outside the map (m, 0 = inside).</summary>
        float Outside(Vector3 p) => Mathf.Max(Mathf.Max(0f, Mathf.Max(-p.x, p.x - map.x)), Mathf.Max(0f, Mathf.Max(p.z, -map.y - p.z)));

        /// <summary>The wall falls' sheet mesh without the triangles that stand more than GroundMargin outside the map (made once).</summary>
        void BuildFall()
        {
            var go = GameObject.Find(FallSheets); var mf = go ? go.GetComponent<MeshFilter>() : null; var m = mf ? mf.sharedMesh : null;
            if (!m || m.subMeshCount != 1) return;
            if (!m.isReadable) { Debug.LogWarning("[OpenZ L16 flyover flora] the sheet mesh of the wall falls cannot be read: the far fall stays in the low shots"); return; }
            var v = m.vertices; var t = m.triangles; var tr = go.transform; var far = new bool[v.Length];
            for (int i = 0; i < v.Length; i++) far[i] = Outside(tr.TransformPoint(v[i])) > GroundMargin;
            var keep = new List<int>(t.Length); int cut = 0;
            for (int i = 0; i + 2 < t.Length; i += 3)
            {
                if (far[t[i]] || far[t[i + 1]] || far[t[i + 2]]) { cut++; continue; }
                keep.Add(t[i]); keep.Add(t[i + 1]); keep.Add(t[i + 2]);
            }
            if (cut == 0 || keep.Count == 0) return;
            fallNear = Instantiate(m); fallNear.name = m.name + " (low shot)"; fallNear.hideFlags = HideFlags.DontSave;
            fallNear.SetTriangles(keep, 0);
            fallMf = mf; fallFull = m; FallCut = cut;
        }

        void LateUpdate()
        {
            var fly = ZodFlyover.Live;
            // a low camera shows the horizon: the flyover until its last shot, an ending's scene (it holds the painted sky on), or ForceOn
            bool want = ForceOn || (game && game.level == Level && (FlybySky.ForceOn || (fly != null && fly.Active && fly.Cine && fly.SkyWanted)));
            if (want != on) Set(want);
            if (on) TickFar();
        }

        void Set(bool want)
        {
            if (want == on) return;
            if (want)
            {
                if (cells == null)
                {
                    var root = GameObject.Find(FloraRoot);
                    cells = root ? root.GetComponentsInChildren<LODGroup>(true) : new LODGroup[0];
                    var rts = FindFirstObjectByType<RtsCamera>();
                    map = rts ? rts.mapSizeMeters : new Vector2(208f, 256f);
                }
                hidden.Clear(); int held = 0;
                foreach (var lg in cells)
                {
                    if (!lg) continue;
                    lg.ForceLOD(0); held++;
                    var lods = lg.GetLODs();
                    if (lods.Length == 0 || lods[0].renderers == null) continue;
                    foreach (var r in lods[0].renderers)
                    {
                        if (!r || !r.enabled) continue;
                        var p = r.bounds.center;
                        float outX = Mathf.Max(0f, Mathf.Max(-p.x, p.x - map.x)), outZ = Mathf.Max(0f, Mathf.Max(p.z, -map.y - p.z));
                        if (Mathf.Max(outX, outZ) <= GroundMargin) continue;
                        r.enabled = false; hidden.Add(r);
                    }
                }
                if (!fallTried) { fallTried = true; BuildFall(); }
                if (fallMf && fallNear) fallMf.sharedMesh = fallNear;
                ShowFar(true);
                on = true; Times++; CellsNow = held; HiddenNow = hidden.Count;
                Debug.Log($"[OpenZ L16 flyover flora] low shot: {held} plant cells show their full plants, {hidden.Count} plants without ground (more than {GroundMargin:F0} m outside the map) are not drawn, the far fall's sheet is not drawn ({FallCut} triangles)");
            }
            else
            {
                foreach (var r in hidden) if (r) r.enabled = true;
                hidden.Clear();
                if (fallMf && fallFull) fallMf.sharedMesh = fallFull;
                ShowFar(false);
                if (cells != null) foreach (var lg in cells) if (lg) lg.ForceLOD(-1);
                on = false; CellsNow = 0; HiddenNow = 0;
                Debug.Log("[OpenZ L16 flyover flora] the steep camera again: every plant cell back on its own distance rule, every plant drawn");
            }
        }

        /// <summary>One-line state for the Play check.</summary>
        public static string Status() => !I ? "OzL16FlyoverFlora: not in this scene (level 16 only)"
            : $"OzL16FlyoverFlora: {(I.on ? "low-shot look ON" : "off")}, cells held {CellsNow}, plants not drawn {HiddenNow}, came on {Times} times, force {ForceOn}";
    }
}
