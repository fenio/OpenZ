// OpenZ (GPL-3.0) - Level 16: nothing that the level draws by distance changes inside the picture while the player scrolls.
// The level's plants stand in 32 m cells, each with its full plants and a far, thinned version, and its rocks, ruins and buildings
// have nearer and farther versions too; the engine chose between them by how large a thing is on screen, so at the top of the
// picture a cell came to its full plants in plain sight. Here the choice is made by PLACE instead: the ground the game camera sees
// (its four corner rays on the ground), widened by one cell on every side, holds the full versions; whatever lies outside has its
// far version. A change therefore always happens at least one cell's width outside the picture, on every side and at every zoom of
// the normal camera up to FarAbove; higher up (the wide overview) every plant cell in view shows its far version, as the level was
// made for, and every other thing in view its second version (the whole view changes together during the zoom, never while
// scrolling). A second view of the level (a window's camera, a picture's camera) keeps full versions round itself.
// While a low shot holds every cell on its full plants (OzL16FlyoverFlora) this stands back. Drawn only: no rule reads it.
// Play check: return OpenZ.OzL16DrawAhead.Status();
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace OpenZ
{
    [DefaultExecutionOrder(215)]
    public sealed class OzL16DrawAhead : MonoBehaviour
    {
        public const string Level = "L16", FloraRoot = "L16_Flora";
        public static OzL16DrawAhead I { get; private set; }
        /// <summary>Off = the engine's own rule again (for a before / after count).</summary>
        public static bool On = true;
        /// <summary>Metres beyond the picture's edge that already hold the full versions (one plant cell), the camera height above
        /// which the plant cells in view show their far version, and the reach of a second view's full versions.</summary>
        public static float Margin = 32f, FarAbove = 110f, FarBand = 6f, OtherViewReach = 110f;
        /// <summary>Counters: things watched, shown full now, changes made in all, and of those the ones that lay inside the picture.</summary>
        public static int Watched, FullNow, Changes, ChangesInPicture, ZoomSwitches;
        /// <summary>Counter: passes over a switched-off group (left alone: the engine refuses a forced level there).</summary>
        public static int Skipped;
        static bool Live(LODGroup g) => g.enabled && g.gameObject.activeInHierarchy;

        ZGame game; RtsCamera rts; Camera cam;
        LODGroup[] groups; bool[] flora; Bounds[] box; int[] last, state;      // state: -2 not set, -1 the engine's rule, 0.. forced
        readonly Plane[] planes = new Plane[6]; bool wide, held, wasOn; Vector3 camWas; bool camSeen;
        readonly List<Vector3> others = new List<Vector3>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { I = null; On = true; Watched = FullNow = Changes = ChangesInPicture = ZoomSwitches = Skipped = 0; }

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
            var go = new GameObject("OzL16DrawAhead");
            if (s.IsValid() && go.scene != s) SceneManager.MoveGameObjectToScene(go, s);
            go.AddComponent<OzL16DrawAhead>();
        }

        void Awake() { I = this; game = ZGame.I ? ZGame.I : FindFirstObjectByType<ZGame>(); }
        void OnDisable() { Release(); }
        void OnDestroy() { if (I == this) I = null; }

        void Collect()
        {
            var env = GameObject.Find("Environment"); var all = new List<LODGroup>();
            if (env) all.AddRange(env.GetComponentsInChildren<LODGroup>(true));
            var zo = GameObject.Find("ZodObjects");
            if (zo) foreach (var lg in zo.GetComponentsInChildren<LODGroup>(true)) if (!all.Contains(lg)) all.Add(lg);
            var fr = GameObject.Find(FloraRoot); var ft = fr ? fr.transform : null;
            if (ft) foreach (var lg in ft.GetComponentsInChildren<LODGroup>(true)) if (!all.Contains(lg)) all.Add(lg);
            groups = all.ToArray(); int n = groups.Length;
            flora = new bool[n]; box = new Bounds[n]; last = new int[n]; state = new int[n];
            for (int i = 0; i < n; i++)
            {
                var lg = groups[i]; state[i] = -2; last[i] = Mathf.Max(0, lg.lodCount - 1);
                flora[i] = ft && lg.transform.IsChildOf(ft);
                bool any = false; Bounds b = new Bounds(lg.transform.position, Vector3.one);
                foreach (var lod in lg.GetLODs())
                    if (lod.renderers != null) foreach (var r in lod.renderers) { if (!r) continue; if (any) b.Encapsulate(r.bounds); else { b = r.bounds; any = true; } }
                box[i] = b;
            }
            Watched = n;
        }

        void Release()
        {
            if (groups == null) return;
            for (int i = 0; i < groups.Length; i++) { if (groups[i] && state[i] >= 0 && Live(groups[i])) groups[i].ForceLOD(-1); state[i] = -2; }
            FullNow = 0;
        }

        void LateUpdate()
        {
            if (!game || game.level != Level) return;
            if (groups == null) Collect();
            if (!On) { if (wasOn) Release(); wasOn = false; return; }
            wasOn = true;
            // a low shot holds every cell on its full plants and gives them back when it ends: start again from nothing after it
            if (OzL16FlyoverFlora.LowLookOn)
            {
                if (!held)
                {   // the rocks and ruins go by the engine's own rule for the low camera; the plant cells are the low shot's
                    held = true;
                    for (int i = 0; i < state.Length; i++) { if (!flora[i] && state[i] >= 0 && groups[i] && Live(groups[i])) groups[i].ForceLOD(-1); state[i] = -2; }
                }
                return;
            }
            held = false;
            if (!rts) rts = FindFirstObjectByType<RtsCamera>();
            if (!cam) cam = rts ? rts.GetComponentInChildren<Camera>() : null;
            if (!cam) cam = Camera.main;
            if (!cam) return;

            // the ground the camera sees, widened by the margin
            Vector3 cp = cam.transform.position; float xMin = cp.x, xMax = cp.x, zMin = cp.z, zMax = cp.z;
            for (int k = 0; k < 4; k++)
            {
                var ray = cam.ViewportPointToRay(new Vector3(k % 2, k / 2, 0f));
                float t = ray.direction.y < -0.02f ? Mathf.Min(900f, -ray.origin.y / ray.direction.y) : 900f;
                var p = ray.origin + ray.direction * t;
                xMin = Mathf.Min(xMin, p.x); xMax = Mathf.Max(xMax, p.x); zMin = Mathf.Min(zMin, p.z); zMax = Mathf.Max(zMax, p.z);
            }
            xMin -= Margin; xMax += Margin; zMin -= Margin; zMax += Margin;
            float h = rts ? rts.height : cp.y;
            bool wideWas = wide;
            if (wide) { if (h < FarAbove - FarBand) wide = false; } else if (h > FarAbove + FarBand) wide = true;
            bool zoomSwitch = wide != wideWas;

            // any other view of the level into a picture of its own keeps full versions round itself
            others.Clear();
            foreach (var c in Camera.allCameras)
                if (c && c != cam && c.targetTexture && (c.cullingMask & cam.cullingMask) != 0 && c.transform.position.y < 40f) others.Add(c.transform.position);

            GeometryUtility.CalculateFrustumPlanes(cam, planes);
            // a jump of the camera (the map's click, a hotkey, a cut) changes the whole picture at once: that is not a change seen while scrolling
            bool jump = !camSeen || (cp - camWas).sqrMagnitude > 12f * 12f; camWas = cp; camSeen = true;
            int full = 0;
            for (int i = 0; i < groups.Length; i++)
            {
                var lg = groups[i]; if (!lg) continue;
                if (!Live(lg)) { state[i] = -2; Skipped++; continue; }                 // switched off (a felled tree, a hidden plant): never forced; set again when it is back
                var b = box[i]; Vector3 mn = b.min, mx = b.max;
                bool near = mx.x >= xMin && mn.x <= xMax && mx.z >= zMin && mn.z <= zMax;
                bool other = false;
                for (int o = 0; o < others.Count && !other; o++)
                {
                    float dx = Mathf.Max(0f, Mathf.Max(mn.x - others[o].x, others[o].x - mx.x)), dz = Mathf.Max(0f, Mathf.Max(mn.z - others[o].z, others[o].z - mx.z));
                    other = dx * dx + dz * dz <= OtherViewReach * OtherViewReach;
                }
                int want;
                if (other) want = 0;
                else if (!near) want = last[i];
                else if (!wide) want = 0;
                else want = flora[i] ? last[i] : Mathf.Min(1, last[i]);      // the wide overview: plants far, everything else on its second version
                if (want == 0) full++;
                if (want == state[i]) continue;
                // the wide overview's switch belongs to the zoom; every other change must lie outside the picture
                if (state[i] != -2)
                {
                    Changes++;
                    if (zoomSwitch && near) ZoomSwitches++;
                    else if (!jump && GeometryUtility.TestPlanesAABB(planes, b)) ChangesInPicture++;
                }
                lg.ForceLOD(want); state[i] = want;
            }
            FullNow = full;
        }

        /// <summary>One-line state for the Play check.</summary>
        public static string Status() => !I ? "OzL16DrawAhead: not in this scene (level 16 only)"
            : $"OzL16DrawAhead: {(On ? (I.held ? "standing back (a low shot holds the plants)" : I.wide ? "on, wide overview" : "on") : "off")}, {Watched} things watched, {FullNow} full now, changes {Changes}, of them inside the picture {ChangesInPicture} (the wide overview's own switches during a zoom: {ZoomSwitches}), switched-off groups passed over {Skipped}";
    }
}
