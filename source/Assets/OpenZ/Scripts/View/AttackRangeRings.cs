using System.Collections.Generic;
using OpenZ.Sim;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace OpenZ
{
    /// <summary>
    /// dotted circle in the owner's colour (ZObject::RenderAttackRadius, zobject.cpp:702-777, called for the whole selection in
    /// zplayer.cpp:2167-2171): 10 dots per quadrant (PI/20 apart) at attack_radius + 3 px round the unit centre, 2 x 2 px,
    /// crawling PI/20 rad per second of game time (zobject.cpp:1032-1045), and a dot that falls inside another selected unit's
    /// radius is not drawn (WithinAttackRadiusOf, zobject.cpp:877-885), so a group draws one outline.
    /// unit type for a group (units of one type standing together share one ring at their middle, a type spread over the map
    /// gets one ring per cluster); radius = the sim's own AttackRadius (so an APC uses its crew's, zobject.cpp:182 / vapc.cpp);
    /// dots lie ON the ground (each dot's height is sampled from ZGame.GroundY: terrain, bridge decks), lifted 12 cm and drawn
    /// with OpenZ/Overlay (depth-tested so units cover them, polygon offset, no depth write): no z-fighting, no clipping into
    /// slopes. One pooled dynamic mesh per ring (13-vertex feathered dots), no allocations per frame, nothing when idle.
    /// Trigger: a new selection (PlayerInput.SelectTime: click / box release, group recall, Ctrl+R/V/C/A, R/V/G cycling) or
    /// units added to the selection outside a box drag (HUD alert tab, minimap). Dots use the team's ground-disc colour (HDR, blooms).
    /// </summary>
    [DefaultExecutionOrder(75)]
    public partial class AttackRangeRings : MonoBehaviour   // a gun's ring and the gun being placed: AttackRangeRings.Placement.cs (this same ring); its blind sectors: AttackRangeRings.BlindSectors.cs
    {
        public static AttackRangeRings I { get; private set; }
        /// <summary>Adds itself next to PlayerInput in every loaded scene (set false to add it by hand).</summary>
        public static bool AutoAttach = true;

        public PlayerInput input;
        [Tooltip("OpenZ/Overlay material (taken from SelectionOverlay when empty)")] public Material overlayMaterial;

        [Header("Timing, seconds (expands from the unit, holds ~1.5 s, fades)")]
        public float expandTime = 0.38f;
        public float holdTime = 1.5f, fadeTime = 0.55f, dropFade = 0.2f;

        [Header("Dots")]
        [Tooltip("Zod: 40 dots on every ring (zobject.cpp:712-713); OpenZ keeps ~this spacing (m) so a big ring does not read sparse")] public float dotSpacing = 1.25f;
        public int minDots = 40, maxDots = 160;
        [Tooltip("dot size (m) at 40 m camera distance; grows with the camera distance within zoomScale")] public float dotLength = 0.46f, dotWidth = 0.2f;
        public Vector2 zoomScale = new Vector2(0.8f, 2.6f);
        [Tooltip("above the sampled ground (m): clear of the terrain, still under the units' feet")] public float lift = 0.12f;
        [Tooltip("HDR gain of the dot colour (bloom)")] public float glow = 1.7f;
        [Range(0f, 1f)] public float alpha = 0.95f;
        [Tooltip("Zod crawl: PI/20 rad = 9 deg per second (zobject.cpp:1040)")] public float crawlDegPerSec = 9f;

        [Header("Grouping")]
        [Tooltip("units of one type (and range) closer than this (m) to a ring's middle share it")] public float mergeDistance = 5f;
        [Tooltip("Zod WithinAttackRadiusOf: dots inside another ring of the SAME type are hidden (one outline per group); rings of other types stay whole so every type's range reads")]
        public bool unionSameType = true;

        const int V = 13, T = 18;                                      // feathered dot: centre + 6 inner + 6 outer, 18 triangles

        class Ring
        {
            public string type; public Team team; public int radiusPx; public float radius;
            public readonly List<ZObject> units = new List<ZObject>();
            public Vector3 center; public float t0, dropAt = -1f, curR, a; public int n;
            public Mesh mesh; public Vector3[] v; public Color32[] c;
            public float[] h; public Vector3 hAt; public float hR = -1f;
            public bool live;
        }

        readonly List<Ring> rings = new List<Ring>();
        readonly Stack<Ring> pool = new Stack<Ring>();
        readonly HashSet<int> lastSel = new HashSet<int>(), curSel = new HashSet<int>();
        readonly Dictionary<Team, Material> mats = new Dictionary<Team, Material>();
        static readonly Vector2[] Hex = new Vector2[6];
        float lastSelectTime = -10f;
        ZGame game;

        // ------------------------------------------------------------------ install
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot() { SceneManager.sceneLoaded -= OnScene; SceneManager.sceneLoaded += OnScene; Attach(); }
        static void OnScene(Scene s, LoadSceneMode m) => Attach();
        static void Attach()
        {
            if (!AutoAttach) return;
            var pi = Object.FindFirstObjectByType<PlayerInput>();
            if (pi && !pi.GetComponent<AttackRangeRings>()) pi.gameObject.AddComponent<AttackRangeRings>();
        }

        void Awake()
        {
            I = this;
            if (!input) input = GetComponent<PlayerInput>();
            for (int i = 0; i < 6; i++) { float a = i * Mathf.PI / 3f; Hex[i] = new Vector2(Mathf.Cos(a), Mathf.Sin(a)); }
        }

        void OnDestroy()
        {
            foreach (var r in rings) if (r.mesh) Destroy(r.mesh);
            foreach (var r in pool) if (r.mesh) Destroy(r.mesh);
            foreach (var kv in mats) if (kv.Value) Destroy(kv.Value);
            BlindDestroy();                                                                                // the blind sectors' meshes and material (AttackRangeRings.BlindSectors.cs)
            if (I == this) I = null;
        }

        Material Mat(Team t)
        {
            if (mats.TryGetValue(t, out var m) && m) return m;
            var baseMat = overlayMaterial;
            if (!baseMat) { var so = GetComponent<SelectionOverlay>(); if (!so) so = Object.FindFirstObjectByType<SelectionOverlay>(); if (so) baseMat = so.overlayMaterial; }
            if (!baseMat) { var sh = Shader.Find("OpenZ/Overlay"); if (!sh) return null; baseMat = new Material(sh); }
            // ground-disc team colours (TeamColors: brighter than the paint so a ring reads on sand and concrete)
            Color c = t == Team.Red ? TeamColors.DiscRed : t == Team.Blue ? TeamColors.DiscBlue : TeamColors.DiscEmpty;
            m = new Material(baseMat) { name = "OZ_RangeRing_" + t };
            m.SetColor("_Color", new Color(c.r * glow, c.g * glow, c.b * glow, 1f));
            m.SetFloat("_ZTest", (float)CompareFunction.LessEqual);                                      // under the units (they cover it)
            mats[t] = m;
            return m;
        }

        // ------------------------------------------------------------------ public
        /// <summary>Plays the rings for the current selection now (context menu, stills, tests).</summary>
        [ContextMenu("Show range rings for the selection")]
        public void ShowForSelection() { if (input) Rebuild(input.Selection); }

        // ------------------------------------------------------------------ per frame
        void LateUpdate()
        {
            if (!game) game = input && input.game ? input.game : ZGame.I;
            if (!input || !game || game.World == null) return;
            var sel = input.Selection;
            // what changed? (a box drag grows the selection every frame: wait for its release, which sets SelectTime)
            curSel.Clear(); bool added = false;
            foreach (var o in sel) if (o != null && !o.Destroyed) { curSel.Add(o.Id); if (!lastSel.Contains(o.Id)) added = true; }
            bool reselect = input.SelectTime > lastSelectTime + 1e-4f;
            lastSelectTime = input.SelectTime;
            var mouse = UnityEngine.InputSystem.Mouse.current;
            bool dragging = mouse != null && mouse.leftButton.isPressed;
            lastSel.Clear(); foreach (var id in curSel) lastSel.Add(id);
            if (curSel.Count > 0 && (reselect || (added && !dragging))) Rebuild(sel);
            else if (curSel.Count == 0) DropAll();

            if (rings.Count == 0) return;
            float now = Time.unscaledTime;
            var cam = Camera.main;
            for (int i = rings.Count - 1; i >= 0; i--)
            {
                var r = rings[i];
                // members: alive and still selected (a unit leaves the selection when it dies or changes owner)
                for (int u = r.units.Count - 1; u >= 0; u--) { var o = r.units[u]; if (o == null || o.Destroyed || !curSel.Contains(o.Id)) r.units.RemoveAt(u); }
                if (r.units.Count == 0 && r.dropAt < 0f) r.dropAt = now;
                float age = now - r.t0, life = expandTime + holdTime + fadeTime;
                bool done = age >= life || (r.dropAt >= 0f && now - r.dropAt >= dropFade);
                if (done) { Recycle(r); rings.RemoveAt(i); continue; }
                if (r.units.Count > 0) r.center = Middle(r.units);
                Timing(r, age, now);                                                                          // the one burst / hold / fade of every ring (Timing)
            }
            for (int i = 0; i < rings.Count; i++) Draw(rings[i], now, now - rings[i].t0, cam);                 // the one drawing of every ring (Draw)
        }

        Vector3 Middle(List<ZObject> us)
        {
            var s = Vector3.zero;
            foreach (var o in us) s += game.Views.TryGetValue(o.Id, out var v) && v ? v.transform.position : game.CenterWorld(o, false);
            s /= us.Count; s.y = 0f;
            return s;
        }

        // ------------------------------------------------------------------ rings for a selection
        void Rebuild(List<ZObject> sel)
        {
            float now = Time.unscaledTime;
            foreach (var r in rings) Recycle(r);
            rings.Clear();
            foreach (var o in sel)
            {
                if (o == null || o.Destroyed || o.AttackRadius <= 0) continue;                                // zobject.cpp:716: no radius, no ring (crane, empty APC)
                // a gun (own or not) gets this very ring, as every unit: one ring for all (the gun being placed: Placement)
                var p = game.Views.TryGetValue(o.Id, out var v) && v ? v.transform.position : game.CenterWorld(o, false); p.y = 0f;
                Ring best = null; float bd = mergeDistance * mergeDistance;
                foreach (var r in rings)
                {
                    if (r.type != o.Type || r.team != o.Owner || r.radiusPx != o.AttackRadius) continue;
                    var d = Middle(r.units) - p; float dd = d.x * d.x + d.z * d.z;
                    if (dd <= bd) { bd = dd; best = r; }
                }
                if (best != null) { best.units.Add(o); continue; }
                var n = pool.Count > 0 ? pool.Pop() : new Ring();
                Begin(n, o.Type, o.Owner, o.AttackRadius, now);                                                // the one set-up of every ring (Begin)
                n.units.Add(o);
                rings.Add(n);
            }
            foreach (var r in rings) r.center = Middle(r.units);
        }

        // ------------------------------------------------------------------ one ring (every ring: a selected unit's, a selected gun's, the gun being placed)
        /// <summary>A ring's set-up: radius = the range + 3 px, the dots about dotSpacing apart.</summary>
        void Begin(Ring n, string type, Team team, int radiusPx, float now)
        {
            n.type = type; n.team = team; n.radiusPx = radiusPx;
            n.radius = (radiusPx + 3) / 8f;                                                                    // zobject.cpp:739-740 (attack_radius + 3) px, 8 px = 1 m
            n.units.Clear();
            n.t0 = now; n.dropAt = -1f; n.hR = -1f; n.live = false;
            int want = Mathf.Clamp(Mathf.RoundToInt(2f * Mathf.PI * n.radius / Mathf.Max(0.2f, dotSpacing) / 4f) * 4, minDots, maxDots);
            Alloc(n, want);
        }

        /// <summary>A ring's burst, hold and fade at age (s): the radius grows out of the middle, the alpha follows.</summary>
        void Timing(Ring r, float age, float now)
        {
            float k = Mathf.Clamp01(age / Mathf.Max(0.01f, expandTime));
            float ease = 1f - Mathf.Pow(1f - k, 3f);                                                       // out-cubic: bursts out, settles
            r.curR = r.radius * (0.1f + 0.9f * ease);
            float a = Mathf.Clamp01(age / Mathf.Max(0.01f, expandTime * 0.5f));                            // fade in with the burst
            float fadeK = Mathf.Clamp01((age - expandTime - holdTime) / Mathf.Max(0.01f, fadeTime));
            a *= 1f - fadeK * fadeK;
            if (r.dropAt >= 0f) a *= 1f - Mathf.Clamp01((now - r.dropAt) / Mathf.Max(0.01f, dropFade));
            r.a = a * alpha;
            r.live = true;
        }

        /// <summary>A ring's dots this frame: sized by the zoom, swelling as the ring lands, in the team's material.</summary>
        void Draw(Ring r, float now, float age, Camera cam)
        {
            float camDist = cam ? Vector3.Distance(cam.transform.position, r.center) : 40f;
            float zs = Mathf.Clamp(camDist / 40f, zoomScale.x, zoomScale.y);
            float ping = 1f + 0.45f * (1f - Mathf.Clamp01((age - expandTime * 0.6f) / 0.3f)) * Mathf.Clamp01(age / Mathf.Max(0.01f, expandTime * 0.6f));   // dots swell as the ring lands
            DrawBlind(r, age, zs);                                                                         // a gun's blind sectors, with this ring (AttackRangeRings.BlindSectors.cs); first, so the dots over a blind arc are dimmed (K333b)
            Build(r, now, zs * ping);
            var m = Mat(r.team);
            if (m) Graphics.DrawMesh(r.mesh, Matrix4x4.identity, m, 0, null, 0, null, ShadowCastingMode.Off, false);
            DrawBlindGreyDots(r);                                                                          // K333c: the grey dots over the blind arcs, right after the ring (AttackRangeRings.BlindClearer.cs)
        }

        void DropAll() { float now = Time.unscaledTime; foreach (var r in rings) if (r.dropAt < 0f) r.dropAt = now; }

        void Recycle(Ring r) { r.units.Clear(); pool.Push(r); }

        void Alloc(Ring r, int n)
        {
            if (r.mesh && r.n == n) return;
            r.n = n;
            if (!r.mesh) { r.mesh = new Mesh { name = "OZ_RangeRing" }; r.mesh.MarkDynamic(); }
            r.mesh.Clear();
            r.v = new Vector3[n * V]; r.c = new Color32[n * V]; r.h = new float[n];
            var tri = new int[n * T * 3];
            for (int d = 0, t = 0; d < n; d++)
            {
                int b = d * V;
                for (int i = 0; i < 6; i++)
                {
                    int i1 = (i + 1) % 6;
                    tri[t++] = b; tri[t++] = b + 1 + i1; tri[t++] = b + 1 + i;                                // core fan
                    tri[t++] = b + 1 + i; tri[t++] = b + 1 + i1; tri[t++] = b + 7 + i;                        // feather strip
                    tri[t++] = b + 1 + i1; tri[t++] = b + 7 + i1; tri[t++] = b + 7 + i;
                }
            }
            r.mesh.vertices = r.v; r.mesh.colors32 = r.c; r.mesh.triangles = tri;
        }

        // ------------------------------------------------------------------ mesh
        /// <summary>Ground height at a ring angle from the fixed sample grid: lerp (follows slopes) and max of the two neighbours
        /// (a dot centre never sinks into a rise between two samples).</summary>
        static void GridH(float[] h, float ang, out float lerp, out float max)
        {
            int g = h.Length;
            float f = Mathf.Repeat(ang, 2f * Mathf.PI) / (2f * Mathf.PI) * g;
            int i0 = Mathf.FloorToInt(f) % g, i1 = (i0 + 1) % g; float t = f - Mathf.Floor(f);
            lerp = Mathf.Lerp(h[i0], h[i1], t); max = Mathf.Max(h[i0], h[i1]);
        }

        void Build(Ring r, float now, float size)
        {
            int n = r.n; if (n == 0) return;
            float phase = now * crawlDegPerSec * Mathf.Deg2Rad;                                               // Zod radius_i crawl
            float step = 2f * Mathf.PI / n;
            // ground heights on a FIXED grid, one sample per dot spacing (the dots crawl over it): re-sampled while the ring grows or
            // its units move, else cached (GroundY is a ray cast: terrain + bridge decks)
            int g = r.h.Length;
            if (Mathf.Abs(r.curR - r.hR) > 0.05f || (r.center - r.hAt).sqrMagnitude > 0.09f)
            {
                r.hR = r.curR; r.hAt = r.center;
                for (int i = 0; i < g; i++)
                {
                    float ga = i * 2f * Mathf.PI / g;
                    r.h[i] = game.GroundY(r.center.x + Mathf.Cos(ga) * r.curR, r.center.z + Mathf.Sin(ga) * r.curR);
                }
            }
            float len = dotLength * 0.5f * size, wid = dotWidth * 0.5f * size;
            float up = lift + 0.35f * wid;                                                                    // + a little for a sideways slope under a big (far zoom) dot
            float dAng = r.curR > 0.01f ? len / r.curR : 0f;
            byte core = (byte)Mathf.RoundToInt(Mathf.Clamp01(r.a) * 255f);
            var c0 = new Color32(255, 255, 255, 0);
            for (int d = 0; d < n; d++)
            {
                float ang = phase + d * step;
                var radial = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang));
                var tangent = new Vector3(-radial.z, 0f, radial.x);
                var p = r.center + radial * r.curR;
                GridH(r.h, ang, out _, out float hc);
                p.y = hc + up;
                byte a = core;
                if (unionSameType && Covered(r, p)) a = 0;
                var ca = BlindRingDotGrey(r, d, ang, a);                                                   // K333b / K333c: over a blind arc of its gun the dot fades out for its grey twin (AttackRangeRings.BlindClearer.cs)
                int b = d * V;
                r.v[b] = p; r.c[b] = ca;
                for (int i = 0; i < 6; i++)
                {
                    var o = tangent * (Hex[i].x * len) + radial * (Hex[i].y * wid);
                    // each vertex follows the slope along the ring, never below the dot centre
                    GridH(r.h, ang + Hex[i].x * dAng, out float hl, out _);
                    float y = Mathf.Max(hc, hl) + up;
                    var q = p + o * 0.62f; q.y = y; r.v[b + 1 + i] = q; r.c[b + 1 + i] = ca;                   // solid core
                    q = p + o * 1.15f; q.y = y; r.v[b + 7 + i] = q; r.c[b + 7 + i] = BlindRim(ca, c0);         // soft rim (blooms softly; K333b: in the dot's own colour)
                }
            }
            r.mesh.vertices = r.v; r.mesh.colors32 = r.c;
            r.mesh.RecalculateBounds();
        }

        /// <summary>Zod WithinAttackRadiusOf (zobject.cpp:877-885), limited to rings of the same type and team.</summary>
        bool Covered(Ring self, Vector3 p)
        {
            foreach (var o in rings)
            {
                if (o == self || !o.live || o.type != self.type || o.team != self.team) continue;
                float dx = p.x - o.center.x, dz = p.z - o.center.z;
                if (dx * dx + dz * dz < o.curR * o.curR) return true;
            }
            return false;
        }
    }
}
